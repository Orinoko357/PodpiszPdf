using iText.IO.Image;
using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas;
using iText.Kernel.Pdf.Xobject;
using iText.Svg.Converter;

namespace PodpisPdf.Core;

/// <summary>
/// Logo w najlepszej jakości.
/// <list type="bullet">
/// <item>Logo wektorowe (SVG, PDF) trafia do podpisu jako grafika wektorowa — ostre w każdym powiększeniu.</item>
/// <item>Logo rastrowe (PNG, JPG) — oryginalne dane obrazu, bez zmniejszania rozdzielczości i bez stratnej kompresji.</item>
/// </list>
/// Puste (białe lub przezroczyste) marginesy są ukrywane przycięciem w PDF, bez przeliczania grafiki,
/// więc logo zajmuje całe dostępne miejsce.
/// </summary>
internal static class Logo
{
    /// <summary>Rozszerzenia plików logo obsługiwane przez program.</summary>
    public static readonly string[] Rozszerzenia = { ".svg", ".pdf", ".png", ".jpg", ".jpeg" };

    // Piksel jaśniejszy niż ten próg (w każdym kanale) uznajemy za tło.
    private const byte ProgTla = 245;
    // Minimalny zysk, przy którym warto przycinać (ułamek wymiaru).
    private const float MinimalnyMargines = 0.02f;
    // Zostawiany zapas wokół treści (ułamek wymiaru), żeby nie ściąć antyaliasingu krawędzi.
    private const float Zapas = 0.003f;
    private const int RozdzielczoscAnalizy = 800;

    /// <summary>Obszar treści logo jako ułamki szerokości/wysokości, licząc od lewego górnego rogu.</summary>
    internal sealed record Obszar(float Lewo, float Gora, float Prawo, float Dol)
    {
        public static readonly Obszar Calosc = new(0, 0, 1, 1);
        public float Szerokosc => Prawo - Lewo;
        public float Wysokosc => Dol - Gora;
    }

    /// <summary>Wczytuje logo z pliku i przygotowuje je do wizualizacji.</summary>
    public static (PdfXObject Obraz, float Proporcje) Wczytaj(string sciezka)
    {
        var dane = File.ReadAllBytes(sciezka);
        switch (System.IO.Path.GetExtension(sciezka).ToLowerInvariant())
        {
            case ".svg":
                return ZWektora(SvgDoPdf(dane));
            case ".pdf":
                return ZWektora(dane);
            default:
                var obraz = ImageDataFactory.Create(dane);
                if (obraz.GetWidth() <= 0 || obraz.GetHeight() <= 0)
                    throw new InvalidDataException("obraz o zerowym rozmiarze");
                return Przygotuj(obraz);
        }
    }

    /// <summary>Logo rastrowe.</summary>
    public static (PdfXObject Obraz, float Proporcje) Przygotuj(ImageData dane)
    {
        var obraz = new PdfImageXObject(dane);
        obraz.Put(PdfName.Interpolate, PdfBoolean.TRUE);
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

        // Obraz rysowany jest w kwadracie jednostkowym, więc macierz musi go rozciągnąć do w × h.
        return Przytnij(obraz, w, h, ZnajdzTresc(pdf), skalujDoRozmiaru: true);
    }

    /// <summary>Logo wektorowe z pierwszej strony dokumentu PDF.</summary>
    private static (PdfXObject Obraz, float Proporcje) ZWektora(byte[] pdf)
    {
        using var dokument = new PdfDocument(new PdfReader(new MemoryStream(pdf)));
        if (dokument.GetNumberOfPages() < 1)
            throw new InvalidDataException("plik logo nie ma żadnej strony");
        var strona = dokument.GetPage(1);
        var c = strona.GetCropBox();

        // Formularz (XObject) niezależny od dokumentu źródłowego: treść strony i kopia jej zasobów.
        // Dzięki temu można go użyć w dowolnym dokumencie, który dopiero powstanie przy podpisie.
        var formularz = new PdfFormXObject(new Rectangle(c.GetLeft(), c.GetBottom(), c.GetWidth(), c.GetHeight()));
        formularz.GetPdfObject().SetData(strona.GetContentBytes());
        formularz.GetPdfObject().Put(PdfName.Matrix, new PdfArray(new float[] { 1, 0, 0, 1, -c.GetLeft(), -c.GetBottom() }));
        var zasoby = strona.GetPdfObject().GetAsDictionary(PdfName.Resources);
        if (zasoby != null)
            formularz.GetPdfObject().Put(PdfName.Resources, Odlacz(zasoby, new Dictionary<PdfObject, PdfObject>(ReferenceEqualityComparer.Instance)));

        return Przytnij(formularz, c.GetWidth(), c.GetHeight(), ZnajdzTresc(pdf), skalujDoRozmiaru: false);
    }

