using PodpiszPdf.Core;
using Xunit;

namespace PodpiszPdf.Tests;

public class DokumentPdfTesty
{
    [Fact]
    public void StronyIRenderowaniePodgladu()
    {
        var folder = PlikiTestowe.Folder();
        var pdf = PlikiTestowe.UtworzPdf(folder, strony: 3);

        var strony = DokumentPdf.Strony(pdf);
        Assert.Equal(3, strony.Count);
        Assert.All(strony, s => Assert.Equal(0, s.Obrot));
        Assert.Equal(595f, strony[0].Szerokosc, 1f);

        var obraz = DokumentPdf.Renderuj(File.ReadAllBytes(pdf), 1, 0.5);
        Assert.InRange(obraz.Szerokosc, 296, 299);
        Assert.Equal(obraz.Szerokosc * obraz.Wysokosc * 4, obraz.Bgra.Length);
        Assert.Contains(obraz.Bgra.Where((_, i) => i % 4 == 0), b => b < 128); // jest tekst
        Assert.All(obraz.Bgra.Where((_, i) => i % 4 == 3), a => Assert.Equal(255, a)); // białe tło, bez przezroczystości
    }

    [Fact]
    public void PodgladWizualizacjiMaJejRozmiar()
    {
        var folder = PlikiTestowe.Folder();
        var dane = new DaneWizualizacji("Mateusz Kozielski", "Kierownik", PlikiTestowe.UtworzLogo(folder), DateTimeOffset.Now);

        var pdf = Wizualizacja.PodgladPdf(dane);
        var obraz = DokumentPdf.Renderuj(pdf, 0, 2);

        var rozmiar = Wizualizacja.Rozmiar(dane);
        Assert.InRange(obraz.Szerokosc, (int)(rozmiar.Szerokosc * 2) - 1, (int)(rozmiar.Szerokosc * 2) + 1);
        Assert.InRange(obraz.Wysokosc, (int)(rozmiar.Wysokosc * 2) - 1, (int)(rozmiar.Wysokosc * 2) + 1);
        Assert.Contains(obraz.Bgra.Where((_, i) => i % 4 == 0), b => b < 128);
    }
}

public class WyrownanieWizualizacjiTesty
{
    [Theory]
    [InlineData(null, true)]
    [InlineData("Kierownik", true)]
    [InlineData("Kierownik biura informatyki\nUrzędu Miasta Wodzisławia Śląskiego", true)]
    [InlineData("Kierownik biura informatyki\nUrzędu Miasta Wodzisławia Śląskiego", false)]
    public void LogoWyrownaneDoBlokuTekstu(string? stanowisko, bool pionowe)
    {
        var folder = PlikiTestowe.Folder();
        var sciezkaLogo = pionowe ? PlikiTestowe.UtworzLogoPionowe(folder) : PlikiTestowe.UtworzLogo(folder);
        var dane = new DaneWizualizacji("Mateusz Kozielski", stanowisko, sciezkaLogo, DateTimeOffset.Now);
        const int skala = 4;
        var o = DokumentPdf.Renderuj(Wizualizacja.PodgladPdf(dane), 0, skala);

        bool Ciemny(int x, int y)
        {
            var i = (y * o.Szerokosc + x) * 4;
            return o.Bgra[i] + o.Bgra[i + 1] + o.Bgra[i + 2] < 600;
        }
        var kolumny = Enumerable.Range(0, o.Szerokosc).Where(x => Enumerable.Range(0, o.Wysokosc).Any(y => Ciemny(x, y))).ToList();
        var przerwa = Enumerable.Range(0, kolumny.Count - 1).First(i => kolumny[i + 1] - kolumny[i] > 10);
        (int Gora, int Dol) Zakres(int x0, int x1)
        {
            var wiersze = Enumerable.Range(0, o.Wysokosc).Where(y => Enumerable.Range(x0, x1 - x0).Any(x => Ciemny(x, y))).ToList();
            return (wiersze.First(), wiersze.Last());
        }
        var logo = Zakres(kolumny[0], kolumny[przerwa] + 1);
        var tekst = Zakres(kolumny[przerwa + 1], kolumny[^1] + 1);

        // Tolerancja 1,5 pt (6 px przy skali 4).
        if (pionowe)
        {
            // Góra logo równo z górą pierwszego wiersza, dół równo z dołem ostatniego.
            Assert.InRange(logo.Gora - tekst.Gora, -6, 6);
            Assert.InRange(logo.Dol - tekst.Dol, -6, 6);
        }
        else
        {
            // Szerokie logo jest ograniczone szerokością — wtedy jest wyśrodkowane względem tekstu.
            Assert.InRange(logo.Gora + logo.Dol - tekst.Gora - tekst.Dol, -12, 12);
        }
    }
}

