using iText.IO.Image;
using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas;
using iText.Kernel.Pdf.Xobject;
using iText.StyledXmlParser.Resolver.Resource;
using iText.Svg.Converter;
using iText.Svg.Processors.Impl;

namespace PodpiszPdf.Core;

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
    /// <param name="bezPrzezroczystosci">
    /// Dla dokumentów PDF/A-1, które nie dopuszczają przezroczystości: logo rastrowe trafia na białe tło,
    /// a logo wektorowe z elementami przezroczystymi jest odrzucane.
    /// </param>
    public static (PdfXObject Obraz, float Proporcje) Wczytaj(string sciezka, bool bezPrzezroczystosci = false)
    {
        var dane = File.ReadAllBytes(sciezka);
        var nazwa = bezPrzezroczystosci ? System.IO.Path.GetFileName(sciezka) : null;
        switch (System.IO.Path.GetExtension(sciezka).ToLowerInvariant())
        {
            case ".svg":
                return ZWektora(SvgDoPdf(dane), nazwa);
            case ".pdf":
                return ZWektora(dane, nazwa);
            default:
                var obraz = ImageDataFactory.Create(dane);
                if (obraz.GetWidth() <= 0 || obraz.GetHeight() <= 0)
                    throw new InvalidDataException("obraz o zerowym rozmiarze");
                return Przygotuj(obraz, bezPrzezroczystosci);
        }
    }

    /// <summary>Logo rastrowe.</summary>
    public static (PdfXObject Obraz, float Proporcje) Przygotuj(ImageData dane, bool bezPrzezroczystosci = false)
    {
        // Bez /Interpolate: PDF/A go zabrania.
        var obraz = new PdfImageXObject(dane);
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

        if (bezPrzezroczystosci && (obraz.GetPdfObject().ContainsKey(PdfName.SMask) || obraz.GetPdfObject().ContainsKey(PdfName.Mask)))
            obraz = NaBialymTle(pdf, (int)w, (int)h);

        // Obraz rysowany jest w kwadracie jednostkowym, więc macierz musi go rozciągnąć do w × h.
        return Przytnij(obraz, w, h, ZnajdzTresc(pdf), skalujDoRozmiaru: true);
    }

    /// <summary>
    /// Obraz z przezroczystością nałożony na białe tło, w tej samej rozdzielczości (strona ma w × h punktów,
    /// więc w skali 1 piksel obrazu to piksel wyniku).
    /// </summary>
    private static PdfImageXObject NaBialymTle(byte[] pdf, int w, int h)
    {
        var obraz = DokumentPdf.Renderuj(pdf, 0, 1);
        if (obraz.Szerokosc != w || obraz.Wysokosc != h)
            throw new InvalidDataException("nieoczekiwany rozmiar obrazu na białym tle");
        var rgb = new byte[w * h * 3];
        for (int i = 0, j = 0; i < obraz.Bgra.Length; i += 4, j += 3)
        {
            rgb[j] = obraz.Bgra[i + 2];
            rgb[j + 1] = obraz.Bgra[i + 1];
            rgb[j + 2] = obraz.Bgra[i];
        }
        return new PdfImageXObject(ImageDataFactory.Create(w, h, 3, 8, rgb, null));
    }

    /// <summary>Logo wektorowe z pierwszej strony dokumentu PDF.</summary>
    /// <param name="plikBezPrzezroczystosci">Nazwa pliku logo, gdy dokument nie dopuszcza przezroczystości (PDF/A-1).</param>
    private static (PdfXObject Obraz, float Proporcje) ZWektora(byte[] pdf, string? plikBezPrzezroczystosci = null)
    {
        using var dokument = new PdfDocument(new PdfReader(new MemoryStream(pdf)));
        if (dokument.GetNumberOfPages() < 1)
            throw new InvalidDataException("plik logo nie ma żadnej strony");
        var strona = dokument.GetPage(1);
        var c = DokumentPdf.ObszarStrony(strona);

        // Strona obrócona (/Rotate): treść obracamy macierzą tak, jak pokazuje ją przeglądarka (i PDFium
        // przy wyszukiwaniu treści), a formularz ma wymiary strony po obrocie.
        float l = c.GetLeft(), b = c.GetBottom(), r = c.GetRight(), t = c.GetTop();
        var (macierz, w, h) = (((strona.GetRotation() % 360) + 360) % 360) switch
        {
            90 => (new[] { 0, -1, 1, 0, -b, r }, c.GetHeight(), c.GetWidth()),
            180 => (new[] { -1, 0, 0, -1, r, t }, c.GetWidth(), c.GetHeight()),
            270 => (new[] { 0, 1, -1, 0, t, -l }, c.GetHeight(), c.GetWidth()),
            _ => (new[] { 1, 0, 0, 1, -l, -b }, c.GetWidth(), c.GetHeight()),
        };

        // Formularz (XObject) niezależny od dokumentu źródłowego: treść strony i kopia jej zasobów
        // (także dziedziczonych z węzła /Pages). Dzięki temu można go użyć w dowolnym dokumencie,
        // który dopiero powstanie przy podpisie.
        var formularz = new PdfFormXObject(new Rectangle(0, 0, w, h));
        using (var tresc = new MemoryStream())
        {
            tresc.Write(System.Text.Encoding.ASCII.GetBytes(FormattableString.Invariant(
                $"q {macierz[0]} {macierz[1]} {macierz[2]} {macierz[3]} {macierz[4]} {macierz[5]} cm\n")));
            tresc.Write(strona.GetContentBytes());
            tresc.Write("\nQ"u8);
            formularz.GetPdfObject().SetData(tresc.ToArray());
        }
        var zasoby = strona.GetResources().GetPdfObject();
        if (plikBezPrzezroczystosci != null && MaPrzezroczystosc(zasoby, new HashSet<PdfObject>(ReferenceEqualityComparer.Instance)))
        {
            throw new BladPodpisu(
                $"Dokument jest w formacie PDF/A-1, który nie dopuszcza przezroczystości, a logo ({plikBezPrzezroczystosci}) " +
                "zawiera elementy przezroczyste. Użyj logo bez przezroczystości (np. PNG lub JPG — zostanie nałożone na białe tło) " +
                "albo podpisz dokument bez logo.");
        }
        if (zasoby.Size() > 0)
            formularz.GetPdfObject().Put(PdfName.Resources, Odlacz(zasoby, new Dictionary<PdfObject, PdfObject>(ReferenceEqualityComparer.Instance)));

        return Przytnij(formularz, w, h, ZnajdzTresc(pdf), skalujDoRozmiaru: false);
    }

    /// <summary>
    /// Czy zasoby (rekurencyjnie: formularze, wzorce, czcionki Type 3) zawierają przezroczystość niedozwoloną
    /// w PDF/A-1: krycie poniżej 1, maskę miękką, tryb mieszania inny niż Normal lub grupę przezroczystości.
    /// </summary>
    private static bool MaPrzezroczystosc(PdfDictionary? zasoby, HashSet<PdfObject> odwiedzone)
    {
        if (zasoby == null || !odwiedzone.Add(zasoby))
            return false;

        if (Wartosci(zasoby.GetAsDictionary(PdfName.ExtGState)).Any(StanPrzezroczysty))
            return true;

        foreach (var obiekt in Wartosci(zasoby.GetAsDictionary(PdfName.XObject)))
        {
            if (obiekt.ContainsKey(PdfName.SMask) || obiekt.GetAsNumber(PdfName.SMaskInData) is { } sm && sm.IntValue() != 0)
                return true;
            if (obiekt.GetAsDictionary(PdfName.Group) is { } grupa && PdfName.Transparency.Equals(grupa.GetAsName(PdfName.S)))
                return true;
            if (MaPrzezroczystosc(obiekt.GetAsDictionary(PdfName.Resources), odwiedzone))
                return true;
        }

        foreach (var wzorzec in Wartosci(zasoby.GetAsDictionary(PdfName.Pattern)))
        {
            if (wzorzec.GetAsDictionary(PdfName.ExtGState) is { } stan && StanPrzezroczysty(stan))
                return true;
            if (MaPrzezroczystosc(wzorzec.GetAsDictionary(PdfName.Resources), odwiedzone))
                return true;
        }

        return Wartosci(zasoby.GetAsDictionary(PdfName.Font))
            .Any(czcionka => MaPrzezroczystosc(czcionka.GetAsDictionary(PdfName.Resources), odwiedzone));
    }

    private static bool StanPrzezroczysty(PdfDictionary stan)
    {
        if (stan.GetAsNumber(PdfName.CA) is { } ca && ca.FloatValue() < 1 || stan.GetAsNumber(PdfName.ca) is { } cf && cf.FloatValue() < 1)
            return true;
        var maska = stan.Get(PdfName.SMask);
        if (maska != null && !PdfName.None.Equals(maska))
            return true;
        var tryb = stan.Get(PdfName.BM);
        if (tryb is PdfArray tablica && tablica.Size() > 0)
            tryb = tablica.Get(0);
        return tryb != null && !PdfName.Normal.Equals(tryb) && !PdfName.Compatible.Equals(tryb);
    }

    /// <summary>Wartości słownika zasobów będące słownikami lub strumieniami (GetAsDictionary pomija strumienie).</summary>
    private static IEnumerable<PdfDictionary> Wartosci(PdfDictionary? slownik) =>
        slownik == null
            ? Enumerable.Empty<PdfDictionary>()
            : slownik.KeySet().Select(k => slownik.Get(k)).OfType<PdfDictionary>();

    private static byte[] SvgDoPdf(byte[] svg)
    {
        using var wynik = new MemoryStream();
        // Bez pobierania zasobów z sieci i plików: adres \\serwer\udział wysłałby skrót hasła Windows (NTLM).
        var wlasciwosci = new SvgConverterProperties().SetResourceRetriever(new TylkoZasobyOsadzone());
        SvgConverter.CreatePdf(new MemoryStream(svg), wynik, wlasciwosci);
        return wynik.ToArray();
    }

    /// <summary>
    /// Odmawia pobrania każdego zasobu zewnętrznego (http, https, file, UNC). Obrazy osadzone
    /// (<c>data:</c>) iText dekoduje sam, bez udziału tej klasy.
    /// </summary>
#pragma warning disable CS0618 // SvgConverterProperties w iText 9.3 przyjmuje tylko ten (przestarzały) interfejs.
    private sealed class TylkoZasobyOsadzone : IResourceRetriever
#pragma warning restore CS0618
    {
        public Stream? GetInputStreamByUrl(Uri url) => null;
        public byte[]? GetByteArrayByUrl(Uri url) => null;
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
                var c = DokumentPdf.ObszarStrony(dokument.GetPage(1));
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
