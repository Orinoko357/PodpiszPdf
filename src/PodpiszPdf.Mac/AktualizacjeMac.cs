using System.Runtime.InteropServices;
using System.Text.Json;
using PodpiszPdf.Core;

namespace PodpiszPdf.Mac;

/// <summary>Nowsza wersja dla macOS w publicznym repozytorium wydań (obraz DMG dla procesora tego komputera).</summary>
internal static class AktualizacjeMac
{
    public static string NazwaObrazu =>
        RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "PodpiszPdf-macOS-AppleSilicon.dmg" : "PodpiszPdf-macOS-Intel.dmg";

    /// <summary>Wersja i adres do pobrania albo null (brak nowszej wersji dla macOS).</summary>
    public static async Task<(Version Wersja, string Adres)?> SprawdzAsync(Version biezaca, CancellationToken anuluj = default)
    {
        using var klient = Aktualizacje.UtworzKlienta();
        klient.Timeout = TimeSpan.FromSeconds(30);
        using var odpowiedz = await klient.GetAsync(Aktualizacje.AdresNajnowszego, anuluj);
        if (!odpowiedz.IsSuccessStatusCode)
            return null;
        using var json = await JsonDocument.ParseAsync(await odpowiedz.Content.ReadAsStreamAsync(anuluj), cancellationToken: anuluj);
        var wydanie = json.RootElement;
        var wersja = Aktualizacje.WersjaZeZnacznika(wydanie.TryGetProperty("tag_name", out var t) ? t.GetString() : null);
        if (wersja == null || wersja <= biezaca)
            return null;
        if (wydanie.TryGetProperty("assets", out var zalaczniki) && zalaczniki.ValueKind == JsonValueKind.Array)
        {
            foreach (var z in zalaczniki.EnumerateArray())
            {
                if (z.TryGetProperty("name", out var n) && n.GetString() == NazwaObrazu &&
                    z.TryGetProperty("browser_download_url", out var u) && u.GetString() is { } adres &&
                    adres.StartsWith("https://github.com/", StringComparison.Ordinal))
                    return (wersja, adres);
            }
        }
        return null; // wydanie bez wersji dla macOS
    }
}
