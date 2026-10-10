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
