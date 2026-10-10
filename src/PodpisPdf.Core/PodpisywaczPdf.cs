using System.Security.Cryptography.X509Certificates;
using iText.Bouncycastleconnector;
using iText.Commons.Bouncycastle.Cert;
using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Signatures;
using Path = System.IO.Path;
using FileStream = System.IO.FileStream;

namespace PodpisPdf.Core;

public sealed record WynikPodpisu(string SciezkaWyniku, DateTimeOffset CzasNaWizualizacji, bool CzasZSerwera);

/// <summary>Składanie podpisu PAdES (Baseline B-T, opcjonalnie B-LT) z widoczną wizualizacją.</summary>
public sealed class PodpisywaczPdf
{
    private readonly ITSAClient _tsa;
    private readonly Func<DateTimeOffset?> _czasSerwera;

    /// <param name="tsa">Klient serwera znacznika czasu.</param>
    /// <param name="czasSerwera">
    /// Źródło czasu do wizualizacji; domyślnie czas pobrany z serwera znacznika czasu.
    /// Gdy zwróci null, używany jest zegar komputera.
    /// </param>
    public PodpisywaczPdf(ITSAClient tsa, Func<DateTimeOffset?>? czasSerwera = null)
    {
        _tsa = tsa;
        _czasSerwera = czasSerwera ?? (() => KlientZnacznikaCzasu.PobierzCzas(tsa));
    }

    /// <summary>Ścieżka pliku wynikowego: <c>wniosek.pdf</c> → <c>wniosek_podpisany.pdf</c>.</summary>
    public static string NazwaPlikuWynikowego(string wejscie, string przyrostek)
    {
        if (string.IsNullOrEmpty(przyrostek))
            przyrostek = "_podpisany";
        var folder = Path.GetDirectoryName(Path.GetFullPath(wejscie)) ?? "";
        var nazwa = Path.GetFileNameWithoutExtension(wejscie);
        var rozszerzenie = Path.GetExtension(wejscie);
        if (string.IsNullOrEmpty(rozszerzenie))
            rozszerzenie = ".pdf";
        return Path.Combine(folder, nazwa + przyrostek + rozszerzenie);
    }

    public WynikPodpisu Podpisz(string wejscie, string wyjscie, X509Certificate2 certyfikat, OpcjePodpisu opcje)
    {
        if (!File.Exists(wejscie))
            throw new BladPodpisu($"Nie znaleziono pliku: {wejscie}");
        if (string.Equals(Path.GetFullPath(wejscie), Path.GetFullPath(wyjscie), StringComparison.OrdinalIgnoreCase))
            throw new BladPodpisu("Plik wynikowy musi być inny niż oryginał.");
        if (!certyfikat.HasPrivateKey)
            throw new BladPodpisu("Wybrany certyfikat nie ma klucza prywatnego.");

        var podpis = new PodpisKluczemCertyfikatu(certyfikat, opcje.PrzygotujKlucz);
        var (numerStrony, prostokat) = UstalMiejscePodpisu(wejscie, opcje);
        var lancuch = ZbudujLancuch(certyfikat);

        var czasSerwera = _czasSerwera();
        var czas = czasSerwera ?? DateTimeOffset.Now;

        var dane = new DaneWizualizacji(
            CertyfikatInfo.PobierzImieNazwisko(certyfikat), opcje.Stanowisko, opcje.SciezkaLogo, czas);
        var wyglad = Wizualizacja.Utworz(dane, opcje.Miejsce?.Skala ?? 1f);

        var wlasciwosci = new SignerProperties()
            .SetPageNumber(numerStrony)
            .SetPageRect(prostokat)
            .SetSignatureAppearance(wyglad)
            .SetClaimedSignDate(czas.UtcDateTime)
            .SetSignatureCreator("PodpisPdf");

        var folder = Path.GetDirectoryName(Path.GetFullPath(wyjscie))!;
        var tymczasowy = Path.Combine(folder, $".{Path.GetFileName(wyjscie)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var czytnik = new PdfReader(wejscie))
            using (var strumien = new FileStream(tymczasowy, FileMode.CreateNew, FileAccess.Write))
            {
                var padesSigner = new PdfPadesSigner(czytnik, strumien);
                if (opcje.PoziomLT)
                {
                    padesSigner
                        .SetOcspClient(new OcspClientBouncyCastle())
                        .SetCrlClient(new CrlClientOnline());
                    padesSigner.SignWithBaselineLTProfile(wlasciwosci, lancuch, podpis, _tsa);
                }
                else
                {
                    padesSigner.SignWithBaselineTProfile(wlasciwosci, lancuch, podpis, _tsa);
                }
            }

            File.Move(tymczasowy, wyjscie, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(tymczasowy))
                    File.Delete(tymczasowy);
            }
            catch (IOException)
            {
                // Pozostawiony plik tymczasowy nie jest błędem podpisu.
            }
        }

        return new WynikPodpisu(wyjscie, czas, czasSerwera != null);
    }

    private static (int Numer, Rectangle Prostokat) UstalMiejscePodpisu(string wejscie, OpcjePodpisu opcje)
    {
        using var czytnik = new PdfReader(wejscie);
        using var dokument = new PdfDocument(czytnik);
        var liczbaStron = dokument.GetNumberOfPages();

        if (opcje.Miejsce is { } miejsce)
        {
            if (miejsce.NumerStrony < 1 || miejsce.NumerStrony > liczbaStron)
                throw new BladPodpisu($"Dokument ma {liczbaStron} str., a wskazano stronę {miejsce.NumerStrony}.");
            var cropBox = dokument.GetPage(miejsce.NumerStrony).GetCropBox();
            return (miejsce.NumerStrony, Wizualizacja.ProstokatWMiejscu(cropBox, miejsce));
        }

        var polozenie = opcje.Polozenie;

        var numer = polozenie.Strona switch
        {
            WyborStrony.Pierwsza => 1,
            WyborStrony.Ostatnia => liczbaStron,
            _ => polozenie.NumerStrony,
        };
        if (numer < 1 || numer > liczbaStron)
            throw new BladPodpisu(
                $"Dokument ma {liczbaStron} str., a w ustawieniach wybrano stronę {numer} na podpis. Popraw numer strony w ustawieniach.");

        var strona = dokument.GetPage(numer).GetCropBox();
        return (numer, Wizualizacja.ObliczProstokat(strona, polozenie));
    }

    /// <summary>Certyfikat podpisującego i (o ile system je zna) certyfikaty wystawców.</summary>
    private static IX509Certificate[] ZbudujLancuch(X509Certificate2 certyfikat)
    {
        var certyfikaty = new List<X509Certificate2> { certyfikat };
        using (var lancuch = new X509Chain())
        {
            lancuch.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            lancuch.ChainPolicy.VerificationFlags = X509VerificationFlags.AllFlags;
            lancuch.Build(certyfikat);
            foreach (var element in lancuch.ChainElements.Skip(1))
                certyfikaty.Add(element.Certificate);
        }

        var fabryka = BouncyCastleFactoryCreator.GetFactory();
        return certyfikaty
            .Select(c => fabryka.CreateX509Certificate(new MemoryStream(c.RawData)))
            .ToArray();
    }
}
