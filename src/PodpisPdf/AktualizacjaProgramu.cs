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

    // Uchwyt pobranego instalatora: blokuje zapis i usunięcie pliku do końca działania programu.
    private static FileStream? _zablokowanyInstalator;

    /// <summary>
    /// Uruchamia instalator (z paskiem postępu, bez pytań poza zgodą administratora), a po nim program.
    /// Pośrednikiem jest cmd.exe z uprawnieniami użytkownika, więc program nie startuje jako administrator.
    /// Plik jest przed uruchomieniem blokowany przed zapisem i sprawdzany ponownie (suma potwierdzona podpisem
    /// wydania), żeby nie dało się go podmienić między sprawdzeniem a odczytem przez msiexec.
    /// </summary>
    public static async Task ZainstalujIUruchomAsync(string instalator, string sha256)
    {
        _zablokowanyInstalator?.Dispose();
        _zablokowanyInstalator = new FileStream(instalator, FileMode.Open, FileAccess.Read, FileShare.Read);
        var faktyczna = Convert.ToHexString(await System.Security.Cryptography.SHA256.HashDataAsync(_zablokowanyInstalator));
        if (!string.Equals(faktyczna, sha256, StringComparison.OrdinalIgnoreCase))
        {
            _zablokowanyInstalator.Dispose();
            _zablokowanyInstalator = null;
            throw new InvalidDataException("Plik instalatora zmienił się po pobraniu. Aktualizacja została przerwana.");
        }

        var msiexec = Path.Combine(Environment.SystemDirectory, "msiexec.exe");
        var polecenie = $"/c \"\"{msiexec}\" /i \"{instalator}\" /passive /norestart && start \"\" \"{Instalacja.BiezacyPlik}\" {Program.ParametrPoAktualizacji}\"";
        Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"), polecenie)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        });

        // Blokada trwa, aż msiexec otworzy plik (i do zamknięcia programu).
        var koniec = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < koniec && Process.GetProcessesByName("msiexec").Length == 0)
            await Task.Delay(200);
        await Task.Delay(2000);
    }
}
