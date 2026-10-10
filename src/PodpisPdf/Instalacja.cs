using System.Diagnostics;
using Microsoft.Win32;

namespace PodpisPdf;

/// <summary>
/// Kopia programu w %APPDATA%\PodpisPdf\PodpisPdf.exe (stąd uruchamia go autostart i menu kontekstowe)
/// oraz wpis autostartu w rejestrze bieżącego użytkownika.
/// </summary>
internal static class Instalacja
{
    public const string ParametrAutostartu = "--autostart";
    private const string KluczAutostartu = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string NazwaWpisu = "PodpisPdf";

    public enum Wynik { BezZmian, Zainstalowano, Zaktualizowano }

    public static string SciezkaDocelowa => Path.Combine(Ustawienia.Folder, "PodpisPdf.exe");

    public static string BiezacyPlik => Environment.ProcessPath ?? Application.ExecutablePath;

    public static Version BiezacaWersja => typeof(Instalacja).Assembly.GetName().Version ?? new Version(0, 0);

    public static bool JestZainstalowany => File.Exists(SciezkaDocelowa);

    public static bool UruchomionyZInstalacji =>
        string.Equals(Path.GetFullPath(BiezacyPlik), SciezkaDocelowa, StringComparison.OrdinalIgnoreCase);

    /// <summary>Plik, który mają uruchamiać autostart i menu kontekstowe.</summary>
    public static string SciezkaProgramu => JestZainstalowany ? SciezkaDocelowa : BiezacyPlik;

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

    /// <summary>
    /// Kopiuje uruchomiony plik do %APPDATA%, gdy jest tam starsza wersja (aktualizacja)
    /// albo gdy kopii nie ma, a <paramref name="instalujGdyBrak"/>. Starszy plik nie nadpisuje nowszego.
    /// </summary>
    public static Wynik Aktualizuj(bool instalujGdyBrak)
    {
        if (UruchomionyZInstalacji)
            return Wynik.BezZmian;
        if (!JestZainstalowany)
        {
            if (!instalujGdyBrak)
                return Wynik.BezZmian;
            Kopiuj();
            return Wynik.Zainstalowano;
        }
        var zainstalowana = WersjaPliku(SciezkaDocelowa);
        if (zainstalowana != null && BiezacaWersja <= zainstalowana)
            return Wynik.BezZmian;
        Kopiuj();
        return Wynik.Zaktualizowano;
    }

    private static void Kopiuj()
    {
        Directory.CreateDirectory(Ustawienia.Folder);
        var tymczasowy = SciezkaDocelowa + ".nowy";
        File.Copy(BiezacyPlik, tymczasowy, overwrite: true);
        // Zamknięta przed chwilą kopia programu może jeszcze przez moment blokować plik.
        for (var proba = 1; ; proba++)
        {
            try
            {
                File.Move(tymczasowy, SciezkaDocelowa, overwrite: true);
                return;
            }
            catch (Exception e) when ((e is IOException or UnauthorizedAccessException) && proba < 10)
            {
                Thread.Sleep(500);
            }
        }
    }

    public static bool AutostartWlaczony()
    {
        using var klucz = Registry.CurrentUser.OpenSubKey(KluczAutostartu);
        return klucz?.GetValue(NazwaWpisu) is string;
    }

    /// <summary>Włącza autostart (z kopią programu w %APPDATA%) albo go wyłącza.</summary>
    public static void UstawAutostart(bool wlacz)
    {
        using var klucz = Registry.CurrentUser.CreateSubKey(KluczAutostartu);
        if (wlacz)
        {
            Aktualizuj(instalujGdyBrak: true);
            var polecenie = $"\"{SciezkaDocelowa}\" {ParametrAutostartu}";
            if (!string.Equals(klucz.GetValue(NazwaWpisu) as string, polecenie, StringComparison.OrdinalIgnoreCase))
                klucz.SetValue(NazwaWpisu, polecenie);
        }
        else
        {
            klucz.DeleteValue(NazwaWpisu, throwOnMissingValue: false);
        }
    }
}