    private static byte[] SvgDoPdf(byte[] svg)
    {
        using var wynik = new MemoryStream();
        SvgConverter.CreatePdf(new MemoryStream(svg), wynik);
        return wynik.ToArray();
    }

    /// <summary>
    /// Głęboka kopia obiektu PDF bez odwołań do dokumentu źródłowego. Strumienie zachowują
    /// oryginalne (zakodowane) dane, np. JPEG wewnątrz PDF nie jest dekodowany ani kompresowany ponownie.
    /// </summary>
    private static PdfObject Odlacz(PdfObject obiekt, Dictionary<PdfObject, PdfObject> skopiowane)
    {
        if (obiekt is PdfIndirectReference odwolanie)
            obiekt = odwolanie.GetRefersTo() ?? PdfNull.PDF_NULL;
        if (skopiowane.TryGetValue(obiekt, out var gotowy))
            return gotowy;

        switch (obiekt)
        {
            case PdfStream strumien:
            {
                var kopia = new PdfStream(strumien.GetBytes(false));
                kopia.SetCompressionLevel(CompressionConstants.NO_COMPRESSION);
                skopiowane[obiekt] = kopia;
                foreach (var klucz in strumien.KeySet())
                {
                    if (!PdfName.Length.Equals(klucz))
                        kopia.Put(klucz, Odlacz(strumien.Get(klucz, false), skopiowane));
                }
                return kopia;
            }
            case PdfDictionary slownik:
            {
                var kopia = new PdfDictionary();
                skopiowane[obiekt] = kopia;
                foreach (var klucz in slownik.KeySet())
                    kopia.Put(klucz, Odlacz(slownik.Get(klucz, false), skopiowane));
                return kopia;
            }
            case PdfArray tablica:
            {
                var kopia = new PdfArray();
                skopiowane[obiekt] = kopia;
                for (var i = 0; i < tablica.Size(); i++)
                    kopia.Add(Odlacz(tablica.Get(i, false), skopiowane));
                return kopia;
            }
            default:
                return obiekt.Clone();
        }
    }

    /// <summary>Opakowuje grafikę w formularz przycięty do obszaru treści.</summary>
    private static (PdfXObject Obraz, float Proporcje) Przytnij(PdfXObject zrodlo, float w, float h, Obszar obszar, bool skalujDoRozmiaru)
    {
        if (obszar == Obszar.Calosc)
            return (zrodlo, w / h);

        // Formularz o rozmiarze widocznej części; grafika rysowana w pełnym rozmiarze z przesunięciem.
        var cw = obszar.Szerokosc * w;
        var ch = obszar.Wysokosc * h;
        var formularz = new PdfFormXObject(new Rectangle(0, 0, cw, ch));
        var nazwa = zrodlo is PdfImageXObject obraz
            ? formularz.GetResources().AddImage(obraz)
            : formularz.GetResources().AddForm((PdfFormXObject)zrodlo);
        var (sx, sy) = skalujDoRozmiaru ? (w, h) : (1f, 1f);
        var tresc = FormattableString.Invariant(
            $"q {sx} 0 0 {sy} {-obszar.Lewo * w} {-(1 - obszar.Dol) * h} cm {nazwa} Do Q");
        formularz.GetPdfObject().SetData(System.Text.Encoding.ASCII.GetBytes(tresc));
        return (formularz, cw / ch);
    }

    /// <summary>Obszar treści obrazu z obrazem logo (testy).</summary>
    internal static Obszar ZnajdzTresc(ImageData dane)
    {
        float w = dane.GetWidth(), h = dane.GetHeight();
        using var ms = new MemoryStream();
        using (var dokument = new PdfDocument(new PdfWriter(ms)))
        {
            var strona = dokument.AddNewPage(new PageSize(w, h));
            new PdfCanvas(strona).AddXObjectFittedIntoRectangle(new PdfImageXObject(dane), new Rectangle(0, 0, w, h));
        }
        return ZnajdzTresc(ms.ToArray());
    }

    /// <summary>
    /// Wyszukuje treść na pierwszej stronie PDF, renderując ją przez PDFium
    /// (PNG z przezroczystością, JPG, SVG i PDF tak samo).
    /// </summary>
    private static Obszar ZnajdzTresc(byte[] pdf)
    {
        ObrazStrony obraz;
        try
        {
            using (var dokument = new PdfDocument(new PdfReader(new MemoryStream(pdf))))
            {
                var c = dokument.GetPage(1).GetCropBox();
                obraz = DokumentPdf.Renderuj(pdf, 0, RozdzielczoscAnalizy / Math.Max(c.GetWidth(), c.GetHeight()));
            }
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
