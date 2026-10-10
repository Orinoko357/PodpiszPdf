using Docnet.Core;
using Docnet.Core.Models;
using iText.Kernel.Pdf;

namespace PodpisPdf.Core;

/// <summary>Wymiary strony: CropBox w punktach (układ współrzędnych PDF) i obrót.</summary>
public sealed record StronaPdf(int Numer, float Lewo, float Dol, float Szerokosc, float Wysokosc, int Obrot)
{
    public float Prawo => Lewo + Szerokosc;
    public float Gora => Dol + Wysokosc;
    public iText.Kernel.Geom.Rectangle Prostokat => new(Lewo, Dol, Szerokosc, Wysokosc);
}

/// <summary>Obraz strony w formacie BGRA (4 bajty na piksel), wiersz po wierszu od góry.</summary>
public sealed record ObrazStrony(byte[] Bgra, int Szerokosc, int Wysokosc);

/// <summary>Odczyt stron i renderowanie podglądu dokumentu PDF (silnik PDFium).</summary>
public static class DokumentPdf
{
    // PDFium nie jest bezpieczny wątkowo.
    private static readonly object Blokada = new();

    public static IReadOnlyList<StronaPdf> Strony(string sciezka)
    {
        using var dokument = new PdfDocument(new PdfReader(sciezka));
        var wynik = new List<StronaPdf>();
        for (var i = 1; i <= dokument.GetNumberOfPages(); i++)
        {
            var strona = dokument.GetPage(i);
            var c = strona.GetCropBox();
            wynik.Add(new StronaPdf(i, c.GetLeft(), c.GetBottom(), c.GetWidth(), c.GetHeight(), ((strona.GetRotation() % 360) + 360) % 360));
        }
        return wynik;
    }

    /// <summary>
    /// Renderuje stronę (indeks od 0) w skali: 1 = 1 piksel na punkt PDF. Domyślnie na białym tle;
    /// bez niego puste miejsca są przezroczyste (np. do nałożenia wizualizacji na podgląd strony).
    /// </summary>
    public static ObrazStrony Renderuj(byte[] pdf, int indeks, double skala, bool bialeTlo = true)
    {
        lock (Blokada)
        {
            using var czytnik = DocLib.Instance.GetDocReader(pdf, new PageDimensions(skala));
            using var strona = czytnik.GetPageReader(indeks);
            var w = strona.GetPageWidth();
            var h = strona.GetPageHeight();
            var bgra = strona.GetImage(RenderFlags.RenderAnnotations);
            if (bialeTlo)
                NaBialymTle(bgra);
            return new ObrazStrony(bgra, w, h);
        }
    }

    /// <summary>PDFium zwraca przezroczyste tło — nakładamy obraz na białą kartkę.</summary>
    private static void NaBialymTle(byte[] bgra)
    {
        for (var i = 0; i < bgra.Length; i += 4)
        {
            var a = bgra[i + 3];
            if (a == 255)
                continue;
            var tlo = 255 - a;
            bgra[i] = (byte)(bgra[i] * a / 255 + tlo);
            bgra[i + 1] = (byte)(bgra[i + 1] * a / 255 + tlo);
            bgra[i + 2] = (byte)(bgra[i + 2] * a / 255 + tlo);
            bgra[i + 3] = 255;
        }
    }
}
