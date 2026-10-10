using System.Formats.Asn1;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using iText.Kernel.Pdf;
using iText.Layout;
using iText.Layout.Element;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using PodpisPdf.Core;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using Windows.UI;

namespace PodpisPdf;

/// <summary>
/// Zrzuty ekranu wszystkich stron w obu motywach z przykładowymi danymi (GitHub Actions: <c>--zrzuty folder</c>).
/// Nie dotyka magazynu certyfikatów, rejestru ani ustawień użytkownika.
/// </summary>
internal static class Zrzuty
{
    public static async Task WykonajAsync(OknoGlowne okno, string folder)
    {
        Directory.CreateDirectory(folder);
        var pdf = UtworzPdf(Path.Combine(folder, "wniosek_o_dofinansowanie.pdf"));
        var certyfikaty = Certyfikaty();
        var korzen = okno.Korzen_;
        var ramka = okno.Ramka_;
        var dane = new DaneWizualizacji("Mateusz Kozielski", "Kierownik Biura Informatyki\nUrzędu Miasta Wodzisławia Śląskiego", null, DateTimeOffset.Now);

        foreach (var (motyw, nazwa) in new[] { (ElementTheme.Light, "jasny"), (ElementTheme.Dark, "ciemny") })
        {
            korzen.RequestedTheme = motyw;
            // Mica nie trafia do RenderTargetBitmap — w trybie testowym tło jest jednolite w kolorach Mica.
            korzen.Background = new SolidColorBrush(motyw == ElementTheme.Dark ? Color.FromArgb(255, 0x20, 0x20, 0x20) : Color.FromArgb(255, 0xF3, 0xF3, 0xF3));

            var glowna = okno.StronaGlowna!;
            glowna.PokazDanePrzykladowe(null, certyfikaty);
            await Zapisz(korzen, Path.Combine(folder, $"1-glowne-pusty-{nazwa}.png"));
            glowna.PokazDanePrzykladowe(pdf, certyfikaty);
            await Zapisz(korzen, Path.Combine(folder, $"2-glowne-{nazwa}.png"), czekajMs: 2500);

            var zadanie = await Task.Run(() => ZadaniePodgladu.Przygotuj(pdf, dane, new PolozenieWizualizacji(), 1f));
            ramka.Navigate(typeof(StronaPodgladu), zadanie);
            await Zapisz(korzen, Path.Combine(folder, $"3-podglad-{nazwa}.png"), czekajMs: 2500);
            ramka.GoBack();

            var ustawienia = new Ustawienia { Stanowisko = dane.Stanowisko, Autostart = true };
            ramka.Navigate(typeof(StronaUstawien), ustawienia);
            var strona = (StronaUstawien)ramka.Content;
            await Zapisz(korzen, Path.Combine(folder, $"4-ustawienia-wizualizacja-{nazwa}.png"), czekajMs: 1500);
            strona.PokazSekcje(1);
            await Zapisz(korzen, Path.Combine(folder, $"5-ustawienia-znacznik-{nazwa}.png"));
            strona.PokazSekcje(2);
            await Zapisz(korzen, Path.Combine(folder, $"6-ustawienia-program-{nazwa}.png"));
            ramka.GoBack();
            await Task.Delay(300);
        }
    }

    private static async Task Zapisz(FrameworkElement element, string sciezka, int czekajMs = 800)
    {
        await Task.Delay(czekajMs);
        var bitmapa = new RenderTargetBitmap();
        await bitmapa.RenderAsync(element);
        var piksele = await bitmapa.GetPixelsAsync();
        using var strumien = new InMemoryRandomAccessStream();
        var koder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, strumien);
        koder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
            (uint)bitmapa.PixelWidth, (uint)bitmapa.PixelHeight, 96, 96, piksele.ToArray());
        await koder.FlushAsync();
        strumien.Seek(0);
        await using var plik = File.Create(sciezka);
        await strumien.AsStreamForRead().CopyToAsync(plik);
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
        CertyfikatInfo Utworz(string wystawca, bool kwalifikowany, int lat)
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
            return new CertyfikatInfo(cert);
        }

        return new[] { Utworz("Certum QCA G3 R35", true, 2), Utworz("Certum Digital ID CA SHA2", false, 1) };
    }
}
