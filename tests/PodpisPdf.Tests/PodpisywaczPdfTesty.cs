using iText.Forms;
using iText.Kernel.Pdf;
using iText.Signatures;
using PodpisPdf.Core;
using Xunit;

namespace PodpisPdf.Tests;

public class PodpisywaczPdfTesty
{
    private static readonly DateTimeOffset StalyCzas = new(2026, 3, 14, 10, 20, 30, TimeSpan.Zero);

    [Fact]
    public void NazwaPlikuWynikowego()
    {
        var wynik = PodpisywaczPdf.NazwaPlikuWynikowego(Path.Combine("C:", "dok", "wniosek.pdf"), "_podpisany");
        Assert.Equal("wniosek_podpisany.pdf", Path.GetFileName(wynik));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PodpisPadesBTZeZnacznikiemCzasu(bool ecdsa)
    {
        var folder = PlikiTestowe.Folder();
        var wejscie = PlikiTestowe.UtworzPdf(folder);
        var oryginal = File.ReadAllBytes(wejscie);
        var wyjscie = PodpisywaczPdf.NazwaPlikuWynikowego(wejscie, "_podpisany");
        using var cert = CertyfikatyTestowe.Utworz(ecdsa: ecdsa);
        var tsa = new TestowyTsa();

        var wynik = new PodpisywaczPdf(tsa).Podpisz(wejscie, wyjscie, cert, new OpcjePodpisu { Stanowisko = "Kierownik działu" });

        Assert.True(wynik.CzasZSerwera);
        Assert.Equal(oryginal, File.ReadAllBytes(wejscie)); // oryginał bez zmian

        var podpisany = File.ReadAllBytes(wyjscie);
        Assert.Equal(oryginal, podpisany.Take(oryginal.Length)); // zapis przyrostowy

        using var pdf = new PdfDocument(new PdfReader(wyjscie));
        var su = new SignatureUtil(pdf);
        var nazwa = Assert.Single(su.GetSignatureNames());
        Assert.True(su.SignatureCoversWholeDocument(nazwa));

        var pkcs7 = su.ReadSignatureData(nazwa);
        Assert.True(pkcs7.VerifySignatureIntegrityAndAuthenticity());
        Assert.Equal("SHA256", pkcs7.GetDigestAlgorithmName());
        Assert.Equal(ecdsa ? "SHA256withECDSA" : "SHA256withRSA", pkcs7.GetSignatureMechanismName());
        Assert.Equal(PdfName.ETSI_CAdES_DETACHED, pkcs7.GetFilterSubtype());
        Assert.NotNull(pkcs7.GetTimeStampTokenInfo());
        Assert.True(pkcs7.VerifyTimestampImprint());

        // Widoczna wizualizacja na ostatniej stronie (ustawienie domyślne).
        var pole = PdfAcroForm.GetAcroForm(pdf, false).GetField(nazwa);
        var widget = Assert.Single(pole.GetWidgets());
        Assert.Equal(2, pdf.GetPageNumber(widget.GetPage()));
        var rect = widget.GetRectangle().ToRectangle();
        Assert.True(rect.GetWidth() > 100 && rect.GetHeight() > 30);
        Assert.NotNull(widget.GetNormalAppearanceObject());
    }

    [Fact]
    public void KolejnyPodpisNieUniewaznniaPoprzedniego()
    {
        var folder = PlikiTestowe.Folder();
        var wejscie = PlikiTestowe.UtworzPdf(folder);
        var raz = Path.Combine(folder, "raz.pdf");
        var dwa = Path.Combine(folder, "dwa.pdf");
        using var cert1 = CertyfikatyTestowe.Utworz(cn: "Pierwszy Podpisujący", imie: null, nazwisko: null);
        using var cert2 = CertyfikatyTestowe.Utworz(cn: "Drugi Podpisujący", imie: null, nazwisko: null, ecdsa: true);
        var tsa = new TestowyTsa();

        new PodpisywaczPdf(tsa).Podpisz(wejscie, raz, cert1, new OpcjePodpisu());
        new PodpisywaczPdf(tsa).Podpisz(raz, dwa, cert2, new OpcjePodpisu
        {
            Polozenie = new PolozenieWizualizacji { Naroznik = Naroznik.LewyDolny },
        });

        using var pdf = new PdfDocument(new PdfReader(dwa));
        var su = new SignatureUtil(pdf);
        var nazwy = su.GetSignatureNames();
        Assert.Equal(2, nazwy.Count);
        Assert.All(nazwy, n => Assert.True(su.ReadSignatureData(n).VerifySignatureIntegrityAndAuthenticity()));
        Assert.False(su.SignatureCoversWholeDocument(nazwy[0]));
        Assert.True(su.SignatureCoversWholeDocument(nazwy[1]));
    }

    [Fact]
    public void WizualizacjaZLogoNaWybranejStronie()
    {
        var folder = PlikiTestowe.Folder();
        var wejscie = PlikiTestowe.UtworzPdf(folder, strony: 3);
        var wyjscie = Path.Combine(folder, "wynik.pdf");
        using var cert = CertyfikatyTestowe.Utworz();
        var opcje = new OpcjePodpisu
        {
            Stanowisko = "Dyrektor",
            SciezkaLogo = PlikiTestowe.UtworzLogo(folder),
            Polozenie = new PolozenieWizualizacji { Strona = WyborStrony.Numer, NumerStrony = 2, Naroznik = Naroznik.PrawyGorny },
        };

        new PodpisywaczPdf(new TestowyTsa(), () => StalyCzas).Podpisz(wejscie, wyjscie, cert, opcje);

        using var pdf = new PdfDocument(new PdfReader(wyjscie));
        var su = new SignatureUtil(pdf);
        var nazwa = Assert.Single(su.GetSignatureNames());
        Assert.True(su.ReadSignatureData(nazwa).VerifySignatureIntegrityAndAuthenticity());
        var widget = PdfAcroForm.GetAcroForm(pdf, false).GetField(nazwa).GetWidgets()[0];
        Assert.Equal(2, pdf.GetPageNumber(widget.GetPage()));
        var rect = widget.GetRectangle().ToRectangle();
        var strona = pdf.GetPage(2).GetCropBox();
        Assert.True(rect.GetTop() > strona.GetHeight() / 2); // górna część strony
    }

    [Fact]
    public void CzasKomputeraGdySerwerNieOdpowiadaPrzedPodpisem()
    {
        var folder = PlikiTestowe.Folder();
        var wejscie = PlikiTestowe.UtworzPdf(folder);
        using var cert = CertyfikatyTestowe.Utworz();

        var wynik = new PodpisywaczPdf(new TestowyTsa(), () => null)
            .Podpisz(wejscie, Path.Combine(folder, "w.pdf"), cert, new OpcjePodpisu());

        Assert.False(wynik.CzasZSerwera);
        Assert.InRange(wynik.CzasNaWizualizacji, DateTimeOffset.Now.AddMinutes(-1), DateTimeOffset.Now.AddMinutes(1));
    }

    [Fact]
    public void BladSerweraZnacznikaCzasuNieTworzyPliku()
    {
        var folder = PlikiTestowe.Folder();
        var wejscie = PlikiTestowe.UtworzPdf(folder);
        var wyjscie = Path.Combine(folder, "w.pdf");
        using var cert = CertyfikatyTestowe.Utworz();
        var tsa = new KlientZnacznikaCzasu(new TestowyTsa { Awaria = true }, "tsa.example");

        var e = Assert.ThrowsAny<Exception>(() =>
            new PodpisywaczPdf(tsa).Podpisz(wejscie, wyjscie, cert, new OpcjePodpisu()));

        Assert.Contains("znacznika czasu", KomunikatyBledow.Opisz(e));
        Assert.False(File.Exists(wyjscie));
        Assert.Single(Directory.GetFiles(folder)); // bez pozostawionych plików tymczasowych
    }

    [Fact]
    public void ZlyNumerStronyDajeZrozumialyBlad()
    {
        var folder = PlikiTestowe.Folder();
        var wejscie = PlikiTestowe.UtworzPdf(folder, strony: 2);
        using var cert = CertyfikatyTestowe.Utworz();
        var opcje = new OpcjePodpisu { Polozenie = new PolozenieWizualizacji { Strona = WyborStrony.Numer, NumerStrony = 5 } };

        var e = Assert.Throws<BladPodpisu>(() =>
            new PodpisywaczPdf(new TestowyTsa()).Podpisz(wejscie, Path.Combine(folder, "w.pdf"), cert, opcje));
        Assert.Contains("stronę 5", e.Message);
    }

    [Fact]
    public void WizualizacjaWiekszaNizStronaDajeZrozumialyBlad()
    {
        var folder = PlikiTestowe.Folder();
        var wejscie = PlikiTestowe.UtworzPdf(folder);
        using var cert = CertyfikatyTestowe.Utworz();
        var opcje = new OpcjePodpisu { Polozenie = new PolozenieWizualizacji { SzerokoscMm = 400 } };

        var e = Assert.Throws<BladPodpisu>(() =>
            new PodpisywaczPdf(new TestowyTsa()).Podpisz(wejscie, Path.Combine(folder, "w.pdf"), cert, opcje));
        Assert.Contains("nie mieści się", e.Message);
    }

    [Fact]
    public void CzasZSerweraZnacznikaCzasu()
    {
        var czas = KlientZnacznikaCzasu.PobierzCzas(new TestowyTsa());
        Assert.NotNull(czas);
        Assert.InRange(czas!.Value, DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(1));
        Assert.Null(KlientZnacznikaCzasu.PobierzCzas(new TestowyTsa { Awaria = true }));
    }
}
