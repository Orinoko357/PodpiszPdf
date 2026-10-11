using iText.Forms;
using iText.Kernel.Pdf;
using iText.Signatures;
using PodpiszPdf.Core;
using Xunit;

namespace PodpiszPdf.Tests;

public class PodpisywaczPdfTesty
{
    private static readonly DateTimeOffset StalyCzas = new(2026, 3, 14, 10, 20, 30, TimeSpan.Zero);

    [Fact]
    public void NazwaPlikuWynikowego()
    {
        var wynik = PodpisywaczPdf.NazwaPlikuWynikowego(Path.Combine("C:", "dok", "wniosek.pdf"), "_podpisany");
        Assert.Equal("wniosek_podpisany.pdf", Path.GetFileName(wynik));
    }

    [Fact]
    public void PodpisZastepujeOryginal()
    {
        var folder = PlikiTestowe.Folder();
        var plik = PlikiTestowe.UtworzPdf(folder);
        var oryginal = File.ReadAllBytes(plik);
        using var cert = CertyfikatyTestowe.Utworz();

        // Dwa podpisy z rzędu w tym samym pliku: drugi dopisuje się do pierwszego, oba ważne.
        new PodpisywaczPdf(new TestowyTsa()).Podpisz(plik, plik, cert, new OpcjePodpisu());
        new PodpisywaczPdf(new TestowyTsa()).Podpisz(plik, plik, cert, new OpcjePodpisu { Miejsce = new MiejscePodpisu(1, 100, 100) });

        var podpisany = File.ReadAllBytes(plik);
        Assert.Equal(oryginal, podpisany.Take(oryginal.Length)); // zapis przyrostowy na oryginale
        Assert.Single(Directory.GetFiles(folder, "*.pdf")); // bez dodatkowych plików
        Assert.Empty(Directory.GetFiles(folder, "*.tmp"));
        using var pdf = new PdfDocument(new PdfReader(plik));
        var su = new SignatureUtil(pdf);
        Assert.Equal(2, su.GetSignatureNames().Count);
        Assert.All(su.GetSignatureNames(), n => Assert.True(su.ReadSignatureData(n).VerifySignatureIntegrityAndAuthenticity()));
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
    public void WizualizacjaWMiejscuWskazanymNaPodgladzie()
    {
        var folder = PlikiTestowe.Folder();
        var wejscie = PlikiTestowe.UtworzPdf(folder, strony: 3);
        var wyjscie = Path.Combine(folder, "w.pdf");
        using var cert = CertyfikatyTestowe.Utworz();
        var opcje = new OpcjePodpisu { Miejsce = new MiejscePodpisu(2, 100, 400) };

        new PodpisywaczPdf(new TestowyTsa()).Podpisz(wejscie, wyjscie, cert, opcje);

        using var pdf = new PdfDocument(new PdfReader(wyjscie));
        var nazwa = new SignatureUtil(pdf).GetSignatureNames()[0];
        var widget = PdfAcroForm.GetAcroForm(pdf, false).GetField(nazwa).GetWidgets()[0];
        Assert.Equal(2, pdf.GetPageNumber(widget.GetPage()));
        var rect = widget.GetRectangle().ToRectangle();
        Assert.Equal(100, rect.GetX(), 0.01);
        Assert.Equal(400, rect.GetY(), 0.01);
        var rozmiar = Wizualizacja.Rozmiar(new DaneWizualizacji(CertyfikatInfo.PobierzImieNazwisko(cert), null, null, DateTimeOffset.Now));
        Assert.Equal(rozmiar.Szerokosc, rect.GetWidth(), 0.01);
        Assert.Equal(rozmiar.Wysokosc, rect.GetHeight(), 0.01);
    }

    [Fact]
    public void MiejscePozaStronaDajeZrozumialyBlad()
    {
        var folder = PlikiTestowe.Folder();
        var wejscie = PlikiTestowe.UtworzPdf(folder);
        using var cert = CertyfikatyTestowe.Utworz();
        var opcje = new OpcjePodpisu { Miejsce = new MiejscePodpisu(1, 550, 10) };

        var e = Assert.Throws<BladPodpisu>(() =>
            new PodpisywaczPdf(new TestowyTsa()).Podpisz(wejscie, Path.Combine(folder, "w.pdf"), cert, opcje));
        Assert.Contains("poza stronę", e.Message);
    }

    [Fact]
    public void DopasowanieKliknieciaDoKrawedziStrony()
    {
        var strona = new iText.Kernel.Geom.Rectangle(0, 0, 595, 842);
        var rozmiar = new RozmiarWizualizacji(140, 50);
        var (x, y) = Wizualizacja.DopasujDoStrony(strona, 590, 5, rozmiar);
        Assert.Equal(595 - 140, x, 0.01);
        Assert.Equal(0, y, 0.01);
        (x, y) = Wizualizacja.DopasujDoStrony(strona, 300, 400, rozmiar);
        Assert.Equal(300 - 140 / 2, x, 0.01);
        Assert.Equal(400 - 50 / 2, y, 0.01);
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

public class WizualizacjaTesty
{
    [Theory]
    [InlineData(null, false)]
    [InlineData("Kierownik", false)]
    [InlineData("Kierownik", true)]
    public void ImieINazwiskoZawszePogrubione(string? stanowisko, bool zLogo)
    {
        var folder = PlikiTestowe.Folder();
        var wejscie = PlikiTestowe.UtworzPdf(folder);
        var wyjscie = Path.Combine(folder, "w.pdf");
        using var cert = CertyfikatyTestowe.Utworz(imie: "Anna", nazwisko: "Nowak");
        var opcje = new OpcjePodpisu { Stanowisko = stanowisko, SciezkaLogo = zLogo ? PlikiTestowe.UtworzLogo(folder) : null };

        new PodpisywaczPdf(new TestowyTsa()).Podpisz(wejscie, wyjscie, cert, opcje);

        using var pdf = new PdfDocument(new PdfReader(wyjscie));
        var nazwa = new SignatureUtil(pdf).GetSignatureNames()[0];
        var wyglad = PdfAcroForm.GetAcroForm(pdf, false).GetField(nazwa).GetWidgets()[0].GetNormalAppearanceObject();
        var czcionki = new List<string>();
        ZbierzCzcionki(wyglad, czcionki, new HashSet<PdfObject>());

        // Pogrubiona czcionka jest używana wyłącznie dla imienia i nazwiska.
        // Arial (Windows) albo metrycznie zgodna Liberation Sans (Linux).
        Assert.Contains(czcionki, c => c.Contains("Arial-Bold") || c.Contains("LiberationSans-Bold"));
        Assert.Contains(czcionki, c => c.EndsWith("ArialMT") || c.EndsWith("LiberationSans"));
    }

    [Fact]
    public void ZawijanieWierszyNaGranicachSlow()
    {
        var czcionka = iText.Kernel.Font.PdfFontFactory.CreateFont(iText.IO.Font.Constants.StandardFonts.HELVETICA);
        var dwa = czcionka.GetWidth("abcd abcd", 10);
        Assert.Equal(new[] { "abcd abcd" }, Wizualizacja.Zawin("abcd abcd", czcionka, 10, dwa));
        Assert.Equal(new[] { "abcd abcd", "abcd" }, Wizualizacja.Zawin("abcd abcd abcd", czcionka, 10, dwa));
        Assert.Equal(new[] { "abcdabcd", "x" }, Wizualizacja.Zawin("abcdabcd x", czcionka, 10, 10));
    }

    [Fact]
    public void PodzialStanowiskaWpisanyPrzezUzytkownika()
    {
        Assert.Equal(new[] { "Kierownik biura informatyki", "Urzędu Miasta Wodzisławia Śląskiego" },
            Wizualizacja.WierszeStanowiska("Kierownik biura informatyki \r\n Urzędu Miasta Wodzisławia Śląskiego\n"));
        Assert.Equal(new[] { "Kierownik" }, Wizualizacja.WierszeStanowiska("Kierownik"));
        Assert.Empty(Wizualizacja.WierszeStanowiska("  "));
    }

    [Fact]
    public void DataNaWizualizacjiBezSekund()
    {
        var tekst = Wizualizacja.FormatujCzas(new DateTimeOffset(2026, 3, 14, 10, 20, 30, TimeSpan.Zero));
        Assert.Matches(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2} \(UTC[+-]\d{2}:\d{2}\)$", tekst);
    }

    private static void ZbierzCzcionki(PdfObject? obiekt, List<string> wynik, HashSet<PdfObject> odwiedzone)
    {
        if (obiekt is PdfIndirectReference r)
            obiekt = r.GetRefersTo();
        if (obiekt == null || !odwiedzone.Add(obiekt))
            return;
        if (obiekt is PdfDictionary d)
        {
            if (PdfName.Font.Equals(d.GetAsName(PdfName.Type)) && d.GetAsName(PdfName.BaseFont) is { } bf)
                wynik.Add(bf.GetValue());
            foreach (var klucz in d.KeySet())
                ZbierzCzcionki(d.Get(klucz, false), wynik, odwiedzone);
        }
        else if (obiekt is PdfArray a)
        {
            for (var i = 0; i < a.Size(); i++)
                ZbierzCzcionki(a.Get(i, false), wynik, odwiedzone);
        }
    }
}

public class SamotestTesty
{
    [Fact]
    public void PodpisProbnyDziala() => PodpiszPdf.Core.Samotest.PodpisProbny();
}
