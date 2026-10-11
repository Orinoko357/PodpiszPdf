using iText.Commons.Digest;
using iText.Signatures;
using Org.BouncyCastle.Cms;
using Org.BouncyCastle.Tsp;

namespace PodpiszPdf.Core;

/// <summary>
/// Klient serwera znacznika czasu (RFC 3161). Opakowuje dowolnego klienta iText,
/// zamieniając błędy na <see cref="BladZnacznikaCzasu"/> z czytelnym komunikatem.
/// </summary>
public sealed class KlientZnacznikaCzasu : ITSAClient
{
    private readonly ITSAClient _klient;
    private readonly string _opis;

    public KlientZnacznikaCzasu(ITSAClient klient, string opis)
    {
        _klient = klient;
        _opis = opis;
    }

    /// <summary>Klient łączący się z serwerem TSA pod podanym adresem (opcjonalnie z loginem i hasłem).</summary>
    public static KlientZnacznikaCzasu DlaAdresu(string adres, string? login = null, string? haslo = null)
    {
        if (string.IsNullOrWhiteSpace(adres))
            throw new BladZnacznikaCzasu("Nie ustawiono adresu serwera znacznika czasu. Uzupełnij go w oknie „Ustawienia”.");
        if (!Uri.TryCreate(adres.Trim(), UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
            throw new BladZnacznikaCzasu($"Nieprawidłowy adres serwera znacznika czasu: {adres}");

        var klient = string.IsNullOrEmpty(login)
            ? new TSAClientBouncyCastle(uri.ToString())
            : new TSAClientBouncyCastle(uri.ToString(), login, haslo ?? "");
        return new KlientZnacznikaCzasu(klient, uri.Host);
    }

    public int GetTokenSizeEstimate() => _klient.GetTokenSizeEstimate();

    public IMessageDigest GetMessageDigest() => _klient.GetMessageDigest();

    public byte[] GetTimeStampToken(byte[] imprint)
    {
        try
        {
            var token = _klient.GetTimeStampToken(imprint);
            if (token == null || token.Length == 0)
                throw new InvalidOperationException("pusta odpowiedź");
            return token;
        }
        catch (BladZnacznikaCzasu)
        {
            throw;
        }
        catch (Exception e)
        {
            throw new BladZnacznikaCzasu(
                $"Serwer znacznika czasu ({_opis}) jest niedostępny lub odrzucił żądanie. " +
                $"Sprawdź połączenie z internetem oraz adres, login i hasło w ustawieniach.\n\nSzczegóły: {e.Message}", e);
        }
    }

    /// <summary>
    /// Pobiera bieżący czas z serwera znacznika czasu (żądanie znacznika dla losowych danych).
    /// Zwraca null, gdy serwer nie odpowie.
    /// </summary>
    public DateTimeOffset? PobierzCzas() => PobierzCzas(_klient);

    public static DateTimeOffset? PobierzCzas(ITSAClient klient)
    {
        try
        {
            var dane = new byte[32];
            System.Security.Cryptography.RandomNumberGenerator.Fill(dane);
            var skrot = klient.GetMessageDigest().Digest(dane);
            var token = new TimeStampToken(new CmsSignedData(klient.GetTimeStampToken(skrot)));
            return new DateTimeOffset(DateTime.SpecifyKind(token.TimeStampInfo.GenTime, DateTimeKind.Utc));
        }
        catch
        {
            return null;
        }
    }
}
