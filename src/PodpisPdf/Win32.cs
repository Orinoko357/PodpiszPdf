using System.Runtime.InteropServices;

namespace PodpisPdf;

/// <summary>Funkcje systemowe Windows potrzebne poza oknami WinUI (komunikaty przed startem interfejsu, okno na wierzch).</summary>
internal static class Win32
{
    public const uint Informacja = 0x40, Ostrzezenie = 0x30, Blad = 0x10;
    public const uint TakNie = 0x4;
    public const int Tak = 6;

    /// <summary>Systemowe okno komunikatu — działa także zanim powstanie interfejs WinUI.</summary>
    public static int Komunikat(string tekst, uint rodzaj, uint przyciski = 0, IntPtr okno = default) =>
        MessageBoxW(okno, tekst, "PodpisPdf", rodzaj | przyciski | 0x00010000 /* MB_SETFOREGROUND */);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("shell32.dll")]
    public static extern void SHChangeNotify(int wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);
}
