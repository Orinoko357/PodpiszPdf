using System.Diagnostics;
using Microsoft.Win32;

namespace PodpiszPdf;

/// <summary>
/// PodpiszPdf jako aplikacja do otwierania plików PDF (HKCU, bez uprawnień administratora).
/// Windows nie pozwala programom samodzielnie ustawić się jako domyślne — rejestrujemy program
/// jako jedną z możliwych aplikacji, a wyboru użytkownik dokonuje w Ustawieniach Windows.
/// </summary>
internal static class DomyslnaAplikacja
{
    public const string ProgId = "PodpiszPdf.Dokument";
    public const string NazwaAplikacji = "PodpiszPdf";
    public const string ParametrOtwarcia = "--otworz";
    private const string KluczProgId = @"Software\Classes\" + ProgId;
    private const string KluczMozliwosci = @"Software\PodpiszPdf\Capabilities";
    private const string KluczZarejestrowanych = @"Software\RegisteredApplications";
    private const string KluczWyboru = @"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.pdf\UserChoice";
    private const string KluczWyboruNowy = @"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.pdf\UserChoiceLatest";

    private static string Polecenie => $"\"{Instalacja.SciezkaProgramu}\" {ParametrOtwarcia} \"%1\"";

    /// <summary>Program, który obecnie otwiera pliki PDF (identyfikator z wyboru użytkownika) albo null.</summary>
    public static string? BiezacyProgId()
    {
        foreach (var klucz in new[] { KluczWyboruNowy + @"\ProgId", KluczWyboru })
        {
            using var k = Registry.CurrentUser.OpenSubKey(klucz);
            if (k?.GetValue("ProgId") is string id && id.Length > 0)
                return id;
        }
        return null;
    }

    public static bool JestDomyslna => string.Equals(BiezacyProgId(), ProgId, StringComparison.OrdinalIgnoreCase);

    public static bool JestZarejestrowana
    {
        get
        {
            using var k = Registry.CurrentUser.OpenSubKey(KluczProgId + @"\shell\open\command");
            return k?.GetValue("") is string;
        }
    }

    public static bool JestAktualna
    {
        get
        {
            using var k = Registry.CurrentUser.OpenSubKey(KluczProgId + @"\shell\open\command");
            return string.Equals(k?.GetValue("") as string, Polecenie, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Rejestruje PodpiszPdf jako aplikację do PDF. Zapamiętuje dotychczasową aplikację (do przeglądania).
    /// </summary>
    public static void Zarejestruj(Ustawienia ustawienia)
    {
        var obecny = BiezacyProgId();
        if (obecny != null && !string.Equals(obecny, ProgId, StringComparison.OrdinalIgnoreCase))
            ustawienia.PoprzedniaAplikacjaPdf = obecny;

        var exe = Instalacja.SciezkaProgramu;
        using (var k = Registry.CurrentUser.CreateSubKey(KluczProgId))
        {
            k.SetValue("", "Dokument PDF");
            k.SetValue("FriendlyTypeName", "Dokument PDF");
        }
        using (var k = Registry.CurrentUser.CreateSubKey(KluczProgId + @"\DefaultIcon"))
            k.SetValue("", IkonaPdf() ?? $"\"{exe}\",0");
        using (var k = Registry.CurrentUser.CreateSubKey(KluczProgId + @"\shell\open"))
            k.SetValue("FriendlyAppName", NazwaAplikacji);
        using (var k = Registry.CurrentUser.CreateSubKey(KluczProgId + @"\shell\open\command"))
            k.SetValue("", Polecenie);
        using (var k = Registry.CurrentUser.CreateSubKey(@"Software\Classes\.pdf\OpenWithProgids"))
            k.SetValue(ProgId, Array.Empty<byte>(), RegistryValueKind.None);

        using (var k = Registry.CurrentUser.CreateSubKey(KluczMozliwosci))
        {
            k.SetValue("ApplicationName", NazwaAplikacji);
            k.SetValue("ApplicationDescription", "Kwalifikowany podpis elektroniczny PAdES plików PDF");
            k.SetValue("ApplicationIcon", $"\"{exe}\",0");
        }
        using (var k = Registry.CurrentUser.CreateSubKey(KluczMozliwosci + @"\FileAssociations"))
            k.SetValue(".pdf", ProgId);
        using (var k = Registry.CurrentUser.CreateSubKey(KluczZarejestrowanych))
            k.SetValue(NazwaAplikacji, KluczMozliwosci);
        Powiadom();
    }

    /// <summary>
    /// Usuwa rejestrację (odinstalowanie programu). Gdy PodpiszPdf był domyślny, usuwany jest też wybór
    /// użytkownika — Windows zapyta wtedy przy dwukliku, czym otwierać PDF, zamiast wskazywać usunięty program.
    /// Wyboru nie da się samodzielnie przestawić na poprzednią aplikację (Windows chroni go skrótem).
    /// </summary>
    public static void Wyrejestruj()
    {
        if (JestDomyslna)
        {
            foreach (var klucz in new[] { KluczWyboruNowy, KluczWyboru })
            {
                try
                {
                    Registry.CurrentUser.DeleteSubKeyTree(klucz, throwOnMissingSubKey: false);
                }
                catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException or IOException)
                {
                    // Nowsze Windows chronią wybór dla .pdf — zostaje, ale bez ProgId Windows i tak zapyta o aplikację.
                }
            }
        }
        Registry.CurrentUser.DeleteSubKeyTree(KluczProgId, throwOnMissingSubKey: false);
        Registry.CurrentUser.DeleteSubKeyTree(@"Software\PodpiszPdf", throwOnMissingSubKey: false);
        using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Classes\.pdf\OpenWithProgids", writable: true))
            k?.DeleteValue(ProgId, throwOnMissingValue: false);
        using (var k = Registry.CurrentUser.OpenSubKey(KluczZarejestrowanych, writable: true))
            k?.DeleteValue(NazwaAplikacji, throwOnMissingValue: false);
        Powiadom();
    }

    /// <summary>Strona „Aplikacje domyślne” w Ustawieniach Windows, od razu na PodpiszPdf (Windows 11).</summary>
    public static void OtworzUstawieniaWindows()
    {
        var adres = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)
            ? $"ms-settings:defaultapps?registeredAppUser={NazwaAplikacji}"
            : "ms-settings:defaultapps";
        Process.Start(new ProcessStartInfo(adres) { UseShellExecute = true });
    }

    /// <summary>Ikona plików PDF z Acrobat Readera (żeby pliki nie zmieniły wyglądu w Eksploratorze).</summary>
    private static string? IkonaPdf()
    {
        foreach (var id in new[] { "Acrobat.Document.DC", "AcroExch.Document.DC", "AcroExch.Document" })
        {
            using var k = Registry.ClassesRoot.OpenSubKey(id + @"\DefaultIcon");
            if (k?.GetValue("") is string ikona && ikona.Length > 0)
                return ikona;
        }
        return null;
    }

    private static void Powiadom()
    {
        const int ShcneAssocchanged = 0x08000000;
        Win32.SHChangeNotify(ShcneAssocchanged, 0, IntPtr.Zero, IntPtr.Zero);
    }
}
