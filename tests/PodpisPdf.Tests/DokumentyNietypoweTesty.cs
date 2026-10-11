using System.Text;
using iText.Bouncycastleconnector;
using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas;
using iText.Layout;
using iText.Layout.Element;
using iText.Signatures;
using PodpisPdf.Core;
using Xunit;
using Path = System.IO.Path;

namespace PodpisPdf.Tests;

/// <summary>Dokumenty spotykane w praktyce: PDF/A, certyfikowane, uszkodzone, zabezpieczone (audyt 2026-10).</summary>
public class DokumentyNietypoweTesty
{
    private static void PodpisJestPoprawny(string sciezka, int liczbaPodpisow = 1)
    {
        using var pdf = new PdfDocument(new PdfReader(sciezka));
        var su = new SignatureUtil(pdf);
        var nazwy = su.GetSignatureNames();
        Assert.Equal(liczbaPodpisow, nazwy.Count);
        Assert.All(nazwy, n => Assert.True(su.ReadSignatureData(n).VerifySignatureIntegrityAndAuthenticity()));
        Assert.True(su.SignatureCoversWholeDocument(nazwy[^1]));
    }

    // C1: /Interpolate jest w PDF/A zabronione — logo PNG/JPG uniemożliwiało podpis dokumentu z EZD.
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void PdfAZLogoRastrowym(int czesc)
    {
        var folder = PlikiTestowe.Folder();
        var wejscie = PlikiTestowe.UtworzPdfA(folder, czesc);
        var wyjscie = Path.Combine(folder, "w.pdf");
        using var cert = CertyfikatyTestowe.Utworz();

        new PodpisywaczPdf(new TestowyTsa()).Podpisz(wejscie, wyjscie, cert,
            new OpcjePodpisu { SciezkaLogo = PlikiTestowe.UtworzLogo(folder) });

        PodpisJestPoprawny(wyjscie);
    }

    [Fact]
    public void BladZgodnosciPdfAMaPolskiKomunikat()
    {
        var komunikat = KomunikatyBledow.Opisz(
            new iText.Pdfa.Exceptions.PdfAConformanceException("The value of interpolate key shall not be true"));
        Assert.Contains("PDF/A", komunikat);
        Assert.Contains("nie spełnia", komunikat);
    }

    // C2: PDF/A-1 nie dopuszcza przezroczystości (SMask) — logo z kanałem alfa jest nakładane na białe tło.
    [Fact]
    public void PdfA1ZLogoZPrzezroczystosciaNaBialymTle()
    {
        var folder = PlikiTestowe.Folder();
        var wejscie = PlikiTestowe.UtworzPdfA(folder, 1);
        var wyjscie = Path.Combine(folder, "w.pdf");
        using var cert = CertyfikatyTestowe.Utworz();

        new PodpisywaczPdf(new TestowyTsa()).Podpisz(wejscie, wyjscie, cert,
            new OpcjePodpisu { SciezkaLogo = PlikiTestowe.UtworzLogoZAlfa(folder) });

        PodpisJestPoprawny(wyjscie);
        using var pdf = new PdfDocument(new PdfReader(wyjscie));
        var obrazy = ObrazyWDokumencie(pdf);
        Assert.NotEmpty(obrazy);
        Assert.All(obrazy, o => Assert.False(o.ContainsKey(PdfName.SMask)));
    }

    [Fact]
    public void ZwyklyDokumentZachowujePrzezroczystoscLogo()
    {
        var folder = PlikiTestowe.Folder();
        var wejscie = PlikiTestowe.UtworzPdf(folder);
        var wyjscie = Path.Combine(folder, "w.pdf");
        using var cert = CertyfikatyTestowe.Utworz();

        new PodpisywaczPdf(new TestowyTsa()).Podpisz(wejscie, wyjscie, cert,
            new OpcjePodpisu { SciezkaLogo = PlikiTestowe.UtworzLogoZAlfa(folder) });

        using var pdf = new PdfDocument(new PdfReader(wyjscie));
        Assert.Contains(ObrazyWDokumencie(pdf), o => o.ContainsKey(PdfName.SMask));
    }

