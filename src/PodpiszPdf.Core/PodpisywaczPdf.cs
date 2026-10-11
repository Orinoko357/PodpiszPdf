using System.Security.Cryptography.X509Certificates;
using iText.Bouncycastleconnector;
using iText.Commons.Bouncycastle.Cert;
using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Signatures;
using Path = System.IO.Path;
using FileStream = System.IO.FileStream;

namespace PodpiszPdf.Core;

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
        if (!certyfikat.HasPrivateKey)
            throw new BladPodpisu("Wybrany certyfikat nie ma klucza prywatnego.");
        // Lista certyfikatów mogła powstać wiele dni temu (program działa w zasobniku).
        if (CertyfikatInfo.PowodNiewaznosci(certyfikat, DateTime.Now) is { } niewazny)
            throw new BladPodpisu(niewazny);

        // Przed znacznikiem czasu: dokument, którego nie da się podpisać, nie wymaga połączenia z serwerem.
        var dokument = SprawdzDokument(wejscie);

        var podpis = new PodpisKluczemCertyfikatu(certyfikat, opcje.PrzygotujKlucz);
        var czasSerwera = _czasSerwera();
        var czas = czasSerwera ?? DateTimeOffset.Now;

        var dane = new DaneWizualizacji(
            CertyfikatInfo.PobierzImieNazwisko(certyfikat), opcje.Stanowisko, opcje.SciezkaLogo, czas);
        var (numerStrony, prostokat) = UstalMiejscePodpisu(dokument.Czytnik(), opcje, Wizualizacja.Rozmiar(dane));
        var lancuch = ZbudujLancuch(certyfikat);

        var wyglad = Wizualizacja.Utworz(dane, opcje.Miejsce?.Skala ?? 1f, bezPrzezroczystosci: dokument.PdfA1);

        var wlasciwosci = new SignerProperties()
            .SetPageNumber(numerStrony)
            .SetPageRect(prostokat)
            .SetSignatureAppearance(wyglad)
            .SetClaimedSignDate(czas.UtcDateTime)
            .SetSignatureCreator("PodpiszPdf");

        var folder = Path.GetDirectoryName(Path.GetFullPath(wyjscie))!;
        var tymczasowy = Path.Combine(folder, $".{Path.GetFileName(wyjscie)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var czytnik = dokument.Czytnik())
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

            // Wynik może zastąpić oryginał (wyjscie == wejscie): czytnik oryginału jest już zamknięty, a podpisany
            // plik powstał obok jako tymczasowy, więc przy błędzie oryginał zostaje nienaruszony.
            try
            {
                File.Move(tymczasowy, wyjscie, overwrite: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                throw new BladPodpisu($"Nie można zapisać pliku {Path.GetFileName(wyjscie)} — jest otwarty w innym programie " +
                                      "(np. w przeglądarce PDF) albo tylko do odczytu. Zamknij go i spróbuj ponownie.", e);
            }
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

    /// <summary>Dokument do podpisu: oryginał albo jego naprawiona kopia w pamięci.</summary>
    private sealed record DokumentDoPodpisu(string Sciezka, byte[]? NaprawionaKopia, bool PdfA1)
    {
        public PdfReader Czytnik() =>
            NaprawionaKopia != null ? new PdfReader(new MemoryStream(NaprawionaKopia)) : new PdfReader(Sciezka);
    }

    /// <summary>
    /// Sprawdza, czy dokument da się podpisać w trybie dopisywania (bez naruszania wcześniejszych podpisów):
    /// uprawnienia, certyfikacja „bez zmian”, uszkodzona tabela xref; rozpoznaje PDF/A-1.
    /// </summary>
    private static DokumentDoPodpisu SprawdzDokument(string wejscie)
    {
        using var czytnik = new PdfReader(wejscie);
        using var dokument = new PdfDocument(czytnik);

        // Bez hasła właściciela iText nie dopisze podpisu — a obejście ograniczeń autora nie wchodzi w grę.
        if (czytnik.IsEncrypted() && !czytnik.IsOpenedWithFullPermission())
            throw new BladPodpisu(KomunikatyBledow.ZablokowanyDoZmian);

        if (CzyCertyfikacjaBezZmian(dokument))
        {
            throw new BladPodpisu(
                "Autor dokumentu zablokował wszelkie zmiany (certyfikacja „bez zmian”) — kolejny podpis unieważniłby certyfikację.");
        }

        var pdfA1 = dokument.GetConformance()?.GetAConformance()?.GetPart() == "1";

        if (!czytnik.HasRebuiltXref())
            return new DokumentDoPodpisu(wejscie, null, pdfA1);

        // Uszkodzona tabela xref (częste przy skanerach): iText odczytał plik, odbudowując ją, ale nie dopisze
        // do niego podpisu. Bez wcześniejszych podpisów wystarczy podpisać zapisaną od nowa (naprawioną) kopię;
        // z podpisami przepisanie pliku unieważniłoby je.
        if (new SignatureUtil(dokument).GetSignatureNames().Count > 0)
        {
            throw new BladPodpisu(
                "Plik PDF jest uszkodzony (błędna tabela odsyłaczy xref) i ma już podpisy. Nie można dodać kolejnego podpisu " +
                "bez unieważnienia poprzednich — poproś nadawcę o poprawny plik.");
        }
        using var naprawiony = new MemoryStream();
        using (var czytnikKopii = new PdfReader(wejscie))
        using (new PdfDocument(czytnikKopii, new PdfWriter(naprawiony)))
        {
            // Zamknięcie dokumentu zapisuje go od nowa, z poprawną tabelą xref.
        }
        return new DokumentDoPodpisu(wejscie, naprawiony.ToArray(), pdfA1);
    }

    /// <summary>Czy dokument ma certyfikację DocMDP z P=1 (żadne zmiany niedozwolone).</summary>
    private static bool CzyCertyfikacjaBezZmian(PdfDocument dokument)
    {
        var podpis = dokument.GetCatalog().GetPdfObject().GetAsDictionary(PdfName.Perms)?.GetAsDictionary(PdfName.DocMDP);
        var odwolania = podpis?.GetAsArray(PdfName.Reference);
        if (odwolania == null)
            return false;
        for (var i = 0; i < odwolania.Size(); i++)
        {
            var odwolanie = odwolania.GetAsDictionary(i);
            if (odwolanie == null || !PdfName.DocMDP.Equals(odwolanie.GetAsName(PdfName.TransformMethod)))
                continue;
            // Brak /P oznacza wartość domyślną 2 (wypełnianie formularzy i podpisy dozwolone).
            var p = odwolanie.GetAsDictionary(PdfName.TransformParams)?.GetAsNumber(PdfName.P)?.IntValue() ?? 2;
            if (p == 1)
                return true;
        }
        return false;
    }

    private static (int Numer, Rectangle Prostokat) UstalMiejscePodpisu(PdfReader czytnik, OpcjePodpisu opcje, RozmiarWizualizacji rozmiar)
    {
        using var dokument = new PdfDocument(czytnik);
        var liczbaStron = dokument.GetNumberOfPages();

        if (opcje.Miejsce is { } miejsce)
        {
            if (miejsce.NumerStrony < 1 || miejsce.NumerStrony > liczbaStron)
                throw new BladPodpisu($"Dokument ma {liczbaStron} str., a wskazano stronę {miejsce.NumerStrony}.");
            var widoczna = DokumentPdf.ObszarStrony(dokument.GetPage(miejsce.NumerStrony));
            return (miejsce.NumerStrony, Wizualizacja.ProstokatWMiejscu(widoczna, miejsce, rozmiar));
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

        var strona = DokumentPdf.ObszarStrony(dokument.GetPage(numer));
        return (numer, Wizualizacja.ObliczProstokat(strona, polozenie, rozmiar));
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
