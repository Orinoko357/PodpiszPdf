using PodpisPdf.Core;
using Xunit;

namespace PodpisPdf.Tests;

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

        Assert.InRange(obraz.Szerokosc, (int)(Wizualizacja.SzerokoscPt * 2) - 1, (int)(Wizualizacja.SzerokoscPt * 2) + 1);
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
