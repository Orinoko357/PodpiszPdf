using System.Diagnostics;
using Microsoft.Win32;

namespace PodpisPdf;

/// <summary>
/// Otwieranie PDF do przeglądania: Adobe Acrobat (Reader), a gdy go nie ma — aplikacja, która otwierała
/// pliki PDF przed PodpisPdf, przeglądarka Microsoft Edge albo okno „Otwórz za pomocą”.
/// </summary>
internal static class Przegladarka
{
    private static readonly string[] ProgramyAdobe = { "Acrobat.exe", "AcroRd32.exe" };

    /// <summary>Nazwa programu do przeglądania (do opisu przycisku).</summary>
    public static string Nazwa(Ustawienia ustawienia)
    {
        if (ProgramAdobe() is { } adobe)
            return Path.GetFileName(adobe).Equals("AcroRd32.exe", StringComparison.OrdinalIgnoreCase) ? "Adobe Acrobat Reader" : "Adobe Acrobat";
        if (PolecenieProgId(ustawienia.PoprzedniaAplikacjaPdf) != null)
            return "poprzednio używanej aplikacji";
        return "przeglądarce Microsoft Edge";
    }

    public static void Otworz(string plik, Ustawienia ustawienia)
    {
        if (ProgramAdobe() is { } adobe && Uruchom(adobe, $"\"{plik}\""))
            return;
        if (PolecenieProgId(ustawienia.PoprzedniaAplikacjaPdf) is { } polecenie && UruchomPolecenie(polecenie, plik))
            return;
        if (SciezkaAppPaths("msedge.exe") is { } edge && Uruchom(edge, $"\"{plik}\""))
            return;
        Uruchom(Path.Combine(Environment.SystemDirectory, "rundll32.exe"), $"shell32.dll,OpenAs_RunDLL {plik}");
    }

    private static string? ProgramAdobe() => ProgramyAdobe.Select(SciezkaAppPaths).FirstOrDefault(p => p != null);

    /// <summary>Ścieżka programu z rejestru „App Paths” (tam rejestrują się Acrobat i Edge).</summary>
    private static string? SciezkaAppPaths(string exe)
    {
        foreach (var galaz in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            using var k = galaz.OpenSubKey($@"Software\Microsoft\Windows\CurrentVersion\App Paths\{exe}");
            if (k?.GetValue("") is string sciezka)
            {
                sciezka = Environment.ExpandEnvironmentVariables(sciezka.Trim('"'));
                if (File.Exists(sciezka))
                    return sciezka;
            }
        }
        return null;
    }

    /// <summary>Polecenie otwarcia z rejestru dla identyfikatora typu pliku (nie PodpisPdf).</summary>
    private static string? PolecenieProgId(string? progId)
    {
        if (string.IsNullOrEmpty(progId) || string.Equals(progId, DomyslnaAplikacja.ProgId, StringComparison.OrdinalIgnoreCase))
            return null;
        using var k = Registry.ClassesRoot.OpenSubKey($@"{progId}\shell\open\command");
        return k?.GetValue("") is string p && p.Length > 0 && !p.Contains("PodpisPdf", StringComparison.OrdinalIgnoreCase) ? p : null;
    }

    private static bool UruchomPolecenie(string polecenie, string plik)
    {
        polecenie = Environment.ExpandEnvironmentVariables(polecenie);
        string exe, argumenty;
        if (polecenie.StartsWith('"'))
        {
            var koniec = polecenie.IndexOf('"', 1);
            if (koniec < 0)
                return false;
            exe = polecenie[1..koniec];
            argumenty = polecenie[(koniec + 1)..].Trim();
        }
        else
        {
            var spacja = polecenie.IndexOf(' ');
            exe = spacja < 0 ? polecenie : polecenie[..spacja];
            argumenty = spacja < 0 ? "" : polecenie[(spacja + 1)..].Trim();
        }
        argumenty = argumenty.Contains("%1") || argumenty.Contains("%L", StringComparison.OrdinalIgnoreCase)
            ? argumenty.Replace("%1", plik).Replace("%L", plik).Replace("%l", plik)
            : (argumenty + $" \"{plik}\"").Trim();
        // Pozostałe zmienne Eksploratora (%*, %I itp.) nie są potrzebne do otwarcia pliku.
        argumenty = argumenty.Replace("%*", "");
        return Uruchom(exe, argumenty);
    }

    private static bool Uruchom(string exe, string argumenty)
    {
        try
        {
            Process.Start(new ProcessStartInfo(exe, argumenty) { UseShellExecute = true });
            return true;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or FileNotFoundException or InvalidOperationException)
        {
            return false;
        }
    }
}
