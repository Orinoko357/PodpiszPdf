using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace PodpiszPdf.Core;

/// <summary>Nowsza wersja programu dostępna w publicznym repozytorium wydań.</summary>
public sealed record DostepnaAktualizacja(Version Wersja, Uri AdresPliku, string Sha256, Uri Strona, string Opis);

/// <summary>
/// Sprawdzanie i pobieranie aktualizacji z wydań GitHub (publiczne repozytorium tylko z plikami programu).
/// Każde wydanie zawiera instalator <c>PodpiszPdf.msi</c>, <c>PodpiszPdf.msi.sha256</c> i podpis <c>PodpiszPdf.msi.sig</c>:
/// podpis (klucz spoza GitHuba, <see cref="PodpisAktualizacji"/>) potwierdza wersję i sumę, a pobrany plik musi mieć tę sumę.
/// </summary>
public static class Aktualizacje
{
    public const string Repozytorium = "Orinoko357/PodpiszPdf-wydania";
    public const string NazwaPliku = "PodpiszPdf.msi";
    public const string NazwaSumy = "PodpiszPdf.msi.sha256";

    public static Uri AdresNajnowszego => new($"https://api.github.com/repos/{Repozytorium}/releases/latest");

    public static Uri StronaWydan => new($"https://github.com/{Repozytorium}/releases");