public class SkalowanieWizualizacjiTesty
{
    [Theory]
    [InlineData(0.6f)]
    [InlineData(1.5f)]
    [InlineData(2.2f)] // największy, który mieści się na szerokości A4
    public void PodpisWSkaliMaProporcjonalnyRozmiar(float skala)
    {
        var folder = PlikiTestowe.Folder();
        var wejscie = PlikiTestowe.UtworzPdf(folder);
        var wyjscie = Path.Combine(folder, "w.pdf");
        using var cert = CertyfikatyTestowe.Utworz();

        new PodpisywaczPdf(new TestowyTsa()).Podpisz(wejscie, wyjscie, cert, new OpcjePodpisu
        {
            Stanowisko = "Kierownik",
            SciezkaLogo = PlikiTestowe.UtworzLogoPionowe(folder),
            Miejsce = new MiejscePodpisu(1, 10, 50, skala),
        });

        using var pdf = new iText.Kernel.Pdf.PdfDocument(new iText.Kernel.Pdf.PdfReader(wyjscie));
        var su = new iText.Signatures.SignatureUtil(pdf);
        var nazwa = su.GetSignatureNames()[0];
        Assert.True(su.ReadSignatureData(nazwa).VerifySignatureIntegrityAndAuthenticity());
        var rect = iText.Forms.PdfAcroForm.GetAcroForm(pdf, false).GetField(nazwa).GetWidgets()[0].GetRectangle().ToRectangle();
        var rozmiar = Wizualizacja.Rozmiar(new DaneWizualizacji(CertyfikatInfo.PobierzImieNazwisko(cert), "Kierownik",
            PlikiTestowe.UtworzLogoPionowe(folder), DateTimeOffset.Now));
        Assert.Equal(rozmiar.Szerokosc * skala, rect.GetWidth(), 0.01);
        Assert.Equal(rozmiar.Wysokosc * skala, rect.GetHeight(), 0.01);
        Assert.Equal(rozmiar.Proporcje, rect.GetWidth() / rect.GetHeight(), 0.001);
    }

    [Fact]
    public void TrescSkalujeSieRazemZProstokatem()
    {
        var folder = PlikiTestowe.Folder();
        var dane = new DaneWizualizacji("Mateusz Kozielski", "Kierownik biura informatyki\nUrzędu Miasta Wodzisławia Śląskiego",
            PlikiTestowe.UtworzLogoPionowe(folder), DateTimeOffset.Now);

        // Ten sam rozmiar w pikselach: skala 1 renderowana 3×, skala 2 renderowana 1,5×.
        var a = DokumentPdf.Renderuj(Wizualizacja.PodgladPdf(dane, 1f), 0, 3);
        var b = DokumentPdf.Renderuj(Wizualizacja.PodgladPdf(dane, 2f), 0, 1.5);
        Assert.InRange(b.Szerokosc - a.Szerokosc, -1, 1);

        static (int L, int G, int P, int D) Tresc(ObrazStrony o)
        {
            int l = o.Szerokosc, g = o.Wysokosc, p = -1, d = -1;
            for (var y = 0; y < o.Wysokosc; y++)
                for (var x = 0; x < o.Szerokosc; x++)
                {
                    var i = (y * o.Szerokosc + x) * 4;
                    if (o.Bgra[i] + o.Bgra[i + 1] + o.Bgra[i + 2] >= 600) continue;
                    l = Math.Min(l, x); p = Math.Max(p, x); g = Math.Min(g, y); d = Math.Max(d, y);
                }
            return (l, g, p, d);
        }
        var ta = Tresc(a);
        var tb = Tresc(b);
        // Układ identyczny co do kilku pikseli (zaokrąglenia przy doborze czcionki).
        Assert.InRange(tb.L - ta.L, -4, 4);
        Assert.InRange(tb.G - ta.G, -4, 4);
        Assert.InRange(tb.P - ta.P, -4, 4);
        Assert.InRange(tb.D - ta.D, -4, 4);
    }