    [Fact]
    public void PdfA1ZLogoWektorowymZPrzezroczystosciaDajeZrozumialyBlad()
    {
        var folder = PlikiTestowe.Folder();
        var wejscie = PlikiTestowe.UtworzPdfA(folder, 1);
        var logo = Path.Combine(folder, "logo.svg");
        File.WriteAllText(logo, """
            <svg xmlns="http://www.w3.org/2000/svg" width="60" height="30"><rect x="5" y="5" width="50" height="20" fill="#c00" fill-opacity="0.6"/><circle cx="30" cy="15" r="8" fill="#00c"/></svg>
            """);
        using var cert = CertyfikatyTestowe.Utworz();

        var e = Assert.Throws<BladPodpisu>(() => new PodpisywaczPdf(new TestowyTsa()).Podpisz(
            wejscie, Path.Combine(folder, "w.pdf"), cert, new OpcjePodpisu { SciezkaLogo = logo }));
        Assert.Contains("PDF/A-1", e.Message);
        Assert.Contains("przezroczyst", e.Message);
    }

    [Fact]
    public void PdfA1ZLogoWektorowymBezPrzezroczystosci()
    {
        var folder = PlikiTestowe.Folder();
        var wejscie = PlikiTestowe.UtworzPdfA(folder, 1);
        var wyjscie = Path.Combine(folder, "w.pdf");
        var logo = Path.Combine(folder, "logo.svg");
        File.WriteAllText(logo, """
            <svg xmlns="http://www.w3.org/2000/svg" width="60" height="30"><rect x="5" y="5" width="50" height="20" fill="#c00"/><circle cx="30" cy="15" r="8" fill="#00c"/></svg>
            """);
        using var cert = CertyfikatyTestowe.Utworz();

        new PodpisywaczPdf(new TestowyTsa()).Podpisz(wejscie, wyjscie, cert, new OpcjePodpisu { SciezkaLogo = logo });

        PodpisJestPoprawny(wyjscie);
    }

    private static List<PdfStream> ObrazyWDokumencie(PdfDocument pdf)
    {
        var wynik = new List<PdfStream>();
        for (var i = 1; i < pdf.GetNumberOfPdfObjects(); i++)
        {
            if (pdf.GetPdfObject(i) is PdfStream s && PdfName.Image.Equals(s.GetAsName(PdfName.Subtype)) &&
                !s.ContainsKey(PdfName.ImageMask) && s.GetAsNumber(PdfName.Width)?.IntValue() == 60)
                wynik.Add(s);
        }
        return wynik;
    }

    // C4: certyfikacja „bez zmian” (DocMDP P=1) — kolejny podpis unieważniłby certyfikację.
    [Theory]
    [InlineData(1, false)]
    [InlineData(2, true)]
    public void DokumentCertyfikowany(int poziom, bool mozna)
    {
        var folder = PlikiTestowe.Folder();
        var certyfikowany = Certyfikuj(PlikiTestowe.UtworzPdf(folder), Path.Combine(folder, "certyfikowany.pdf"),
            poziom == 1 ? AccessPermissions.NO_CHANGES_PERMITTED : AccessPermissions.FORM_FIELDS_MODIFICATION);
        var wyjscie = Path.Combine(folder, "w.pdf");
        using var cert = CertyfikatyTestowe.Utworz();
        var podpis = () => new PodpisywaczPdf(new TestowyTsa()).Podpisz(certyfikowany, wyjscie, cert, new OpcjePodpisu());

        if (mozna)
        {
            podpis();
            PodpisJestPoprawny(wyjscie, 2);
        }
        else
        {
            var e = Assert.Throws<BladPodpisu>(podpis);
            Assert.Contains("zablokował wszelkie zmiany", e.Message);
            Assert.False(File.Exists(wyjscie));
        }
    }

