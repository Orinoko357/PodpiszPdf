using System.Diagnostics;
using System.Security.Cryptography.X509Certificates;
using Windows.Management.Deployment;

namespace PodpisPdf;

/// <summary>
/// Nowe menu kontekstowe Windows 11 wymaga tożsamości pakietu: rejestrujemy mały pakiet MSIX
/// („z zawartością zewnętrzną”), który wskazuje na kopię programu w %APPDATA%\PodpisPdf.
/// Pakiet jest podpisany certyfikatem PodpisPdf — Windows zainstaluje go tylko wtedy, gdy ten certyfikat
/// jest zaufany na komputerze (jednorazowo, z uprawnieniami administratora).
/// </summary>
internal static class MenuWindows11
{
    public const string NazwaPakietu = "PodpisPdf";
    public const string Wydawca = "CN=PodpisPdf";
    private const string ZasobPakietu = "PodpisPdf.msix";
    private const string ZasobCertyfikatu = "PodpisPdf.cer";

    private static string FolderPakietu => Path.Combine(Ustawienia.Folder, "Pakiet");
    private static string SciezkaPakietu => Path.Combine(FolderPakietu, ZasobPakietu);
    private static string SciezkaCertyfikatu => Path.Combine(FolderPakietu, ZasobCertyfikatu);

    /// <summary>Windows 11 (nowe menu kontekstowe istnieje od kompilacji 22000).</summary>
    public static bool JestWindows11 => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000);

    /// <summary>Czy ta kopia programu zawiera podpisany pakiet (wydania z GitHub Actions).</summary>
    public static bool MaPakiet => typeof(MenuWindows11).Assembly.GetManifestResourceInfo(ZasobPakietu) != null;

    /// <summary>Zarejestrowana wersja pakietu albo null.</summary>
    public static Version? ZarejestrowanaWersja()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
            return null;
        var pakiet = new PackageManager().FindPackagesForUser("", NazwaPakietu, Wydawca).FirstOrDefault();
        if (pakiet == null)
            return null;
        var w = pakiet.Id.Version;
        return new Version(w.Major, w.Minor, w.Build, w.Revision);
    }

    /// <summary>Czy certyfikat pakietu jest zaufany (Zaufane osoby lub Zaufane główne urzędy, komputer lokalny).</summary>
    public static bool CertyfikatZaufany()
    {
        using var certyfikat = WczytajCertyfikat();
        if (certyfikat == null)
            return false;
        foreach (var nazwa in new[] { StoreName.TrustedPeople, StoreName.Root })
        {
            using var magazyn = new X509Store(nazwa, StoreLocation.LocalMachine);
            magazyn.Open(OpenFlags.ReadOnly);
            if (magazyn.Certificates.Find(X509FindType.FindByThumbprint, certyfikat.Thumbprint, false).Count > 0)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Dodaje certyfikat do magazynu „Zaufane osoby” komputera (okno UAC). Zwraca false, gdy użytkownik odmówił.
    /// </summary>
    public static bool ZainstalujCertyfikat()
    {
        WypakujZasoby();
        try
        {
            using var proces = Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "certutil.exe"),
                $"-addstore TrustedPeople \"{SciezkaCertyfikatu}\"")
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
            });
            proces?.WaitForExit();
            return proces?.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false; // odmowa w oknie UAC
        }
    }

    /// <summary>
    /// Rejestruje (albo aktualizuje) pakiet dla bieżącego użytkownika. Program musi mieć kopię w %APPDATA%.
    /// </summary>
    public static async Task ZarejestrujAsync()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
            throw new PlatformNotSupportedException("Menu wymaga Windows 11.");
        Instalacja.Aktualizuj(instalujGdyBrak: true);
        WypakujZasoby();
        var opcje = new AddPackageOptions
        {
            ExternalLocationUri = new Uri(Ustawienia.Folder + Path.DirectorySeparatorChar),
            ForceUpdateFromAnyVersion = true,
        };
        var wynik = await new PackageManager().AddPackageByUriAsync(new Uri(SciezkaPakietu), opcje);
        if (wynik.ExtendedErrorCode != null)
            throw new InvalidOperationException(OpisBledu(wynik.ExtendedErrorCode.HResult, wynik.ErrorText));
    }

    public static async Task WyrejestrujAsync()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
            return;
        var menedzer = new PackageManager();
        foreach (var pakiet in menedzer.FindPackagesForUser("", NazwaPakietu, Wydawca).ToList())
        {
            var wynik = await menedzer.RemovePackageAsync(pakiet.Id.FullName);
            if (wynik.ExtendedErrorCode != null)
                throw new InvalidOperationException(OpisBledu(wynik.ExtendedErrorCode.HResult, wynik.ErrorText));
        }
    }

    /// <summary>Po aktualizacji programu: nowsza wersja pakietu (wywoływane w tle, błędy pomijane).</summary>
    public static async Task OdswiezAsync(Ustawienia ustawienia)
    {
        if (!ustawienia.MenuWindows11 || !JestWindows11 || !MaPakiet || !Instalacja.UruchomionyZInstalacji)
            return;
        var zarejestrowana = ZarejestrowanaWersja();
        if (zarejestrowana != null && zarejestrowana >= Instalacja.BiezacaWersja)
            return;
        if (!CertyfikatZaufany())
            return;
        await ZarejestrujAsync();
    }

    private static X509Certificate2? WczytajCertyfikat()
    {
        using var zasob = typeof(MenuWindows11).Assembly.GetManifestResourceStream(ZasobCertyfikatu);
        if (zasob == null)
            return null;
        using var pamiec = new MemoryStream();
        zasob.CopyTo(pamiec);
        return X509CertificateLoader.LoadCertificate(pamiec.ToArray());
    }

    private static void WypakujZasoby()
    {
        Directory.CreateDirectory(FolderPakietu);
        foreach (var (zasob, cel) in new[] { (ZasobPakietu, SciezkaPakietu), (ZasobCertyfikatu, SciezkaCertyfikatu) })
        {
            using var dane = typeof(MenuWindows11).Assembly.GetManifestResourceStream(zasob)
                ?? throw new InvalidOperationException("Ta kopia programu nie zawiera pakietu menu Windows 11.");
            using var plik = File.Create(cel);
            dane.CopyTo(plik);
        }
    }

    private static string OpisBledu(int kod, string tekst) => (uint)kod switch
    {
        0x800B0109 or 0x800B010A => "Certyfikat PodpisPdf nie jest zaufany na tym komputerze. Zainstaluj go przyciskiem „Zainstaluj certyfikat”.",
        0x80073CFF => "Instalowanie pakietów jest zablokowane zasadami Windows (tryb dewelopera lub zasady instalacji aplikacji).",
        _ => $"Nie udało się zarejestrować pakietu (0x{kod:X8}). {tekst}",
    };
}