    /// <summary>Klient HTTP z nagłówkami wymaganymi przez API GitHub.</summary>
    public static HttpClient UtworzKlienta(HttpMessageHandler? obsluga = null)
    {
        var klient = obsluga == null ? new HttpClient() : new HttpClient(obsluga);
        klient.Timeout = TimeSpan.FromMinutes(5);
        klient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("PodpiszPdf", "1"));
        klient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return klient;
    }

    /// <summary>Numer wersji ze znacznika wydania („v3.0.0.1” → 3.0.0.1).</summary>
    public static Version? WersjaZeZnacznika(string? znacznik)
    {
        if (string.IsNullOrWhiteSpace(znacznik))
            return null;
        var tekst = znacznik.Trim().TrimStart('v', 'V');
        if (!Version.TryParse(tekst, out var wersja))
            return null;
        // 3.0 i 3.0.0.0 to ta sama wersja.
        return new Version(wersja.Major, wersja.Minor, Math.Max(0, wersja.Build), Math.Max(0, wersja.Revision));
    }

    /// <summary>
    /// Zwraca nowszą wersję niż <paramref name="biezaca"/> albo null (brak nowszej lub brak wydań).
    /// Błędy sieci i serwera przechodzą do wywołującego.
    /// </summary>
    public static Task<DostepnaAktualizacja?> SprawdzAsync(HttpClient klient, Version biezaca, CancellationToken anuluj = default) =>
        SprawdzAsync(klient, biezaca, null, anuluj);

    /// <param name="klucze">Klucze publiczne do sprawdzenia podpisu (testy); null — klucze wbudowane w program.</param>
    internal static async Task<DostepnaAktualizacja?> SprawdzAsync(HttpClient klient, Version biezaca, IReadOnlyList<string>? klucze,
        CancellationToken anuluj = default)
    {
        using var odpowiedz = await klient.GetAsync(AdresNajnowszego, anuluj).ConfigureAwait(false);
        if (odpowiedz.StatusCode == HttpStatusCode.NotFound)
            return null; // repozytorium bez wydań
        odpowiedz.EnsureSuccessStatusCode();
        using var json = await JsonDocument.ParseAsync(await odpowiedz.Content.ReadAsStreamAsync(anuluj).ConfigureAwait(false), cancellationToken: anuluj).ConfigureAwait(false);
        var wydanie = json.RootElement;

        var wersja = WersjaZeZnacznika(Tekst(wydanie, "tag_name"));
        if (wersja == null || wersja <= Normalizuj(biezaca))
            return null;

        Uri? plik = null, suma = null, podpis = null;
        if (wydanie.TryGetProperty("assets", out var zalaczniki) && zalaczniki.ValueKind == JsonValueKind.Array)
        {
            foreach (var z in zalaczniki.EnumerateArray())
            {
                var nazwa = Tekst(z, "name");
                var adres = Tekst(z, "browser_download_url");
                if (adres == null || !Uri.TryCreate(adres, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
                    continue;
                if (string.Equals(nazwa, NazwaPliku, StringComparison.OrdinalIgnoreCase))
                    plik = uri;
                else if (string.Equals(nazwa, NazwaSumy, StringComparison.OrdinalIgnoreCase))
                    suma = uri;
                else if (string.Equals(nazwa, PodpisAktualizacji.NazwaPliku, StringComparison.OrdinalIgnoreCase))
                    podpis = uri;
            }
        }
        if (plik == null || suma == null || podpis == null)
            return null; // wydanie niekompletne — bez sumy kontrolnej i podpisu nie aktualizujemy

        var tekstSumy = await klient.GetStringAsync(suma, anuluj).ConfigureAwait(false);
        var sha = OdczytajSume(tekstSumy) ?? throw new InvalidDataException("Nieprawidłowy plik sumy kontrolnej aktualizacji.");
        var tekstPodpisu = await klient.GetStringAsync(podpis, anuluj).ConfigureAwait(false);
        if (!PodpisAktualizacji.Sprawdz(wersja, NazwaPliku, sha, tekstPodpisu, klucze))
        {
            throw new InvalidDataException(
                $"Wydanie {wersja} nie ma ważnego podpisu PodpiszPdf. Aktualizacja została zablokowana — " +
                "plik mógł zostać podmieniony. Zgłoś to osobie odpowiedzialnej za program.");
        }
        var strona = Uri.TryCreate(Tekst(wydanie, "html_url"), UriKind.Absolute, out var s) ? s : StronaWydan;
        return new DostepnaAktualizacja(wersja, plik, sha, strona, Tekst(wydanie, "body") ?? "");
    }

    /// <summary>
    /// Pobiera plik aktualizacji do <paramref name="cel"/> i sprawdza sumę SHA-256.
    /// Przy niezgodnej sumie plik jest usuwany, a wyjątek przechodzi do wywołującego.
    /// </summary>
    public static async Task PobierzAsync(HttpClient klient, DostepnaAktualizacja aktualizacja, string cel,
        IProgress<double>? postep = null, CancellationToken anuluj = default)
    {
        var tymczasowy = cel + ".pobieranie";
        try
        {
            using (var odpowiedz = await klient.GetAsync(aktualizacja.AdresPliku, HttpCompletionOption.ResponseHeadersRead, anuluj).ConfigureAwait(false))
            {
                odpowiedz.EnsureSuccessStatusCode();
                var dlugosc = odpowiedz.Content.Headers.ContentLength;
                await using var zrodlo = await odpowiedz.Content.ReadAsStreamAsync(anuluj).ConfigureAwait(false);
                await using var plik = File.Create(tymczasowy);
                var bufor = new byte[81920];
                long razem = 0;
                int n;
                while ((n = await zrodlo.ReadAsync(bufor, anuluj).ConfigureAwait(false)) > 0)
                {
                    await plik.WriteAsync(bufor.AsMemory(0, n), anuluj).ConfigureAwait(false);
                    razem += n;
                    if (dlugosc > 0)
                        postep?.Report((double)razem / dlugosc.Value);
                }
            }

            string sha;
            await using (var plik = File.OpenRead(tymczasowy))
                sha = Convert.ToHexString(await SHA256.HashDataAsync(plik, anuluj).ConfigureAwait(false));
            if (!string.Equals(sha, aktualizacja.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Pobrany plik aktualizacji jest uszkodzony (niezgodna suma kontrolna SHA-256). Spróbuj ponownie później.");
            File.Move(tymczasowy, cel, overwrite: true);
        }
        finally
        {
            try { File.Delete(tymczasowy); } catch (IOException) { /* plik mógł już zostać przeniesiony */ }
        }
    }

    /// <summary>Suma z pliku w formacie <c>sha256sum</c> („HEX  nazwa”) albo samej wartości.</summary>
    public static string? OdczytajSume(string tekst)
    {
        var pierwszy = tekst.Trim().Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return pierwszy != null && pierwszy.Length == 64 && pierwszy.All(Uri.IsHexDigit) ? pierwszy.ToUpperInvariant() : null;
    }

    private static Version Normalizuj(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build), Math.Max(0, v.Revision));

    private static string? Tekst(JsonElement element, string nazwa) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(nazwa, out var w) && w.ValueKind == JsonValueKind.String ? w.GetString() : null;
}
