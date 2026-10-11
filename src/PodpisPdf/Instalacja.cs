using System.Diagnostics;
using Microsoft.Win32;

namespace PodpisPdf;

/// <summary>
/// Program instalowany instalatorem MSI w C:\Program Files\PodpisPdf (dla całego komputera). Autostart
/// to wpis w rejestrze bieżącego użytkownika. Wersje 1.x–3.0 kopiowały się do %APPDATA%\PodpisPdf —
/// taka kopia jest przy starcie usuwana, a wpisy w rejestrze przestawiane na zainstalowany program.
/// </summary>
internal static class Instalacja
{
    public const string ParametrAutostartu = "--autostart";
    private const string KluczAutostartu = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string NazwaWpisu = "PodpisPdf";

    public static string BiezacyPlik => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "PodpisPdf.exe");

    /// <summary>Folder programu (zewnętrzna lokalizacja pakietu menu Windows 11).</summary>
    public static string FolderProgramu => Path.GetDirectoryName(BiezacyPlik) ?? AppContext.BaseDirectory;

    public static Version BiezacaWersja => typeof(Instalacja).Assembly.GetName().Version ?? new Version(0, 0);

    /// <summary>Plik, który mają uruchamiać autostart i menu kontekstowe.</summary>
    public static string SciezkaProgramu => BiezacyPlik;

    /// <summary>Kopia programu z wersji 1.x–3.0 (przed instalatorem MSI).</summary>
    private static string StaraKopia => Path.Combine(Ustawienia.Folder, "PodpisPdf.exe");

    /// <summary>
    /// Usuwa kopię programu ze starszych wersji (%APPDATA%\PodpisPdf\PodpisPdf.exe i pliki pomocnicze).
    /// Wywoływane w tle przy starcie; zwraca komunikat do paska stanu albo null.
    /// </summary>
    public static string? PosprzatajStaraKopie()
    {
        if (string.Equals(Path.GetFullPath(BiezacyPlik), StaraKopia, StringComparison.OrdinalIgnoreCase))
            return null; // uruchomiono starą kopię — nie usuwamy samego siebie
        var usunieto = false;
        foreach (var plik in new[] { StaraKopia, StaraKopia + ".stary", StaraKopia + ".pobrany", StaraKopia + ".nowy" })
        {
            try
            {
                if (File.Exists(plik))
                {
                    File.Delete(plik);
                    usunieto |= plik == StaraKopia;
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Stara wersja może jeszcze działać — usuniemy ją przy następnym starcie.
            }
        }
        return usunieto ? "Usunięto starą kopię programu z folderu AppData — używany jest zainstalowany PodpisPdf." : null;
    }

    public static bool AutostartWlaczony()
    {
        using var klucz = Registry.CurrentUser.OpenSubKey(KluczAutostartu);
        return klucz?.GetValue(NazwaWpisu) is string;
    }

    /// <summary>Włącza autostart zainstalowanego programu albo go wyłącza.</summary>
    public static void UstawAutostart(bool wlacz)
    {
        using var klucz = Registry.CurrentUser.CreateSubKey(KluczAutostartu);
        if (wlacz)
        {
            var polecenie = $"\"{BiezacyPlik}\" {ParametrAutostartu}";
            if (!string.Equals(klucz.GetValue(NazwaWpisu) as string, polecenie, StringComparison.OrdinalIgnoreCase))
                klucz.SetValue(NazwaWpisu, polecenie);
        }
        else
        {
            klucz.DeleteValue(NazwaWpisu, throwOnMissingValue: false);
        }
    }

    /// <summary>
    /// Tryb <c>--odinstaluj</c> (akcja instalatora MSI przy odinstalowaniu, jako bieżący użytkownik): usuwa
    /// pozycje w menu (Windows 11 i klasyczne), rejestrację aplikacji do PDF i autostart. Ustawienia
    /// w %APPDATA%\PodpisPdf zostają (przy ponownej instalacji program je odczyta). Błędy są pomijane —
    /// odinstalowanie nie może się przez nie zatrzymać. Zwraca kod wyjścia (zawsze 0).
    /// </summary>
    public static int PosprzatajPoOdinstalowaniu()
    {
        // Działająca kopia programu (jeśli niezajęta) zamyka się, żeby nie przywróciła wpisów.
        try { JednaInstancja.Wyslij(JednaInstancja.Zakoncz, 2000); } catch (Exception) { /* bez znaczenia */ }

        void Wykonaj(Action akcja)
        {
            try
            {
                akcja();
            }
            catch (Exception e)
            {
                App.Zapisz(e); // szczegóły w %TEMP%\PodpisPdf-blad.txt
            }
        }

        Wykonaj(() =>
        {
            // Pakiet menu Windows 11 wskazuje na folder programu — po odinstalowaniu byłby martwy.
            if (!Task.Run(() => MenuWindows11.WyrejestrujAsync()).Wait(TimeSpan.FromSeconds(60)))
                throw new TimeoutException("Usuwanie pakietu menu Windows 11 trwa zbyt długo.");
        });
        Wykonaj(MenuWindows11.UsunWypakowanePliki);
        Wykonaj(MenuKontekstowe.Wyrejestruj);
        Wykonaj(DomyslnaAplikacja.Wyrejestruj);
        Wykonaj(() => UstawAutostart(false));
        return 0;
    }

    /// <summary>Wersja pliku programu (do porównań z działającą kopią).</summary>
    public static Version? WersjaPliku(string sciezka)
    {
        try
        {
            var w = FileVersionInfo.GetVersionInfo(sciezka);
            return new Version(w.FileMajorPart, w.FileMinorPart, w.FileBuildPart, w.FilePrivatePart);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
    }
}
