using iText.IO.Image;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Xobject;
using PodpiszPdf.Core;
using Xunit;

namespace PodpiszPdf.Tests;

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
        Assert.False(wewnetrzny.ContainsKey(PdfName.Interpolate)); // zabronione w PDF/A
    }
}

public class LogoWektoroweTesty
{
    private const string Svg = """
        <svg xmlns="http://www.w3.org/2000/svg" width="300" height="300" viewBox="0 0 300 300">
          <rect width="300" height="300" fill="white"/>
          <path d="M100 60 H200 V160 Q200 230 150 250 Q100 230 100 160 Z" fill="none" stroke="black" stroke-width="5"/>
          <circle cx="125" cy="130" r="15" fill="#c8102e"/>
        </svg>
        """;

    /// <summary>Logo PDF z osadzoną czcionką, obrazem i przesuniętym CropBox (jak pliki z programów graficznych).</summary>
    private static string UtworzLogoPdf(string folder)
    {
        var sciezka = Path.Combine(folder, "logo.pdf");
        using (var pdf = new PdfDocument(new PdfWriter(sciezka)))
        {
            var strona = pdf.AddNewPage(new iText.Kernel.Geom.PageSize(400, 400));
            strona.SetCropBox(new iText.Kernel.Geom.Rectangle(50, 50, 300, 300));
            using var zasob = typeof(Wizualizacja).Assembly.GetManifestResourceStream("PodpiszPdf.Core.LiberationSans-Regular.ttf")!;
            using var bajty = new MemoryStream();
            zasob.CopyTo(bajty);
            var czcionka = iText.Kernel.Font.PdfFontFactory.CreateFont(bajty.ToArray(), iText.IO.Font.PdfEncodings.IDENTITY_H,
                iText.Kernel.Font.PdfFontFactory.EmbeddingStrategy.FORCE_EMBEDDED);
            var plotno = new iText.Kernel.Pdf.Canvas.PdfCanvas(strona);
            plotno.SetFillColor(iText.Kernel.Colors.ColorConstants.BLUE).Rectangle(120, 150, 160, 160).Fill();
            plotno.AddImageFittedIntoRectangle(ImageDataFactory.Create(File.ReadAllBytes(PlikiTestowe.UtworzLogo(folder))),
                new iText.Kernel.Geom.Rectangle(120, 110, 80, 40), false);
            plotno.BeginText().SetFontAndSize(czcionka, 20).MoveText(120, 80).ShowText("Śląski").EndText();
        }
        return sciezka;
    }

    [Fact]
    public void LogoSvgJestWektoroweIPrzyciete()
    {
        var folder = PlikiTestowe.Folder();
        var sciezka = Path.Combine(folder, "logo.svg");
        File.WriteAllText(sciezka, Svg);

        var (obraz, proporcje) = Logo.Wczytaj(sciezka);

        Assert.IsType<PdfFormXObject>(obraz);
        Assert.InRange(proporcje, 0.45f, 0.6f); // tarcza 100x190, a nie cały kwadrat 300x300
    }

    [Theory]
    [InlineData("svg")]
    [InlineData("pdf")]
    public void PodpisZLogoWektorowym(string format)
    {
        var folder = PlikiTestowe.Folder();
        string logo;
        if (format == "svg")
        {
            logo = Path.Combine(folder, "logo.svg");
            File.WriteAllText(logo, Svg);
        }
        else
        {
            logo = UtworzLogoPdf(folder);
        }
        var wejscie = PlikiTestowe.UtworzPdf(folder);
        var wyjscie = Path.Combine(folder, "w.pdf");
        using var cert = CertyfikatyTestowe.Utworz();

        new PodpisywaczPdf(new TestowyTsa()).Podpisz(wejscie, wyjscie, cert,
            new OpcjePodpisu { SciezkaLogo = logo, Miejsce = new MiejscePodpisu(1, 100, 100) });
        File.Copy(wyjscie, Path.Combine(Path.GetTempPath(), $"PodpiszPdf-logo-{format}.pdf"), true);

        using (var pdf = new PdfDocument(new PdfReader(wyjscie)))
        {
            var su = new iText.Signatures.SignatureUtil(pdf);
            Assert.True(su.ReadSignatureData(su.GetSignatureNames()[0]).VerifySignatureIntegrityAndAuthenticity());
        }

        // Logo widoczne na wyrenderowanej stronie: lewa część prostokąta podpisu nie jest pusta.
        const double skala = 2;
        var o = DokumentPdf.Renderuj(File.ReadAllBytes(wyjscie), 0, skala);
        var rozmiar = Wizualizacja.Rozmiar(new DaneWizualizacji(CertyfikatInfo.PobierzImieNazwisko(cert), null, logo, DateTimeOffset.Now));
        int x0 = (int)(100 * skala), x1 = (int)((100 + rozmiar.Szerokosc * 0.3) * skala);
        int y0 = o.Wysokosc - (int)((100 + rozmiar.Wysokosc) * skala), y1 = o.Wysokosc - (int)(100 * skala);
        var kolorowe = 0;
        for (var y = y0; y < y1; y++)
            for (var x = x0; x < x1; x++)
            {
                var i = (y * o.Szerokosc + x) * 4;
                if (o.Bgra[i] + o.Bgra[i + 1] + o.Bgra[i + 2] < 600)
                    kolorowe++;
            }
        Assert.True(kolorowe > 50, $"logo niewidoczne ({kolorowe} px)");
    }
}
