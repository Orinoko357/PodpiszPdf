using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Data;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using PodpiszPdf.Core;

namespace PodpiszPdf.Tests;

public class MotywyWizualizacjiTesty
{
    private sealed class Zbieracz : IEventListener
    {
        public List<(string Tekst, float Rozmiar, float[] Kolor)> Fragmenty { get; } = new();

        public void EventOccurred(IEventData dane, EventType typ)
        {
            if (dane is TextRenderInfo t)
                Fragmenty.Add((t.GetText(), t.GetFontSize(), t.GetFillColor().GetColorValue()));
        }

        public ICollection<EventType> GetSupportedEvents() => new[] { EventType.RENDER_TEXT };
    }

    private static readonly DateTimeOffset Czas = new(2026, 10, 11, 16, 2, 0, TimeSpan.FromHours(2));

    private static List<(string Tekst, float Rozmiar, float[] Kolor)> Tekst(DaneWizualizacji dane, float skala = 1f)
    {
        using var pdf = new PdfDocument(new PdfReader(new MemoryStream(Wizualizacja.PodgladPdf(dane, skala))));
        var z = new Zbieracz();
        new PdfCanvasProcessor(z).ProcessPageContent(pdf.GetPage(1));
        return z.Fragmenty;
    }

    private static (string Tekst, float Rozmiar, float[] Kolor) Wiersz(List<(string Tekst, float Rozmiar, float[] Kolor)> f, string poczatek) =>
        f.First(x => x.Tekst.StartsWith(poczatek, StringComparison.Ordinal));

    [Theory]
    [InlineData(MotywWizualizacji.Hierarchia, "PODPISANO", 6.5f)]
    [InlineData(MotywWizualizacji.Linia, "PODPISANO", 6.5f)]
    [InlineData(MotywWizualizacji.Akcent, "PODPISANO", 6.5f)]
    [InlineData(MotywWizualizacji.DataPodKreska, "PODPISANO", 6.5f)]
    [InlineData(MotywWizualizacji.Pieczatka, "PODPISANO", 6.5f)]
    public void StanowiskoWyrazniejszeNizNaglowekIData(MotywWizualizacji motyw, string naglowek, float rozmiarNaglowka)
    {
        var f = Tekst(new DaneWizualizacji("Anna Nowak", "Kierownik Biura Informatyki\nUrzędu Miasta Wodzisławia Śląskiego", null, Czas, motyw, true));
        Assert.Equal(rozmiarNaglowka, Wiersz(f, naglowek).Rozmiar, 0.01);
        Assert.Equal(11f, Wiersz(f, "Anna").Rozmiar, 0.01);
        Assert.Equal(9f, Wiersz(f, "Kierownik").Rozmiar, 0.01);
        Assert.Equal(9f, Wiersz(f, "Urzędu").Rozmiar, 0.01);
        var data = f.Last();
        Assert.Contains("11.10.2026, ", data.Tekst + string.Concat(f.SkipWhile(x => x != data).Select(x => x.Tekst)));
        Assert.True(data.Rozmiar <= 7f);
        // Nagłówek i data szare, stanowisko ciemne.
        Assert.True(Wiersz(f, naglowek).Kolor.Average() > 0.3);
        Assert.True(Wiersz(f, "Kierownik").Kolor.Average() < 0.2);
    }

    [Fact]
    public void KlasycznyZNaglowkiemWersalikami()
    {
        var f = Tekst(new DaneWizualizacji("Anna Nowak", "Inspektor", null, Czas));
        Assert.Equal(6.5f, Wiersz(f, "PODPISANO").Rozmiar, 0.01);
        Assert.InRange(Wiersz(f, "Inspektor").Rozmiar, 7f, 9f); // stanowisko 7–9 pt
        Assert.StartsWith("Data: 11.10.2026", string.Concat(f.SkipWhile(x => !x.Tekst.StartsWith("Data")).Select(x => x.Tekst)).Trim());
    }

