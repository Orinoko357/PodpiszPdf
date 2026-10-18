using System.Runtime.InteropServices;
using System.Text;

namespace PodpiszPdf.Mac;

/// <summary>
/// Hasło do serwera znacznika czasu w pęku kluczy macOS (pozycja „PodpiszPdf — znacznik czasu”).
/// Poza macOS (zrzuty ekranu w testach) hasło nie jest zapisywane.
/// </summary>
internal static class PekKluczy
{
    private const string Usluga = "PodpiszPdf — znacznik czasu";
    private const int BrakPozycji = -25300; // errSecItemNotFound
    private const string Security = "/System/Library/Frameworks/Security.framework/Security";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    public static string? Odczytaj(string konto)
    {
        if (!OperatingSystem.IsMacOS())
            return null;
        var u = Encoding.UTF8.GetBytes(Usluga);
        var k = Encoding.UTF8.GetBytes(konto);
        var wynik = SecKeychainFindGenericPassword(IntPtr.Zero, (uint)u.Length, u, (uint)k.Length, k, out var dlugosc, out var dane, out var pozycja);
        if (wynik != 0)
            return null;
        try
        {
            var bajty = new byte[dlugosc];
            Marshal.Copy(dane, bajty, 0, (int)dlugosc);
            return Encoding.UTF8.GetString(bajty);
        }
        finally
        {
            SecKeychainItemFreeContent(IntPtr.Zero, dane);
            if (pozycja != IntPtr.Zero)
                CFRelease(pozycja);
        }
    }

    /// <summary>Zapisuje albo (hasło puste) usuwa hasło. Błąd pęku kluczy — wyjątek z opisem.</summary>
    public static void Zapisz(string konto, string? haslo)
    {
        if (!OperatingSystem.IsMacOS())
            return;
        var u = Encoding.UTF8.GetBytes(Usluga);
        var k = Encoding.UTF8.GetBytes(konto);
        var wynik = SecKeychainFindGenericPassword(IntPtr.Zero, (uint)u.Length, u, (uint)k.Length, k, out _, out var dane, out var pozycja);
        if (wynik == 0)
            SecKeychainItemFreeContent(IntPtr.Zero, dane);
        try
        {
            if (string.IsNullOrEmpty(haslo))
            {
                if (wynik == 0)
                    SprawdzWynik(SecKeychainItemDelete(pozycja));
                return;
            }
            var h = Encoding.UTF8.GetBytes(haslo);
            if (wynik == 0)
                SprawdzWynik(SecKeychainItemModifyAttributesAndData(pozycja, IntPtr.Zero, (uint)h.Length, h));
            else if (wynik == BrakPozycji)
                SprawdzWynik(SecKeychainAddGenericPassword(IntPtr.Zero, (uint)u.Length, u, (uint)k.Length, k, (uint)h.Length, h, IntPtr.Zero));
            else
                SprawdzWynik(wynik);
        }
        finally
        {
            if (wynik == 0 && pozycja != IntPtr.Zero)
                CFRelease(pozycja);
        }
    }

    private static void SprawdzWynik(int wynik)
    {
        if (wynik != 0)
            throw new IOException($"Nie udało się zapisać hasła w pęku kluczy (kod {wynik}).");
    }

    [DllImport(Security)]
    private static extern int SecKeychainFindGenericPassword(IntPtr pekKluczy, uint dlugoscUslugi, byte[] usluga, uint dlugoscKonta, byte[] konto,
        out uint dlugoscHasla, out IntPtr haslo, out IntPtr pozycja);

    [DllImport(Security)]
    private static extern int SecKeychainAddGenericPassword(IntPtr pekKluczy, uint dlugoscUslugi, byte[] usluga, uint dlugoscKonta, byte[] konto,
        uint dlugoscHasla, byte[] haslo, IntPtr pozycja);

    [DllImport(Security)]
    private static extern int SecKeychainItemModifyAttributesAndData(IntPtr pozycja, IntPtr atrybuty, uint dlugosc, byte[] dane);

    [DllImport(Security)]
    private static extern int SecKeychainItemDelete(IntPtr pozycja);

    [DllImport(Security)]
    private static extern int SecKeychainItemFreeContent(IntPtr atrybuty, IntPtr dane);

    [DllImport(CoreFoundation)]
    private static extern void CFRelease(IntPtr obiekt);
}
