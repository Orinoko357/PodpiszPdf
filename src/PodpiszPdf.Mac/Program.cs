using Avalonia;
using PodpiszPdf.Core;

namespace PodpiszPdf.Mac;

internal static class Program
{
    /// <summary>--sprawdz: próbny podpis i start okna (GitHub Actions), potem koniec programu.</summary>
    public static bool TrybSprawdzenia { get; private set; }

    /// <summary>Plik PDF podany w wierszu poleceń.</summary>
    public static string? PlikZArgumentow { get; private set; }

    public static string Wersja =>
        typeof(Program).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}.{v.Revision}" : "?";

    private static string PlikBledu => Path.Combine(Path.GetTempPath(), "PodpiszPdf-blad.txt");

    [STAThread]
    public static int Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Zapisz(e.ExceptionObject as Exception);
        try
        {
            var indeksZrzutow = Array.IndexOf(args, "--zrzuty");
            if (indeksZrzutow >= 0 && indeksZrzutow + 1 < args.Length)
            {
                Zrzuty.Wykonaj(args[indeksZrzutow + 1]);
                return 0;
            }

            if (args.Contains("--sprawdz"))
            {
                TrybSprawdzenia = true;
                Samotest.PodpisProbny();
                var tekst = $"PodpiszPdf {Wersja} (macOS): próbny podpis PAdES i podgląd PDFium działają.";
                File.WriteAllText(Path.Combine(Path.GetTempPath(), "PodpiszPdf-sprawdz.txt"), tekst);
                Console.WriteLine(tekst);
            }
            // macOS starszych wersji dodaje -psn_…; pozostałe argumenty to pliki PDF.
            PlikZArgumentow = args.FirstOrDefault(a => !a.StartsWith('-') && File.Exists(a));

            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception e)
        {
            Zapisz(e);
            Console.Error.WriteLine(e);
            return 1;
        }
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        var budowa = AppBuilder.Configure<App>().UsePlatformDetect();
        // macOS ma własną czcionkę systemową; gdzie indziej (testy) — wbudowany Inter.
        return OperatingSystem.IsMacOS() ? budowa : budowa.WithInterFont();
    }

    public static void Zapisz(Exception? e)
    {
        try
        {
            File.WriteAllText(PlikBledu, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} PodpiszPdf {Wersja}\n{e}");
        }
        catch (Exception)
        {
            // Brak zapisu nie może przerwać programu.
        }
    }
}