    [Theory]
    [InlineData(MotywWizualizacji.Klasyczny)]
    [InlineData(MotywWizualizacji.Hierarchia)]
    [InlineData(MotywWizualizacji.Linia)]
    [InlineData(MotywWizualizacji.Akcent)]
    [InlineData(MotywWizualizacji.DataPodKreska)]
    [InlineData(MotywWizualizacji.Pieczatka)]
    public void DataTaSamaWeWszystkichMotywach(MotywWizualizacji motyw)
    {
        var czas = new DateTimeOffset(2026, 10, 11, 16, 2, 0, TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 10, 11, 16, 2, 0)));
        var strefa = czas.Offset;
        Assert.Equal($"Data: 11.10.2026, 16:02 (UTC{(strefa < TimeSpan.Zero ? "-" : "+")}{strefa.Duration():hh\\:mm})",
            Wizualizacja.TekstDaty(new DaneWizualizacji("A", null, null, czas, motyw, true)));
    }

    [Fact]
    public void DlugieStanowiskoZawijaSieWStalymRozmiarze()
    {
        var f = Tekst(new DaneWizualizacji("Anna Nowak",
            "Zastępca Kierownika Wydziału Gospodarki Nieruchomościami i Geodezji Urzędu Miasta", null, Czas, MotywWizualizacji.Hierarchia));
        var stanowisko = f.Where(x => Math.Abs(x.Rozmiar - 9f) < 0.01).Select(x => x.Tekst).ToList();
        Assert.True(stanowisko.Count >= 2); // dwa wiersze, oba 9 pt
    }

    [Theory]
    [InlineData(MotywWizualizacji.Linia)]
    [InlineData(MotywWizualizacji.DataPodKreska)]
    public void KreskaJestNarysowana(MotywWizualizacji motyw)
    {
        var folder = PlikiTestowe.Folder();
        var dane = new DaneWizualizacji("Mateusz Kozielski", "Kierownik", PlikiTestowe.UtworzLogo(folder), Czas, motyw, true);
        var bez = new DaneWizualizacji("Mateusz Kozielski", "Kierownik", PlikiTestowe.UtworzLogo(folder), Czas, MotywWizualizacji.Hierarchia, true);
        const double skala = 4;
        int Szare(ObrazStrony o)
        {
            var n = 0;
            for (var i = 0; i < o.Bgra.Length; i += 4)
            {
                int b = o.Bgra[i], g = o.Bgra[i + 1], r = o.Bgra[i + 2];
                if (Math.Abs(r - 0x9A) < 20 && Math.Abs(g - 0x9A) < 20 && Math.Abs(b - 0x9A) < 20)
                    n++;
            }
            return n;
        }
        var z = DokumentPdf.Renderuj(Wizualizacja.PodgladPdf(dane), 0, skala);
        var b = DokumentPdf.Renderuj(Wizualizacja.PodgladPdf(bez), 0, skala);
        Assert.True(Szare(z) > Szare(b) + 40, $"{Szare(z)} vs {Szare(b)}");
    }

    [Theory]
    [InlineData(MotywWizualizacji.Linia)]
    [InlineData(MotywWizualizacji.Akcent)]
    [InlineData(MotywWizualizacji.DataPodKreska)]
    public void MotywSkalujeSieProporcjonalnie(MotywWizualizacji motyw)
    {
        var dane = new DaneWizualizacji("Anna Nowak", "Inspektor", null, Czas, motyw, true);
        var f1 = Tekst(dane);
        var f2 = Tekst(dane, 2f);
        Assert.Equal(f1.Count, f2.Count);
        for (var i = 0; i < f1.Count; i++)
            Assert.Equal(f1[i].Rozmiar * 2, f2[i].Rozmiar, 0.01);
    }

    [Fact]
    public void PodpisUzywaMotywuZOpcji()
    {
        var folder = PlikiTestowe.Folder();
        var wejscie = PlikiTestowe.UtworzPdf(folder);
        using var cert = CertyfikatyTestowe.Utworz(imie: "Anna", nazwisko: "Nowak");
        new PodpisywaczPdf(new TestowyTsa()).Podpisz(wejscie, wejscie, cert,
            new OpcjePodpisu { Stanowisko = "Inspektor", Motyw = MotywWizualizacji.Akcent });
        using var pdf = new PdfDocument(new PdfReader(wejscie));
        var nazwa = new iText.Signatures.SignatureUtil(pdf).GetSignatureNames()[0];
        Assert.True(new iText.Signatures.SignatureUtil(pdf).ReadSignatureData(nazwa).VerifySignatureIntegrityAndAuthenticity());
        // Tekst wyglądu pola podpisu (strumień wyglądu adnotacji, nie treść strony).
        var wyglad = iText.Forms.PdfAcroForm.GetAcroForm(pdf, false).GetField(nazwa).GetWidgets()[0].GetNormalAppearanceObject();
        var z = new Zbieracz();
        var procesor = new PdfCanvasProcessor(z);
        Przetworz(procesor, (PdfStream)wyglad);
        Assert.Contains(z.Fragmenty, x => x.Tekst.Contains("PODPISANO"));
        Assert.Contains(z.Fragmenty, x => x.Tekst.StartsWith("Anna", StringComparison.Ordinal) && x.Kolor[2] > x.Kolor[0]); // granat
    }

    /// <summary>Treść strumienia wyglądu razem z zagnieżdżonymi formularzami (iText zagnieżdża je w kilku warstwach).</summary>
    private static void Przetworz(PdfCanvasProcessor procesor, PdfStream strumien)
    {
        var zasoby = strumien.GetAsDictionary(PdfName.Resources);
        procesor.ProcessContent(strumien.GetBytes(), new PdfResources(zasoby ?? new PdfDictionary()));
    }

    [Theory]
    [InlineData(1f, 11f)]
    [InlineData(1.5f, 16.5f)]
    [InlineData(1.37f, 15f)]
    [InlineData(0.6f, 7f)]   // poniżej najmniejszego rozmiaru
    [InlineData(5f, 33f)]    // powyżej największego
    public void SkalaZaokraglonaDoPolPunktuImieniaINazwiska(float skala, float oczekiwanyRozmiar)
    {
        var s = Wizualizacja.ZaokraglijSkale(skala);
        Assert.Equal(oczekiwanyRozmiar, Wizualizacja.RozmiarNazwiskaWSkali(s), 0.001);
        // Imię i nazwisko w podpisie ma dokładnie ten rozmiar, reszta proporcjonalnie.
        var f = Tekst(new DaneWizualizacji("Anna Nowak", "Inspektor", null, Czas, MotywWizualizacji.Hierarchia), s);
        Assert.Equal(oczekiwanyRozmiar, Wiersz(f, "Anna").Rozmiar, 0.01);
        Assert.Equal(9f * s, Wiersz(f, "Inspektor").Rozmiar, 0.01);
    }

    [Fact]
    public void PieczatkaStanowiskoNadNazwiskiemWysrodkowane()
    {
        var dane = new DaneWizualizacji("Anna Nowak", "Kierownik Biura Informatyki\nUrzędu Miasta Wodzisławia Śląskiego", null, Czas,
            MotywWizualizacji.Pieczatka, true);
        var pdf = Wizualizacja.PodgladPdf(dane);
        using var dok = new PdfDocument(new PdfReader(new MemoryStream(pdf)));
        var tekst = PdfTextExtractor.GetTextFromPage(dok.GetPage(1)).Split('\n');
        Assert.Equal(new[] { "PODPISANO ELEKTRONICZNIE", "Kierownik Biura Informatyki", "Urzędu Miasta Wodzisławia Śląskiego", "Anna Nowak" },
            tekst.Take(4).Select(w => w.Trim()).Select(w => w.Replace(" ", "") == "PODPISANOELEKTRONICZNIE" ? "PODPISANO ELEKTRONICZNIE" : w));

        // Wyśrodkowanie: krótki wiersz (imię i nazwisko) ma po obu stronach podobny margines.
        var o = DokumentPdf.Renderuj(pdf, 0, 4);
        bool Ciemny(int x, int y) { var i = (y * o.Szerokosc + x) * 4; return o.Bgra[i] + o.Bgra[i + 1] + o.Bgra[i + 2] < 300; }
        var wiersze = Enumerable.Range(0, o.Wysokosc).Where(y => Enumerable.Range(0, o.Szerokosc).Count(x => Ciemny(x, y)) > 0).ToList();
        // Wiersz z nazwiskiem: najgrubsze (pogrubione) piksele w dolnej połowie.
        var y0 = wiersze.Where(y => y > o.Wysokosc / 2).OrderByDescending(y => Enumerable.Range(0, o.Szerokosc).Count(x => Ciemny(x, y))).First();
        var xs = Enumerable.Range(0, o.Szerokosc).Where(x => Ciemny(x, y0)).ToList();
        Assert.InRange((xs.Min() - 0) - (o.Szerokosc - 1 - xs.Max()), -12, 12);
    }
}