    private static string Certyfikuj(string wejscie, string wyjscie, AccessPermissions poziom)
    {
        using var cert = CertyfikatyTestowe.Utworz(cn: "Certyfikujący", imie: null, nazwisko: null);
        using (var czytnik = new PdfReader(wejscie))
        using (var strumien = new FileStream(wyjscie, FileMode.Create))
        {
            var wlasciwosci = new SignerProperties()
                .SetPageNumber(1)
                .SetPageRect(new Rectangle(0, 0, 0, 0))
                .SetCertificationLevel(poziom);
            var lancuch = new[] { BouncyCastleFactoryCreator.GetFactory().CreateX509Certificate(new MemoryStream(cert.RawData)) };
            new PdfPadesSigner(czytnik, strumien)
                .SignWithBaselineTProfile(wlasciwosci, lancuch, new PodpisKluczemCertyfikatu(cert, null), new TestowyTsa());
        }
        return wyjscie;
    }

    // C5: uszkodzona tabela xref (częste przy skanerach).
    private static string UszkodzXref(string sciezka, string wynik)
    {
        var tekst = Encoding.Latin1.GetString(File.ReadAllBytes(sciezka));
        var i = tekst.LastIndexOf("startxref", StringComparison.Ordinal);
        File.WriteAllBytes(wynik, Encoding.Latin1.GetBytes(tekst[..i] + "startxref\n12345\n%%EOF\n"));
        return wynik;
    }

    [Fact]
    public void UszkodzonyXrefBezPodpisowJestNaprawiany()
    {
        var folder = PlikiTestowe.Folder();
        var uszkodzony = UszkodzXref(PlikiTestowe.UtworzPdf(folder), Path.Combine(folder, "skan.pdf"));
        var oryginal = File.ReadAllBytes(uszkodzony);
        var wyjscie = Path.Combine(folder, "w.pdf");
        using var cert = CertyfikatyTestowe.Utworz();

        new PodpisywaczPdf(new TestowyTsa()).Podpisz(uszkodzony, wyjscie, cert, new OpcjePodpisu());

        PodpisJestPoprawny(wyjscie);
        Assert.Equal(oryginal, File.ReadAllBytes(uszkodzony)); // oryginał bez zmian
        using var pdf = new PdfDocument(new PdfReader(wyjscie));
        Assert.Equal(2, pdf.GetNumberOfPages());
    }

    [Fact]
    public void UszkodzonyXrefZPodpisemDajeZrozumialyBlad()
    {
        var folder = PlikiTestowe.Folder();
        var podpisany = Path.Combine(folder, "podpisany.pdf");
        using var cert = CertyfikatyTestowe.Utworz();
        new PodpisywaczPdf(new TestowyTsa()).Podpisz(PlikiTestowe.UtworzPdf(folder), podpisany, cert, new OpcjePodpisu());
        var uszkodzony = UszkodzXref(podpisany, Path.Combine(folder, "uszkodzony.pdf"));

        var e = Assert.Throws<BladPodpisu>(() => new PodpisywaczPdf(new TestowyTsa()).Podpisz(
            uszkodzony, Path.Combine(folder, "w.pdf"), cert, new OpcjePodpisu()));
        Assert.Contains("uszkodzony", e.Message);
    }

    // C8: CropBox większy niż MediaBox — widoczna jest tylko ich część wspólna (tak rysuje PDFium).
    private static string PdfZCropBox(string folder, Rectangle cropBox)
    {
        var sciezka = Path.Combine(folder, "crop.pdf");
        using var pdf = new PdfDocument(new PdfWriter(sciezka));
        var strona = pdf.AddNewPage(PageSize.A4);
        new PdfCanvas(strona).SetFillColorRgb(1, 0, 0).Rectangle(100, 100, 200, 300).Fill();
        strona.SetCropBox(cropBox);
        return sciezka;
    }

