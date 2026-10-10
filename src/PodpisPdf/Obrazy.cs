using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml.Media.Imaging;
using PodpisPdf.Core;

namespace PodpisPdf;

internal static class Obrazy
{
    /// <summary>
    /// Obraz z PDFium (BGRA bez premultiplikacji) jako bitmapa XAML. Przezroczystość wymaga przeliczenia
    /// kolorów na postać premultiplikowaną, której oczekuje <see cref="WriteableBitmap"/>.
    /// </summary>
    public static WriteableBitmap NaObraz(ObrazStrony obraz)
    {
        var dane = obraz.Bgra;
        var premultiplikowane = new byte[dane.Length];
        for (var i = 0; i < dane.Length; i += 4)
        {
            var a = dane[i + 3];
            if (a == 255)
            {
                premultiplikowane[i] = dane[i];
                premultiplikowane[i + 1] = dane[i + 1];
                premultiplikowane[i + 2] = dane[i + 2];
            }
            else if (a > 0)
            {
                premultiplikowane[i] = (byte)(dane[i] * a / 255);
                premultiplikowane[i + 1] = (byte)(dane[i + 1] * a / 255);
                premultiplikowane[i + 2] = (byte)(dane[i + 2] * a / 255);
            }
            premultiplikowane[i + 3] = a;
        }

        var bitmapa = new WriteableBitmap(obraz.Szerokosc, obraz.Wysokosc);
        using (var strumien = bitmapa.PixelBuffer.AsStream())
            strumien.Write(premultiplikowane, 0, premultiplikowane.Length);
        bitmapa.Invalidate();
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
