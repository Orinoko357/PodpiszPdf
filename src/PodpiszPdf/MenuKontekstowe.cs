using Microsoft.Win32;

namespace PodpiszPdf;

/// <summary>
/// Pozycja „Podpisz w PodpiszPdf” w menu kontekstowym plików PDF w Eksploratorze Windows.
/// Rejestrowana dla bieżącego użytkownika (HKCU) — nie wymaga uprawnień administratora.
/// </summary>
internal static class MenuKontekstowe
{
    public const string Etykieta = "Podpisz w PodpiszPdf";
    private const string Klucz = @"Software\Classes\SystemFileAssociations\.pdf\shell\PodpiszPdf";

    public static void Zarejestruj()
    {
        var exe = Instalacja.SciezkaProgramu;
        using (var klucz = Registry.CurrentUser.CreateSubKey(Klucz))
        {
            klucz.SetValue("", Etykieta);
            klucz.SetValue("Icon", $"\"{exe}\",0");
        }
        using (var polecenie = Registry.CurrentUser.CreateSubKey(Klucz + @"\command"))
            polecenie.SetValue("", $"\"{exe}\" \"%1\"");
        PowiadomEksplorator();
    }

    public static void Wyrejestruj()
    {
        Registry.CurrentUser.DeleteSubKeyTree(Klucz, throwOnMissingSubKey: false);
        PowiadomEksplorator();
    }

    /// <summary>
    /// Czy wpis wskazuje na ten plik programu. Program jest przenośnym plikiem .exe — po przeniesieniu
    /// w inne miejsce wpis trzeba odświeżyć.
    /// </summary>
    public static bool JestAktualne()
    {
        using var polecenie = Registry.CurrentUser.OpenSubKey(Klucz + @"\command");
        var exe = Instalacja.SciezkaProgramu;
        return polecenie?.GetValue("") is string wartosc &&
               string.Equals(wartosc, $"\"{exe}\" \"%1\"", StringComparison.OrdinalIgnoreCase);
    }

    private static void PowiadomEksplorator()
    {
        const int ShcneAssocchanged = 0x08000000;
        Win32.SHChangeNotify(ShcneAssocchanged, 0, IntPtr.Zero, IntPtr.Zero);
    }
}