    [Theory]
    [InlineData(-50, -50, 695, 942, 0, 0, 595, 842)]
    [InlineData(300, 400, 595, 842, 300, 400, 295, 442)]
    [InlineData(10, 20, 300, 400, 10, 20, 300, 400)]
    public void StronaToCzescWspolnaCropBoxIMediaBox(float x, float y, float w, float h, float ex, float ey, float ew, float eh)
    {
        var sciezka = PdfZCropBox(PlikiTestowe.Folder(), new Rectangle(x, y, w, h));

        var strona = Assert.Single(DokumentPdf.Strony(sciezka));
        Assert.Equal(ex, strona.Lewo, 0.01);
        Assert.Equal(ey, strona.Dol, 0.01);
        Assert.Equal(ew, strona.Szerokosc, 0.01);
        Assert.Equal(eh, strona.Wysokosc, 0.01);

        var obraz = DokumentPdf.Renderuj(File.ReadAllBytes(sciezka), 0, 1);
        Assert.Equal((int)ew, obraz.Szerokosc);
        Assert.Equal((int)eh, obraz.Wysokosc);
    }

    [Fact]
    public void PolozenieDomyslneLiczoneOdWidocznejCzesciStrony()
    {
        var folder = PlikiTestowe.Folder();
        var wejscie = PdfZCropBox(folder, new Rectangle(-50, -50, 695, 942));
        var wyjscie = Path.Combine(folder, "w.pdf");
        using var cert = CertyfikatyTestowe.Utworz();

        new PodpisywaczPdf(new TestowyTsa()).Podpisz(wejscie, wyjscie, cert, new OpcjePodpisu
        {
            Polozenie = new PolozenieWizualizacji { Strona = WyborStrony.Pierwsza, Naroznik = Naroznik.LewyDolny },
        });

        using var pdf = new PdfDocument(new PdfReader(wyjscie));
        var nazwa = new SignatureUtil(pdf).GetSignatureNames()[0];
        var rect = iText.Forms.PdfAcroForm.GetAcroForm(pdf, false).GetField(nazwa).GetWidgets()[0].GetRectangle().ToRectangle();
        Assert.Equal(15 * Wizualizacja.PunktowNaMm, rect.GetX(), 0.01);
        Assert.Equal(15 * Wizualizacja.PunktowNaMm, rect.GetY(), 0.01);

        var e = Assert.Throws<BladPodpisu>(() => new PodpisywaczPdf(new TestowyTsa()).Podpisz(
            wejscie, Path.Combine(folder, "w2.pdf"), cert, new OpcjePodpisu { Miejsce = new MiejscePodpisu(1, -40, 100) }));
        Assert.Contains("poza stronę", e.Message);
    }

    // C10: plik z samym hasłem właściciela (ograniczone uprawnienia) a plik z hasłem otwarcia.
    private static string Zaszyfrowany(string folder, string? hasloUzytkownika)
    {
        var sciezka = Path.Combine(folder, "zabezpieczony.pdf");
        var wlasciwosci = new WriterProperties().SetStandardEncryption(
            hasloUzytkownika == null ? null : Encoding.ASCII.GetBytes(hasloUzytkownika), Encoding.ASCII.GetBytes("wlasciciel"),
            EncryptionConstants.ALLOW_PRINTING, EncryptionConstants.ENCRYPTION_AES_128);
        using (var pdf = new PdfDocument(new PdfWriter(sciezka, wlasciwosci)))
        using (var doc = new Document(pdf))
            doc.Add(new Paragraph("Dokument zabezpieczony"));
        return sciezka;
    }

