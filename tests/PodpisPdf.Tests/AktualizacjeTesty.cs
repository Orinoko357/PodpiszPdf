using System.Net;
using System.Security.Cryptography;
using System.Text;
using PodpisPdf.Core;

namespace PodpisPdf.Tests;

public class AktualizacjeTesty
{
    private static readonly byte[] NowyProgram = Encoding.UTF8.GetBytes("nowa wersja programu");
    private static readonly string Suma = Convert.ToHexString(SHA256.HashData(NowyProgram)).ToLowerInvariant();

    /// <summary>Udawany serwer GitHub: odpowiedzi według adresu.</summary>
    private sealed class Serwer(Dictionary<string, Func<HttpResponseMessage>> odpowiedzi) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage zadanie, CancellationToken anuluj)
        {
            Assert.Contains("PodpisPdf", zadanie.Headers.UserAgent.ToString());
            return Task.FromResult(odpowiedzi.TryGetValue(zadanie.RequestUri!.ToString(), out var o)
                ? o()
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private static HttpResponseMessage Tekst(string tekst) => new(HttpStatusCode.OK) { Content = new StringContent(tekst) };

    private static HttpClient Klient(string znacznik, bool zSuma = true, string? suma = null, byte[]? plik = null)
    {
        var zalaczniki = "[{\"name\":\"PodpisPdf.msi\",\"browser_download_url\":\"https://example.org/PodpisPdf.msi\"}" +
                         (zSuma ? ",{\"name\":\"PodpisPdf.msi.sha256\",\"browser_download_url\":\"https://example.org/PodpisPdf.msi.sha256\"}" : "") + "]";
        var wydanie = $"{{\"tag_name\":\"{znacznik}\",\"html_url\":\"https://github.com/x/releases/tag/{znacznik}\",\"body\":\"Opis zmian\",\"assets\":{zalaczniki}}}";
        return Aktualizacje.UtworzKlienta(new Serwer(new()
        {
            [Aktualizacje.AdresNajnowszego.ToString()] = () => Tekst(wydanie),
            ["https://example.org/PodpisPdf.msi.sha256"] = () => Tekst($"{suma ?? Suma}  PodpisPdf.msi\n"),
            ["https://example.org/PodpisPdf.msi"] = () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(plik ?? NowyProgram) },
        }));
    }

    [Theory]
    [InlineData("v3.0.0.1", "3.0.0.1")]
    [InlineData("3.0", "3.0.0.0")]
    [InlineData("V2.0.0.10", "2.0.0.10")]
    public void WersjaZeZnacznika(string znacznik, string oczekiwana) =>
        Assert.Equal(Version.Parse(oczekiwana), Aktualizacje.WersjaZeZnacznika(znacznik));

    [Fact]
    public void NieprawidlowyZnacznik() => Assert.Null(Aktualizacje.WersjaZeZnacznika("najnowsza"));

    [Fact]
    public async Task NowszaWersjaJestZnaleziona()
    {
        using var klient = Klient("v3.0.0.1");
        var a = await Aktualizacje.SprawdzAsync(klient, new Version(3, 0, 0, 0));
        Assert.NotNull(a);
        Assert.Equal(new Version(3, 0, 0, 1), a.Wersja);
        Assert.Equal(Suma.ToUpperInvariant(), a.Sha256);
        Assert.Equal("Opis zmian", a.Opis);
    }

    [Theory]
    [InlineData("v3.0.0.0")]
    [InlineData("v2.0.0.9")]
    public async Task TaSamaLubStarszaWersjaToBrakAktualizacji(string znacznik)
    {
        using var klient = Klient(znacznik);
        Assert.Null(await Aktualizacje.SprawdzAsync(klient, new Version(3, 0, 0, 0)));
    }

    [Fact]
    public async Task WydanieBezSumyKontrolnejJestPomijane()
    {
        using var klient = Klient("v9.0", zSuma: false);
        Assert.Null(await Aktualizacje.SprawdzAsync(klient, new Version(3, 0)));
    }

    [Fact]
    public async Task BrakWydanToBrakAktualizacji()
    {
        using var klient = Aktualizacje.UtworzKlienta(new Serwer(new()));
        Assert.Null(await Aktualizacje.SprawdzAsync(klient, new Version(3, 0)));
    }

    [Fact]
    public async Task PobranieSprawdzaSume()
    {
        var folder = PlikiTestowe.Folder();
        using var klient = Klient("v3.1");
        var a = (await Aktualizacje.SprawdzAsync(klient, new Version(3, 0)))!;
        var cel = Path.Combine(folder, "PodpisPdf.msi.nowy");
        var postep = new List<double>();
        await Aktualizacje.PobierzAsync(klient, a, cel, new Progress<double>(postep.Add));
        Assert.Equal(NowyProgram, File.ReadAllBytes(cel));
    }

    [Fact]
    public async Task UszkodzonyPlikJestOdrzucany()
    {
        var folder = PlikiTestowe.Folder();
        using var klient = Klient("v3.1", plik: Encoding.UTF8.GetBytes("podmieniony plik"));
        var a = (await Aktualizacje.SprawdzAsync(klient, new Version(3, 0)))!;
        var cel = Path.Combine(folder, "PodpisPdf.msi.nowy");
        await Assert.ThrowsAsync<InvalidDataException>(() => Aktualizacje.PobierzAsync(klient, a, cel));
        Assert.False(File.Exists(cel));
        Assert.Empty(Directory.GetFiles(folder, "*.pobieranie"));
    }

    [Theory]
    [InlineData("ABCDEF0123456789abcdef0123456789ABCDEF0123456789abcdef0123456789  PodpisPdf.msi", true)]
    [InlineData("abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789", true)]
    [InlineData("xyz  PodpisPdf.msi", false)]
    [InlineData("", false)]
    public void OdczytSumy(string tekst, bool poprawna) =>
        Assert.Equal(poprawna, Aktualizacje.OdczytajSume(tekst) != null);
}
