using System.Diagnostics;
using PodpisPdf.Core;

namespace PodpisPdf;

/// <summary>
/// Aktualizacja programu z publicznego repozytorium wydań: pobranie instalatora MSI (z kontrolą SHA-256)
/// i jego uruchomienie. Instalator prosi o zgodę administratora, zamienia pliki w Program Files,
/// a po zakończeniu uruchamia program ponownie.
/// </summary>
internal static class AktualizacjaProgramu
{
    private static readonly HttpClient Klient = Aktualizacje.UtworzKlienta();

    public static Task<DostepnaAktualizacja?> SprawdzAsync() =>
        Aktualizacje.SprawdzAsync(Klient, Instalacja.BiezacaWersja);

    /// <summary>Pobiera instalator nowej wersji i zwraca jego ścieżkę.</summary>
    public static async Task<string> PobierzAsync(DostepnaAktualizacja aktualizacja, IProgress<double>? postep = null)
    {
        var folder = Path.Combine(Path.GetTempPath(), "PodpisPdf-aktualizacja");
        Directory.CreateDirectory(folder);
        var plik = Path.Combine(folder, $"PodpisPdf-{aktualizacja.Wersja}.msi");
        await Aktualizacje.PobierzAsync(Klient, aktualizacja, plik, postep);
        return plik;
    }

    /// <summary>
    /// Uruchamia instalator (z paskiem postępu, bez pytań poza zgodą administratora), a po nim program.
    /// Pośrednikiem jest cmd.exe z uprawnieniami użytkownika, więc program nie startuje jako administrator.
    /// </summary>
    public static void ZainstalujIUruchom(string instalator)
    {
        var msiexec = Path.Combine(Environment.SystemDirectory, "msiexec.exe");
        var polecenie = $"/c \"\"{msiexec}\" /i \"{instalator}\" /passive /norestart && start \"\" \"{Instalacja.BiezacyPlik}\" {Program.ParametrPoAktualizacji}\"";
        Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"), polecenie)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        });
    }
}