    [Fact]
    public void PlikZOgraniczonymiUprawnieniami()
    {
        var folder = PlikiTestowe.Folder();
        var sciezka = Zaszyfrowany(folder, null);
        using var cert = CertyfikatyTestowe.Utworz();

        Assert.True(DokumentPdf.CzyZablokowanyDoZmian(sciezka));
        Assert.Single(DokumentPdf.Strony(sciezka)); // podgląd działa
        var e = Assert.ThrowsAny<Exception>(() => new PodpisywaczPdf(new TestowyTsa()).Podpisz(
            sciezka, Path.Combine(folder, "w.pdf"), cert, new OpcjePodpisu()));
        Assert.Contains("ograniczył uprawnienia", KomunikatyBledow.Opisz(e));
        Assert.Contains("ograniczył uprawnienia", KomunikatyBledow.Opisz(
            new iText.Kernel.Exceptions.BadPasswordException("PdfReader is not opened with owner password")));
    }

    [Fact]
    public void PlikZHaslemOtwarcia()
    {
        var folder = PlikiTestowe.Folder();
        var sciezka = Zaszyfrowany(folder, "uzytkownik");
        using var cert = CertyfikatyTestowe.Utworz();

        var e = Assert.ThrowsAny<Exception>(() => new PodpisywaczPdf(new TestowyTsa()).Podpisz(
            sciezka, Path.Combine(folder, "w.pdf"), cert, new OpcjePodpisu()));
        Assert.Contains("zabezpieczony hasłem", KomunikatyBledow.Opisz(e));
        Assert.Contains("zabezpieczony hasłem", KomunikatyBledow.Opisz(Assert.ThrowsAny<Exception>(() => DokumentPdf.CzyZablokowanyDoZmian(sciezka))));
        Assert.False(DokumentPdf.CzyZablokowanyDoZmian(PlikiTestowe.UtworzPdf(folder)));
    }

    // S9: ważność certyfikatu sprawdzana w chwili podpisu (program działa w zasobniku tygodniami).
    [Fact]
    public void CertyfikatPrzeterminowanyWChwiliPodpisu()
    {
        var folder = PlikiTestowe.Folder();
        using var cert = CertyfikatyTestowe.Utworz(od: DateTimeOffset.UtcNow.AddYears(-2), doDnia: DateTimeOffset.UtcNow.AddDays(-1));

        var e = Assert.Throws<BladPodpisu>(() => new PodpisywaczPdf(new TestowyTsa()).Podpisz(
            PlikiTestowe.UtworzPdf(folder), Path.Combine(folder, "w.pdf"), cert, new OpcjePodpisu()));
        Assert.Contains("wygasł", e.Message);
    }

    [Fact]
    public void CertyfikatJeszczeNiewaznyWChwiliPodpisu()
    {
        var folder = PlikiTestowe.Folder();
        using var cert = CertyfikatyTestowe.Utworz(od: DateTimeOffset.UtcNow.AddDays(2), doDnia: DateTimeOffset.UtcNow.AddYears(1));

        var e = Assert.Throws<BladPodpisu>(() => new PodpisywaczPdf(new TestowyTsa()).Podpisz(
            PlikiTestowe.UtworzPdf(folder), Path.Combine(folder, "w.pdf"), cert, new OpcjePodpisu()));
        Assert.Contains("jeszcze nieważny", e.Message);
    }

    [Fact]
    public void CertyfikatBezNonRepudiationOdrzucanyPrzyPodpisie()
    {
        var folder = PlikiTestowe.Folder();
        using var cert = CertyfikatyTestowe.Utworz(uzycie: System.Security.Cryptography.X509Certificates.X509KeyUsageFlags.DigitalSignature);

        var e = Assert.Throws<BladPodpisu>(() => new PodpisywaczPdf(new TestowyTsa()).Podpisz(
            PlikiTestowe.UtworzPdf(folder), Path.Combine(folder, "w.pdf"), cert, new OpcjePodpisu()));
        Assert.Contains("niezaprzeczalność", e.Message);
    }
}
