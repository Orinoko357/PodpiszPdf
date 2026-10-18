using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using PodpiszPdf.Core;

namespace PodpiszPdf.Mac;

internal static class Obrazy
{
    /// <summary>Obraz z PDFium (BGRA bez premultiplikacji) jako bitmapa Avalonii.</summary>
    public static WriteableBitmap NaObraz(ObrazStrony obraz)
    {
        var bitmapa = new WriteableBitmap(new PixelSize(obraz.Szerokosc, obraz.Wysokosc), new Vector(96, 96),
            PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        using var bufor = bitmapa.Lock();
        var wiersz = obraz.Szerokosc * 4;
        for (var y = 0; y < obraz.Wysokosc; y++)
            System.Runtime.InteropServices.Marshal.Copy(obraz.Bgra, y * wiersz, bufor.Address + y * bufor.RowBytes, wiersz);
        return bitmapa;
    }

    /// <summary>„1 strona”, „3 strony”, „5 stron”.</summary>
    public static string LiczbaStron(int n)
    {
        if (n == 1) return "1 strona";
        var j = n % 10; var d = n % 100;
        return j is >= 2 and <= 4 && d is < 12 or > 14 ? $"{n} strony" : $"{n} stron";
    }

    public static string Rozmiar(long bajty) =>
        bajty < 1024 * 1024 ? $"{Math.Max(1, bajty / 1024)} KB" : $"{bajty / 1024d / 1024d:0.0} MB";
}
