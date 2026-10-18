using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Styling;
using Avalonia.Threading;
using iText.Kernel.Pdf;
using iText.Layout;
using iText.Layout.Element;
using PodpiszPdf.Core;

namespace PodpiszPdf.Mac;

/// <summary>
/// Zrzuty ekranu wszystkich stron w obu motywach z przykładowymi danymi (<c>--zrzuty folder</c>), bez okna na ekranie
/// (Avalonia.Headless z rysowaniem Skia). Nie dotyka kart, pęku kluczy ani ustawień użytkownika.
/// </summary>
internal static class Zrzuty
{
    public static void Wykonaj(string folder)
    {
        AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .WithInterFont()
            .SetupWithoutStarting();

        using var koniec = new CancellationTokenSource();
        Exception? blad = null;
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                await WykonajAsync(folder);
            }
            catch (Exception e)
            {
                blad = e;
            }
            finally
            {
                koniec.Cancel();
            }
        });
        Dispatcher.UIThread.MainLoop(koniec.Token);
        if (blad != null)
            throw new InvalidOperationException("Zrzuty ekranu: " + blad.Message, blad);
    }

    private static async Task WykonajAsync(string folder)
    {
        Directory.CreateDirectory(folder);
        var pdf = UtworzPdf(Path.Combine(folder, "wniosek_o_dofinansowanie.pdf"));
        var certyfikaty = Certyfikaty();
        var dane = new DaneWizualizacji("Mateusz Kozielski", "Kierownik Biura Informatyki\nUrzędu Miasta Wodzisławia Śląskiego", null, DateTimeOffset.Now);

        foreach (var (motyw, nazwa) in new[] { (ThemeVariant.Light, "jasny"), (ThemeVariant.Dark, "ciemny") })
        {
            Application.Current!.RequestedThemeVariant = motyw;
            var ustawienia = new Ustawienia { Stanowisko = dane.Stanowisko };
            var okno = new OknoGlowne(ustawienia, trybTestowy: true) { Width = 780, Height = 700 };
            okno.Show();

            var glowna = okno.StronaGlowna;
            glowna.PokazDanePrzykladowe(null, certyfikaty);
            await Zapisz(okno, Path.Combine(folder, $"mac-1-glowne-pusty-{nazwa}.png"));
            glowna.PokazDanePrzykladowe(pdf, certyfikaty);
            await Zapisz(okno, Path.Combine(folder, $"mac-2-glowne-{nazwa}.png"), czekajMs: 1500);

            var zadanie = await WatekRoboczy.Uruchom(() => ZadaniePodgladu.Przygotuj(pdf, dane, new PolozenieWizualizacji(), 1f));
            okno.Width = 1100;
            okno.Height = 820;
            okno.PokazPodglad(new StronaPodgladu(okno, zadanie), pdf);
            await Zapisz(okno, Path.Combine(folder, $"mac-3-podglad-{nazwa}.png"), czekajMs: 2500);
            okno.PokazStroneGlowna();
            okno.Width = 780;
            okno.Height = 700;

            okno.OtworzUstawienia();
            var strona = (StronaUstawien)okno.Tresc.Content!;
            await Zapisz(okno, Path.Combine(folder, $"mac-4-ustawienia-wizualizacja-{nazwa}.png"), czekajMs: 1500);
            string[] sekcje = { "znacznik", "karta", "program", "informacje" };
            for (var i = 0; i < sekcje.Length; i++)
            {
                strona.PokazSekcje(i + 1);
                await Zapisz(okno, Path.Combine(folder, $"mac-{5 + i}-ustawienia-{sekcje[i]}-{nazwa}.png"));
            }
            okno.PokazStroneGlowna();

            var pin = new OknoPin();
            pin.PokazPrzyklad("Certum QCA (profil bezpieczny)", "Mateusz Kozielski", "Uwaga: została ostatnia próba — błędny PIN zablokuje kartę.");
            pin.Show();
            await Zapisz(pin, Path.Combine(folder, $"mac-9-pin-{nazwa}.png"));
            pin.Close();
            okno.Close();
        }
    }

    private static async Task Zapisz(Window okno, string sciezka, int czekajMs = 600)
    {
        await Task.Delay(czekajMs);
        Dispatcher.UIThread.RunJobs();
        var obraz = okno.CaptureRenderedFrame() ?? throw new InvalidOperationException($"Brak obrazu okna ({Path.GetFileName(sciezka)}).");
        obraz.Save(sciezka);
    }

    private static string UtworzPdf(string sciezka)
    {
        using (var dokument = new PdfDocument(new PdfWriter(sciezka)))
        using (var uklad = new Document(dokument))
        {
            for (var s = 1; s <= 3; s++)
            {
                if (s > 1) uklad.Add(new AreaBreak());
                uklad.Add(new Paragraph($"Wniosek o dofinansowanie — strona {s}").SetFontSize(18));
                for (var i = 0; i < 14; i++)
                    uklad.Add(new Paragraph("Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua."));
            }
        }
        return sciezka;
    }

    private static IReadOnlyList<CertyfikatInfo> Certyfikaty()
    {
        CertyfikatInfo Utworz(string wystawca, bool kwalifikowany, int lat, ZrodloPkcs11? karta)
        {
            using var kluczCa = RSA.Create(2048);
            var ca = new CertificateRequest($"CN={wystawca}, O=Asseco Data Systems S.A., C=PL", kluczCa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            ca.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
            using var certCa = ca.CreateSelfSigned(DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddYears(5));

            using var klucz = RSA.Create(2048);
            var nazwa = new X500DistinguishedNameBuilder();
            nazwa.AddCountryOrRegion("PL");
            nazwa.Add("2.5.4.42", "Mateusz");
            nazwa.Add("2.5.4.4", "Kozielski");
            nazwa.AddCommonName("Mateusz Kozielski");
            var zadanie = new CertificateRequest(nazwa.Build(), klucz, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            zadanie.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.NonRepudiation, true));
            if (kwalifikowany)
            {
                var w = new AsnWriter(AsnEncodingRules.DER);
                using (w.PushSequence())
                using (w.PushSequence())
                    w.WriteObjectIdentifier("0.4.0.1862.1.1");
                zadanie.CertificateExtensions.Add(new X509Extension("1.3.6.1.5.5.7.1.3", w.Encode(), false));
            }
            var cert = zadanie.Create(certCa, DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddYears(lat), new byte[] { 1, 2, 3, (byte)lat });
            return new CertyfikatInfo(cert, karta);
        }

        var karta = new ZrodloPkcs11("/usr/local/lib/libcrypto3PKCS.dylib", "0000", "Certum QCA", new byte[] { 1 }, false);
        return new[] { Utworz("Certum QCA G3 R35", true, 2, karta), Utworz("Certum Digital ID CA SHA2", false, 1, null) };
    }
}
