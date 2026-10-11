using System.Security.Cryptography.X509Certificates;
using Windows.Management.Deployment;

namespace PodpisPdf;

/// <summary>
/// Nowe menu kontekstowe Windows 11 wymaga tożsamości pakietu: rejestrujemy (dla użytkownika) mały pakiet
/// MSIX „z zawartością zewnętrzną”, który wskazuje na zainstalowany program w Program Files.
/// Pakiet jest podpisany certyfikatem PodpisPdf — instalator MSI dodaje go do zaufanych na komputerze.
/// </summary>
internal static class MenuWindows11
{
    public const string NazwaPakietu = "PodpisPdf";
    public const string Wydawca = "CN=PodpisPdf";
    private const string ZasobPakietu = "PodpisPdf.msix";
    private const string ZasobCertyfikatu = "PodpisPdf.cer";

    private static string FolderPakietu => Path.Combine(Ustawienia.Folder, "Pakiet");
    private static string SciezkaPakietu => Path.Combine(FolderPakietu, ZasobPakietu);

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
    /// Rejestruje (albo aktualizuje) pakiet dla bieżącego użytkownika. Program musi mieć kopię w %APPDATA%.
    /// </summary>
    public static async Task ZarejestrujAsync()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
            throw new PlatformNotSupportedException("Menu wymaga Windows 11.");
        WypakujZasoby();
        var opcje = new AddPackageOptions
        {
            ExternalLocationUri = new Uri(Instalacja.FolderProgramu.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar),
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

    /// <summary>
    /// Przy starcie: rejestracja pakietu (pierwsze uruchomienie po instalacji) albo jego nowsza wersja po
    /// aktualizacji. Certyfikat dodaje instalator MSI, więc nie trzeba zgody administratora.
    /// </summary>
    public static async Task OdswiezAsync(Ustawienia ustawienia)
    {
        if (!ustawienia.PozycjaMenuWindows11 || !JestWindows11 || !MaPakiet)
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
        using var dane = typeof(MenuWindows11).Assembly.GetManifestResourceStream(ZasobPakietu)
            ?? throw new InvalidOperationException("Ta kopia programu nie zawiera pakietu menu Windows 11.");
        using var plik = File.Create(SciezkaPakietu);
        dane.CopyTo(plik);
    }

    /// <summary>Usuwa pakiet wypakowany do folderu ustawień (przy odinstalowaniu programu).</summary>
    public static void UsunWypakowanePliki()
    {
        if (Directory.Exists(FolderPakietu))
            Directory.Delete(FolderPakietu, recursive: true);
    }

    private static string OpisBledu(int kod, string tekst) => (uint)kod switch
    {
        0x800B0109 or 0x800B010A => "Certyfikat PodpisPdf nie jest zaufany na tym komputerze. Zainstaluj PodpisPdf ponownie instalatorem MSI — instalator dodaje certyfikat (wymaga uprawnień administratora).",
        0x80073CFF => "Instalowanie pakietów jest zablokowane zasadami Windows (tryb dewelopera lub zasady instalacji aplikacji).",
        _ => $"Nie udało się zarejestrować pakietu (0x{kod:X8}). {tekst}",
    };
}
