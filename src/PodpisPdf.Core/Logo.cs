using iText.IO.Image;
using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas;
using iText.Kernel.Pdf.Xobject;

namespace PodpisPdf.Core;

/// <summary>
/// Logo w najlepszej jakości: oryginalne dane obrazu bez zmniejszania rozdzielczości i bez stratnej
/// kompresji. Puste (białe lub przezroczyste) marginesy są ukrywane przycięciem w PDF, nie przez
/// przeliczanie obrazu, więc logo zajmuje całe dostępne miejsce.
/// </summary>
internal static class Logo
{
    // Piksel jaśniejszy niż ten próg (w każdym kanale) uznajemy za tło.
    private const byte ProgTla = 245;
    // Minimalny zysk, przy którym warto przycinać (ułamek wymiaru).
    private const float MinimalnyMargines = 0.02f;
    // Zostawiany zapas wokół treści (ułamek wymiaru), żeby nie ściąć antyaliasingu krawędzi.
    private const float Zapas = 0.01f;
    private const int RozdzielczoscAnalizy = 800;

    /// <summary>Obszar treści logo jako ułamki szerokości/wysokości, licząc od lewego górnego rogu.</summary>
    internal sealed record Obszar(float Lewo, float Gora, float Prawo, float Dol)
    {
        public static readonly Obszar Calosc = new(0, 0, 1, 1);
        public float Szerokosc => Prawo - Lewo;
        public float Wysokosc => Dol - Gora;
    }

    /// <summary>Obraz do wizualizacji wraz z proporcjami (szerokość/wysokość) widocznej części.</summary>
    public static (PdfXObject Obraz, float Proporcje) Przygotuj(ImageData dane)
    {
        var obraz = new PdfImageXObject(dane);
        obraz.Put(PdfName.Interpolate, PdfBoolean.TRUE);
        float w = dane.GetWidth(), h = dane.GetHeight();

        var obszar = ZnajdzTresc(dane);
        if (obszar == Obszar.Calosc)
            return (obraz, w / h);

        // Formularz (XObject) o rozmiarze widocznej części; obraz rysowany w pełnym rozmiarze z przesunięciem.
        var cw = obszar.Szerokosc * w;
        var ch = obszar.Wysokosc * h;
        var formularz = new PdfFormXObject(new Rectangle(0, 0, cw, ch));
        var nazwa = formularz.GetResources().AddImage(obraz);
        var tresc = FormattableString.Invariant(
            $"q {w} 0 0 {h} {-obszar.Lewo * w} {-(1 - obszar.Dol) * h} cm {nazwa} Do Q");
        formularz.GetPdfObject().SetData(System.Text.Encoding.ASCII.GetBytes(tresc));
        return (formularz, cw / ch);
    }

    /// <summary>Wyszukuje treść logo, renderując je przez PDFium (obsługuje PNG z przezroczystością i JPG).</summary>
    internal static Obszar ZnajdzTresc(ImageData dane)
    {
        float w = dane.GetWidth(), h = dane.GetHeight();
        byte[] pdf;
        using (var ms = new MemoryStream())
        {
            using (var dokument = new PdfDocument(new PdfWriter(ms)))
            {
                var strona = dokument.AddNewPage(new PageSize(w, h));
                new PdfCanvas(strona).AddXObjectFittedIntoRectangle(new PdfImageXObject(dane), new Rectangle(0, 0, w, h));
            }
            pdf = ms.ToArray();
        }

        ObrazStrony obraz;
        try
        {
            obraz = DokumentPdf.Renderuj(pdf, 0, RozdzielczoscAnalizy / Math.Max(w, h));
        }
        catch (Exception)
        {
            return Obszar.Calosc; // Bez analizy logo i tak zostanie pokazane, tylko bez przycięcia.
        }

        int minX = obraz.Szerokosc, minY = obraz.Wysokosc, maxX = -1, maxY = -1;
        for (var y = 0; y < obraz.Wysokosc; y++)
        {
            for (var x = 0; x < obraz.Szerokosc; x++)
            {
                var i = (y * obraz.Szerokosc + x) * 4;
                if (obraz.Bgra[i] >= ProgTla && obraz.Bgra[i + 1] >= ProgTla && obraz.Bgra[i + 2] >= ProgTla)
                    continue;
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }
        }
        if (maxX < 0)
            return Obszar.Calosc; // Puste logo.

        var obszar = new Obszar(
            Math.Max(0, (float)minX / obraz.Szerokosc - Zapas),
            Math.Max(0, (float)minY / obraz.Wysokosc - Zapas),
            Math.Min(1, (float)(maxX + 1) / obraz.Szerokosc + Zapas),
            Math.Min(1, (float)(maxY + 1) / obraz.Wysokosc + Zapas));

        return obszar.Szerokosc > 1 - MinimalnyMargines && obszar.Wysokosc > 1 - MinimalnyMargines
            ? Obszar.Calosc
            : obszar;
    }
}
