using System.Runtime.InteropServices;

namespace PodpiszPdf;

/// <summary>Funkcje systemowe Windows potrzebne poza oknami WinUI (komunikaty przed startem interfejsu, okno na wierzch).</summary>
internal static class Win32
{
    public const uint Informacja = 0x40, Ostrzezenie = 0x30, Blad = 0x10;
    public const uint TakNie = 0x4;
    public const int Tak = 6;

    /// <summary>Systemowe okno komunikatu — działa także zanim powstanie interfejs WinUI.</summary>
    public static int Komunikat(string tekst, uint rodzaj, uint przyciski = 0, IntPtr okno = default) =>
        MessageBoxW(okno, tekst, "PodpiszPdf", rodzaj | przyciski | 0x00010000 /* MB_SETFOREGROUND */);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("shell32.dll")]
    public static extern void SHChangeNotify(int wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(ref int dlugosc, [Out] char[]? nazwa);

    /// <summary>Pełna nazwa pakietu, gdy program działa z tożsamością pakietu (menu Windows 11); inaczej null.</summary>
    public static string? NazwaPakietu()
    {
        var dlugosc = 0;
        if (GetCurrentPackageFullName(ref dlugosc, null) != 122 /* ERROR_INSUFFICIENT_BUFFER */)
            return null;
        var bufor = new char[dlugosc];
        return GetCurrentPackageFullName(ref dlugosc, bufor) == 0 ? new string(bufor, 0, dlugosc - 1) : null;
    }

    // ---- Okno wyboru po otwarciu pliku PDF (TaskDialog z przyciskami-poleceniami) ----

    public enum WyborOtwarcia { Anuluj, Podpisz, Przegladaj }

    private const int IdPodpisz = 101, IdPrzegladaj = 102;

    [StructLayout(LayoutKind.Sequential, Pack = 1, CharSet = CharSet.Unicode)]
    private struct PrzyciskTaskDialog
    {
        public int Id;
        public IntPtr Tekst;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1, CharSet = CharSet.Unicode)]
    private struct KonfiguracjaTaskDialog
    {
        public uint cbSize;
        public IntPtr hwndParent;
        public IntPtr hInstance;
        public uint dwFlags;
        public uint dwCommonButtons;
        public IntPtr pszWindowTitle;
        public IntPtr hMainIcon;
        public IntPtr pszMainInstruction;
        public IntPtr pszContent;
        public uint cButtons;
        public IntPtr pButtons;
        public int nDefaultButton;
        public uint cRadioButtons;
        public IntPtr pRadioButtons;
        public int nDefaultRadioButton;
        public IntPtr pszVerificationText;
        public IntPtr pszExpandedInformation;
        public IntPtr pszExpandedControlText;
        public IntPtr pszCollapsedControlText;
        public IntPtr hFooterIcon;
        public IntPtr pszFooter;
        public IntPtr pfCallback;
        public IntPtr lpCallbackData;
        public uint cxWidth;
    }

    [DllImport("comctl32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int TaskDialogIndirect(ref KonfiguracjaTaskDialog konfiguracja, out int przycisk, out int radio,
        [MarshalAs(UnmanagedType.Bool)] out bool zaznaczono);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr ExtractIconW(IntPtr hInst, string plik, int indeks);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr ikona);

    /// <summary>
    /// Pyta, co zrobić z otwieranym plikiem PDF: podpisać czy przejrzeć. Zwraca wybór i to, czy zapamiętać go
    /// na stałe. Gdy TaskDialog jest niedostępny — zwykłe okno komunikatu (Tak/Nie/Anuluj).
    /// </summary>
    public static (WyborOtwarcia Wybor, bool Zapamietaj) ZapytajOOtwarcie(string plik, string przegladarka)
    {
        var teksty = new List<IntPtr>();
        IntPtr T(string s)
        {
            var p = Marshal.StringToHGlobalUni(s);
            teksty.Add(p);
            return p;
        }

        var ikona = IntPtr.Zero;
        var przyciski = IntPtr.Zero;
        try
        {
            var definicje = new[]
            {
                new PrzyciskTaskDialog { Id = IdPodpisz, Tekst = T("Podpisz\nZłóż kwalifikowany podpis elektroniczny i dodaj jego wizualizację") },
                new PrzyciskTaskDialog { Id = IdPrzegladaj, Tekst = T($"Przeglądaj\nOtwórz dokument w {przegladarka}") },
            };
            var rozmiar = Marshal.SizeOf<PrzyciskTaskDialog>();
            przyciski = Marshal.AllocHGlobal(rozmiar * definicje.Length);
            for (var i = 0; i < definicje.Length; i++)
                Marshal.StructureToPtr(definicje[i], przyciski + i * rozmiar, false);

            if (Environment.ProcessPath is { } exe)
                ikona = ExtractIconW(IntPtr.Zero, exe, 0);

            const uint UzyjIkony = 0x2, PrzyciskiPolecen = 0x10, MoznaAnulowac = 0x8, Wysrodkuj = 0x1000000 /* TDF_SIZE_TO_CONTENT */;
            var konfiguracja = new KonfiguracjaTaskDialog
            {
                cbSize = (uint)Marshal.SizeOf<KonfiguracjaTaskDialog>(),
                dwFlags = PrzyciskiPolecen | MoznaAnulowac | (ikona != IntPtr.Zero ? UzyjIkony : 0) | Wysrodkuj,
                dwCommonButtons = 0x8, // Anuluj
                pszWindowTitle = T("PodpiszPdf"),
                hMainIcon = ikona,
                pszMainInstruction = T(Path.GetFileName(plik)),
                pszContent = T("Co chcesz zrobić z tym dokumentem?"),
                cButtons = (uint)definicje.Length,
                pButtons = przyciski,
                nDefaultButton = IdPodpisz,
                pszVerificationText = T("Zapamiętaj mój wybór (można go zmienić w ustawieniach PodpiszPdf)"),
            };
            if (TaskDialogIndirect(ref konfiguracja, out var wybrany, out _, out var zapamietaj) != 0)
                throw new InvalidOperationException("TaskDialog");
            return (wybrany switch
            {
                IdPodpisz => WyborOtwarcia.Podpisz,
                IdPrzegladaj => WyborOtwarcia.Przegladaj,
                _ => WyborOtwarcia.Anuluj,
            }, zapamietaj && wybrany is IdPodpisz or IdPrzegladaj);
        }
        catch (Exception e) when (e is EntryPointNotFoundException or DllNotFoundException or InvalidOperationException)
        {
            var odpowiedz = Komunikat($"{Path.GetFileName(plik)}\n\nPodpisać dokument?\n\nTak — podpisz\nNie — otwórz w {przegladarka}",
                Informacja, 0x3 /* MB_YESNOCANCEL */);
            return (odpowiedz switch { Tak => WyborOtwarcia.Podpisz, 7 => WyborOtwarcia.Przegladaj, _ => WyborOtwarcia.Anuluj }, false);
        }
        finally
        {
            foreach (var t in teksty)
                Marshal.FreeHGlobal(t);
            if (przyciski != IntPtr.Zero)
                Marshal.FreeHGlobal(przyciski);
            if (ikona != IntPtr.Zero)
                DestroyIcon(ikona);
        }
    }
}
