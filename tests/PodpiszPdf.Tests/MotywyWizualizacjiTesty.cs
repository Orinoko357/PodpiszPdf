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
        Assert.StartsWith("Data: 2026-10-11", string.Concat(f.SkipWhile(x => !x.Tekst.StartsWith("Data")).Select(x => x.Tekst)).Trim());
    }

    [Theory]
    [InlineData(MotywWizualizacji.Klasyczny, "Data: 2026-10-11 16:02 (UTC+02:00)")]
    [InlineData(MotywWizualizacji.Hierarchia, "11.10.2026, 16:02 (UTC+02:00)")]
    [InlineData(MotywWizualizacji.Linia, "Data podpisu: 11.10.2026, 16:02")]
    [InlineData(MotywWizualizacji.Akcent, "11.10.2026, 16:02 (UTC+02:00)")]
    public void FormatDaty(MotywWizualizacji motyw, string oczekiwany)
    {
        var czas = new DateTimeOffset(2026, 10, 11, 16, 2, 0, TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 10, 11, 16, 2, 0)));
        var tekst = Wizualizacja.TekstDaty(new DaneWizualizacji("A", null, null, czas, motyw));
        var strefa = czas.Offset;
        Assert.Equal(oczekiwany.Replace("UTC+02:00", $"UTC{(strefa < TimeSpan.Zero ? "-" : "+")}{strefa.Duration():hh\\:mm}"), tekst);
    }

    [Fact]
    public void DataPodKreskaToSamaData()
    {
        var tekst = Wizualizacja.TekstDaty(new DaneWizualizacji("A", null, null, Czas, MotywWizualizacji.DataPodKreska, true));
        Assert.Equal(Wizualizacja.FormatujCzasPolski(Czas, zeStrefa: true), tekst);
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
}