    [Fact]
    public void SkalaPozaZakresemDajeZrozumialyBlad()
    {
        var folder = PlikiTestowe.Folder();
        var wejscie = PlikiTestowe.UtworzPdf(folder);
        using var cert = CertyfikatyTestowe.Utworz();
        var e = Assert.Throws<BladPodpisu>(() => new PodpisywaczPdf(new TestowyTsa()).Podpisz(wejscie,
            Path.Combine(folder, "w.pdf"), cert, new OpcjePodpisu { Miejsce = new MiejscePodpisu(1, 10, 10, 5f) }));
        Assert.Contains("rozmiar", e.Message);
    }

    [Fact]
    public void DopasowanieDoStronyUwzglednniaSkale()
    {
        var strona = new iText.Kernel.Geom.Rectangle(0, 0, 595, 842);
        var rozmiar = new RozmiarWizualizacji(140, 50);
        var (x, y) = Wizualizacja.DopasujDoStrony(strona, 590, 5, rozmiar, 2f);
        Assert.Equal(595 - 140 * 2, x, 0.01);
        Assert.Equal(0, y, 0.01);
    }
}

public class WspolbieznoscTesty
{
    /// <summary>
    /// Kilka wizualizacji jednocześnie (np. podpis w trakcie przygotowywania podglądu) — czcionki nie mogą
    /// być współdzielone między wątkami (iText czyta je ze wspólnego bufora).
    /// </summary>
    [Fact]
    public void WizualizacjeRownolegle()
    {
        var bledy = new System.Collections.Concurrent.ConcurrentBag<Exception>();
        Parallel.For(0, 64, new ParallelOptions { MaxDegreeOfParallelism = 16 }, i =>
        {
            try
            {
                var dane = new DaneWizualizacji($"Osoba {i} Zażółć Gęślą Jaźń", "Kierownik Biura Informatyki\nUrzędu Miasta Wodzisławia Śląskiego " + i, null, DateTimeOffset.Now);
                Wizualizacja.PodgladPdf(dane, 1f + i % 3 * 0.5f);
            }
            catch (Exception e)
            {
                bledy.Add(e);
            }
        });
        Assert.Empty(bledy);
    }

    /// <summary>Renderowanie z wielu wątków naraz (podgląd, miniatury, podpis) — PDFium działa na jednym wątku.</summary>
    [Fact]
    public void RenderowanieRownolegle()
    {
        var folder = PlikiTestowe.Folder();
        var pdf = File.ReadAllBytes(PlikiTestowe.UtworzPdf(folder, strony: 3));
        // Podpisany dokument: wizualizacja jest polem formularza (rysowanym przez środowisko formularzy PDFium).
        var podpisany = Path.Combine(folder, "podpisany.pdf");
        using (var cert = CertyfikatyTestowe.Utworz())
            new PodpisywaczPdf(new TestowyTsa()).Podpisz(PlikiTestowe.UtworzPdf(folder, nazwa: "do-podpisu.pdf"), podpisany, cert, new OpcjePodpisu());
        var pdfPodpisany = File.ReadAllBytes(podpisany);
        var bledy = new System.Collections.Concurrent.ConcurrentBag<Exception>();
        Parallel.For(0, 96, new ParallelOptions { MaxDegreeOfParallelism = 12 }, i =>
        {
            try
            {
                var obraz = i % 2 == 0
                    ? DokumentPdf.Renderuj(pdf, i % 3, 0.3 + i % 4 * 0.2, bialeTlo: i % 4 == 0)
                    : DokumentPdf.Renderuj(pdfPodpisany, 1, 0.5 + i % 3 * 0.25);
                Assert.Equal(obraz.Szerokosc * obraz.Wysokosc * 4, obraz.Bgra.Length);
            }
            catch (Exception e)
            {
                bledy.Add(e);
            }
        });
        Assert.Empty(bledy);
        Assert.ThrowsAny<Exception>(() => DokumentPdf.Renderuj(pdf, 99, 1)); // błąd wraca do wywołującego, wątek działa dalej
        Assert.True(DokumentPdf.Renderuj(pdf, 0, 0.2).Szerokosc > 0);
    }
}

