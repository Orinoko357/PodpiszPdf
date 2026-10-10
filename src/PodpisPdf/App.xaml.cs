using Microsoft.UI.Xaml;
using PodpisPdf.Core;

namespace PodpisPdf;

public partial class App : Application
{
    public App()
    {
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
                Win32.Komunikat("Nie udało się uruchomić programu:\n\n" + KomunikatyBledow.Opisz(e), Win32.Blad);
            Exit();
        }
    }

    /// <summary>Zapis błędu do pliku (diagnostyka, także w GitHub Actions).</summary>
    internal static void Zapisz(Exception e)
    {
        try
        {
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "PodpisPdf-blad.txt"), $"{DateTime.Now:O}\n{e}");
        }
        catch (Exception)
        {
            // Brak zapisu nie może przerwać obsługi błędu.
        }
    }
}
