using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using iText.Kernel.Pdf;
using iText.Layout;
using iText.Layout.Element;
using PodpisPdf.Core;
using PodpisPdf.Fluent;

namespace PodpisPdf;

/// <summary>
/// Zrzuty ekranu wszystkich okien w obu motywach z przykładowymi danymi (GitHub Actions: <c>--zrzuty folder</c>).
/// Nie dotyka magazynu certyfikatów, rejestru ani ustawień użytkownika.
/// </summary>
internal static class Zrzuty
{
    public static bool Aktywne { get; private set; }

    public static void Wykonaj(string folder)
    {
        Aktywne = true;
        Directory.CreateDirectory(folder);
        var pdf = UtworzPdf(Path.Combine(folder, "wniosek_o_dofinansowanie.pdf"));
        var certyfikaty = Certyfikaty();

        foreach (var (ciemny, nazwa) in new[] { (false, "jasny"), (true, "ciemny") })
        {
            Motyw.Wymus(ciemny);

            using (var glowne = new OknoGlowne(null) { StartPosition = FormStartPosition.Manual, Location = new Point(0, 0) })
            {
                glowne.Show();
                glowne.PokazDanePrzykladowe(null, certyfikaty);
                Zapisz(glowne, Path.Combine(folder, $"1-glowne-pusty-{nazwa}.png"));
                glowne.PokazDanePrzykladowe(pdf, certyfikaty);
                Zapisz(glowne, Path.Combine(folder, $"2-glowne-{nazwa}.png"), czekajMs: 2500);
                glowne.Close();
            }

            var dane = new DaneWizualizacji("Mateusz Kozielski", "Kierownik Biura Informatyki\nUrzędu Miasta Wodzisławia Śląskiego", null, DateTimeOffset.Now);
            using (var podglad = OknoPodgladu.Utworz(pdf, dane, new PolozenieWizualizacji(), 1f))
            {
                podglad.StartPosition = FormStartPosition.Manual;
                podglad.Location = new Point(0, 0);
                podglad.Size = new Size(1100, 780);
                podglad.Show();
                Zapisz(podglad, Path.Combine(folder, $"3-podglad-{nazwa}.png"), czekajMs: 1500);
                podglad.Close();
            }

            var ustawienia = new Ustawienia { Stanowisko = "Kierownik Biura Informatyki\nUrzędu Miasta Wodzisławia Śląskiego", Autostart = true };
            using (var okno = new OknoUstawien(ustawienia) { StartPosition = FormStartPosition.Manual, Location = new Point(0, 0) })
            {
                okno.Show();
                Zapisz(okno, Path.Combine(folder, $"4-ustawienia-wizualizacja-{nazwa}.png"), czekajMs: 1500);
                okno.PokazStrone(2);
                Zapisz(okno, Path.Combine(folder, $"5-ustawienia-program-{nazwa}.png"));
                okno.Close();
            }
        }
    }

    private static void Zapisz(Form okno, string sciezka, int czekajMs = 800)
    {
        var koniec = Environment.TickCount64 + czekajMs;
        while (Environment.TickCount64 < koniec)
        {
            Application.DoEvents();
            Thread.Sleep(30);
        }
        okno.Refresh();
        using var bitmapa = new Bitmap(okno.Width, okno.Height);
        okno.DrawToBitmap(bitmapa, new Rectangle(0, 0, okno.Width, okno.Height));
        bitmapa.Save(sciezka, System.Drawing.Imaging.ImageFormat.Png);
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