public class RozmiaryCzcionekTesty
{
    private sealed class Zbieracz : iText.Kernel.Pdf.Canvas.Parser.Listener.IEventListener
    {
        public List<(string Tekst, float Rozmiar, string Czcionka)> Fragmenty { get; } = new();

        public void EventOccurred(iText.Kernel.Pdf.Canvas.Parser.Data.IEventData dane, iText.Kernel.Pdf.Canvas.Parser.EventType typ)
        {
            if (dane is iText.Kernel.Pdf.Canvas.Parser.Data.TextRenderInfo t)
                Fragmenty.Add((t.GetText(), t.GetFontSize(), t.GetFont().GetFontProgram().GetFontNames().GetFontName()));
        }

        public ICollection<iText.Kernel.Pdf.Canvas.Parser.EventType> GetSupportedEvents() =>
            new[] { iText.Kernel.Pdf.Canvas.Parser.EventType.RENDER_TEXT };
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(2f)]
    public void ImieINazwisko11ptNaglowekIData8pt(float skala)
    {
        var dane = new DaneWizualizacji("Anna Nowak", "Inspektor", null, DateTimeOffset.Now);
        using var pdf = new iText.Kernel.Pdf.PdfDocument(new iText.Kernel.Pdf.PdfReader(new MemoryStream(Wizualizacja.PodgladPdf(dane, skala))));
        var z = new Zbieracz();
        new iText.Kernel.Pdf.Canvas.Parser.PdfCanvasProcessor(z).ProcessPageContent(pdf.GetPage(1));

        float Rozmiar(string poczatek) => z.Fragmenty.First(f => f.Tekst.StartsWith(poczatek)).Rozmiar;
        Assert.Equal(11 * skala, Rozmiar("Anna"), 0.01);
        Assert.Equal(6.5f * skala, Rozmiar("PODPISANO"), 0.01);
        Assert.Equal(8 * skala, Rozmiar("Data"), 0.01);
        Assert.InRange(Rozmiar("Inspektor"), 7 * skala - 0.01, 9 * skala + 0.01);
        Assert.All(z.Fragmenty, f => Assert.Matches("Arial|LiberationSans", f.Czcionka));
    }

    [Fact]
    public void DlugieStanowiskoZawijaSieIPoszerzaWizualizacje()
    {
        var krotkie = Wizualizacja.Rozmiar(new DaneWizualizacji("Anna Nowak", "Inspektor", null, DateTimeOffset.Now));
        var dlugie = Wizualizacja.Rozmiar(new DaneWizualizacji("Anna Nowak",
            "Zastępca Kierownika Wydziału Gospodarki Nieruchomościami i Geodezji Urzędu Miasta Wodzisławia Śląskiego", null, DateTimeOffset.Now));
        Assert.True(dlugie.Wysokosc > krotkie.Wysokosc); // dwa wiersze stanowiska
        Assert.True(dlugie.Szerokosc > krotkie.Szerokosc);
        var bez = Wizualizacja.Rozmiar(new DaneWizualizacji("Anna Nowak", null, null, DateTimeOffset.Now));
        Assert.True(bez.Wysokosc < krotkie.Wysokosc);
    }
}
