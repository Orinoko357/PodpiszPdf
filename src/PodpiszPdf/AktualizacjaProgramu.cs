using System.Diagnostics;
using PodpiszPdf.Core;

namespace PodpiszPdf;

/// <summary>
/// Aktualizacja programu z publicznego repozytorium wydań: pobranie instalatora MSI (z kontrolą SHA-256)
/// i jego uruchomienie. Instalator prosi o zgodę administratora, zamienia pliki w Program Files,
/// a po zakończeniu (także nieudanym lub anulowanym) program uruchamia się ponownie z kodem wyniku.
/// </summary>
internal static class AktualizacjaProgramu
{
    private static readonly HttpClient Klient = Aktualizacje.UtworzKlienta();

    public static Task<DostepnaAktualizacja?> SprawdzAsync() =>
        Aktualizacje.SprawdzAsync(Klient, Instalacja.BiezacaWersja);

    private static string FolderPobranych => Path.Combine(Path.GetTempPath(), "PodpiszPdf-aktualizacja");

    /// <summary>Pobiera instalator nowej wersji i zwraca jego ścieżkę.</summary>
    public static async Task<string> PobierzAsync(DostepnaAktualizacja aktualizacja, IProgress<double>? postep = null)
    {
        var folder = FolderPobranych;
        Directory.CreateDirectory(folder);
        var plik = Path.Combine(folder, $"PodpiszPdf-{aktualizacja.Wersja}.msi");
        await Aktualizacje.PobierzAsync(Klient, aktualizacja, plik, postep);
        return plik;
    }

    /// <summary>
    /// Usuwa instalatory pobrane przy wcześniejszych aktualizacjach (kilkadziesiąt MB każdy). Wywoływane w tle
    /// przy starcie; błędy są pomijane (np. plik jeszcze otwarty).
    /// </summary>
    public static void UsunPobraneInstalatory()
    {
        try
        {
            if (Directory.Exists(FolderPobranych))
                Directory.Delete(FolderPobranych, recursive: true);
        }
        catch (Exception)
        {
            // Spróbujemy przy następnym starcie.
        }
    }

    // Uchwyt pobranego instalatora: blokuje zapis i usunięcie pliku do końca działania programu.
    private static FileStream? _zablokowanyInstalator;

    /// <summary>
    /// Uruchamia instalator (z paskiem postępu, bez pytań poza zgodą administratora), a po nim program — zawsze,
    /// także po odmowie w oknie UAC lub błędzie, z kodem zakończenia msiexec (program pokaże wtedy komunikat).
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

        // „start /wait” czeka na msiexec i zostawia jego kod w ERRORLEVEL; „&” uruchamia program niezależnie
        // od wyniku. Ścieżki trafiają do cmd przez zmienne środowiskowe rozwijane z opóźnieniem (/v:on, !…!) —
        // ich treść nie jest już interpretowana (znaki ^ & % ! w ścieżce folderu użytkownika nie psują polecenia).
        var polecenie = "/v:on /c \"start \"\" /wait \"!PODPISZPDF_MSIEXEC!\" /i \"!PODPISZPDF_INSTALATOR!\" /passive /norestart" +
                        $" & start \"\" \"!PODPISZPDF_PROGRAM!\" {Program.ParametrPoAktualizacji} !ERRORLEVEL!\"";
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"), polecenie)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.Environment["PODPISZPDF_MSIEXEC"] = Path.Combine(Environment.SystemDirectory, "msiexec.exe");
        start.Environment["PODPISZPDF_INSTALATOR"] = instalator;
        start.Environment["PODPISZPDF_PROGRAM"] = Instalacja.BiezacyPlik;
        Process.Start(start);

        // Blokada trwa, aż msiexec otworzy plik (i do zamknięcia programu).
        var koniec = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < koniec && Process.GetProcessesByName("msiexec").Length == 0)
            await Task.Delay(200);
        await Task.Delay(2000);
    }
}
