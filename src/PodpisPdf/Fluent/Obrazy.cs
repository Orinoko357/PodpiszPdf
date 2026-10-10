using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using PodpisPdf.Core;

namespace PodpisPdf.Fluent;

internal static class Obrazy
{
    public static Bitmap NaBitmape(ObrazStrony obraz)
    {
        var b = new Bitmap(obraz.Szerokosc, obraz.Wysokosc, PixelFormat.Format32bppArgb);
        var dane = b.LockBits(new Rectangle(0, 0, b.Width, b.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            var wiersz = obraz.Szerokosc * 4;
            for (var y = 0; y < obraz.Wysokosc; y++)
                Marshal.Copy(obraz.Bgra, y * wiersz, dane.Scan0 + y * dane.Stride, wiersz);
        }
        finally
        {
            b.UnlockBits(dane);
        }
        return b;
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
