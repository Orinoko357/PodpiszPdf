using Microsoft.UI.Xaml;
using PodpiszPdf.Core;

namespace PodpiszPdf;

public partial class App : Application
{
    /// <summary>Bieżący etap startu (do komunikatu o błędzie i pliku diagnostycznego).</summary>
    internal static string Etap { get; set; } = "zasoby aplikacji";

    // Brakujące zasoby XAML zgłaszane przez WinUI (zwykle przyczyna „XAML parsing failed”).
    private static readonly List<string> BledyZasobow = new();

    public App()
    {
        DebugSettings.IsXamlResourceReferenceTracingEnabled = true;
        DebugSettings.XamlResourceReferenceFailed += (_, e) => { lock (BledyZasobow) BledyZasobow.Add(e.Message); };
        InitializeComponent();
        UnhandledException += (_, e) =>
        {
            Zapisz(e.Exception);
            if (Program.Tryb == TrybUruchomienia.Normalny)
            {
                e.Handled = true;
                Win32.Komunikat("Wystąpił nieoczekiwany błąd programu:\n\n" + KomunikatyBledow.Opisz(e.Exception), Win32.Blad);
            }
            else
            {
                Environment.ExitCode = 1;
            }
        };
    }

    internal static OknoGlowne? Okno { get; set; }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            switch (Program.Tryb)
            {
                case TrybUruchomienia.Sprawdzenie:
                    // Test uruchomienia (GitHub Actions): okno, strony i biblioteki PDF; kod wyjścia 0 = działa.
                    Okno = new OknoGlowne(startWZasobniku: false, trybTestowy: true);
                    Okno.Activate();
                    await Task.Delay(1500);
                    Rozgrzewka.Wykonaj(zglaszajBledy: true);
                    Samotest.PodpisProbny(); // pełny podpis w gotowym exe (certyfikat i znacznik czasu w pamięci)
                    File.WriteAllText(Path.Combine(Path.GetTempPath(), "PodpiszPdf-sprawdz.txt"),
                        $"PodpiszPdf {Instalacja.BiezacaWersja}, tożsamość pakietu: {Win32.NazwaPakietu() ?? "brak"}, " +
                        $"pakiet menu w programie: {(MenuWindows11.MaPakiet ? "tak" : "nie")}");
                    Okno.ZamknijProgram();
                    return;

                case TrybUruchomienia.Zrzuty:
                    Okno = new OknoGlowne(startWZasobniku: false, trybTestowy: true);
                    Okno.Activate();
                    await Zrzuty.WykonajAsync(Okno, Program.FolderZrzutow!);
                    Okno.ZamknijProgram();
                    return;

                default:
                    // Uruchomienie przy logowaniu do Windows: program czeka w zasobniku, okno nie jest pokazywane.
                    var wZasobniku = Program.Autostart && string.IsNullOrEmpty(Program.PlikStartowy);
                    Okno = new OknoGlowne(wZasobniku, trybTestowy: false);
                    Program.Instancja?.Nasluchuj(Okno.PrzyjmijPolecenie);
                    if (!wZasobniku)
                        Okno.Activate();
                    Okno.Uruchom(Program.PlikStartowy);
                    return;
            }
        }
        catch (Exception e)
        {
            Zapisz(e);
            Environment.ExitCode = 1;
            if (Program.Tryb == TrybUruchomienia.Normalny)
                Win32.Komunikat($"Nie udało się uruchomić programu (etap: {Etap}):\n\n" + KomunikatyBledow.Opisz(e) +
                    $"\n\n{e.GetType().Name} 0x{e.HResult:X8}" + OpisBledowZasobow() +
                    "\n\nSzczegóły: %TEMP%\\PodpiszPdf-blad.txt", Win32.Blad);
            Exit();
        }
    }

    private static string OpisBledowZasobow()
    {
        lock (BledyZasobow)
            return BledyZasobow.Count == 0 ? "" : "\n\nBrakujące zasoby XAML:\n" + string.Join("\n", BledyZasobow.Distinct().Take(5));
    }

    /// <summary>Zapis błędu do pliku (diagnostyka, także w GitHub Actions).</summary>
    internal static void Zapisz(Exception e)
    {
        try
        {
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "PodpiszPdf-blad.txt"),
                $"{DateTime.Now:O}\nPodpiszPdf {Instalacja.BiezacaWersja}, etap: {Etap}, Windows {Environment.OSVersion.Version}\n{e}{OpisBledowZasobow()}");
        }
        catch (Exception)
        {
            // Brak zapisu nie może przerwać obsługi błędu.
        }
    }
}
