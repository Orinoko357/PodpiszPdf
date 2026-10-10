using iText.IO.Image;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Xobject;
using PodpisPdf.Core;
using Xunit;

namespace PodpisPdf.Tests;

public class LogoTesty
{
    /// <summary>Obraz RGB 200x100 z czarnym prostokątem w (50..149, 20..59) na białym tle.</summary>
    private static ImageData ObrazZMarginesami()
    {
        const int w = 200, h = 100;
        var dane = new byte[w * h * 3];
        Array.Fill(dane, (byte)255);
        for (var y = 20; y < 60; y++)
            for (var x = 50; x < 150; x++)
                Array.Clear(dane, (y * w + x) * 3, 3);
        return ImageDataFactory.Create(w, h, 3, 8, dane, null);
    }

    [Fact]
    public void ZnajdujeTrescBezBialychMarginesow()
    {
        var obszar = Logo.ZnajdzTresc(ObrazZMarginesami());
        Assert.Equal(0.25f, obszar.Lewo, 0.02f);
        Assert.Equal(0.75f, obszar.Prawo, 0.02f);
        Assert.Equal(0.20f, obszar.Gora, 0.02f);
        Assert.Equal(0.60f, obszar.Dol, 0.02f);
    }

    [Fact]
    public void ObrazBezMarginesowNieJestPrzycinany()
    {
        var folder = PlikiTestowe.Folder();
        var logo = ImageDataFactory.Create(File.ReadAllBytes(PlikiTestowe.UtworzLogo(folder)));
        Assert.Equal(Logo.Obszar.Calosc, Logo.ZnajdzTresc(logo));
    }

    [Fact]
    public void PrzycieciePrzezPdfZachowujeOryginalnyObraz()
    {
        var (obraz, proporcje) = Logo.Przygotuj(ObrazZMarginesami());

        var formularz = Assert.IsType<PdfFormXObject>(obraz);
        Assert.InRange(proporcje, 2.2f, 2.8f); // ok. 100x40 px treści
        var xobjects = formularz.GetResources().GetResource(PdfName.XObject);
        var wewnetrzny = Assert.IsType<PdfStream>(xobjects.Get(xobjects.KeySet().Single()));
        Assert.Equal(200, wewnetrzny.GetAsNumber(PdfName.Width).IntValue()); // pełna rozdzielczość
        Assert.Equal(PdfBoolean.TRUE, wewnetrzny.GetAsBoolean(PdfName.Interpolate));
    }
}
