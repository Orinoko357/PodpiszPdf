using iText.Signatures;

namespace PodpiszPdf.Core;

/// <summary>
/// Wczytanie i przygotowanie bibliotek (iText, BouncyCastle, PDFium, czcionek) w tle po starcie programu,
/// żeby pierwszy podgląd i pierwszy podpis nie czekały na nie.
/// </summary>
public static class Rozgrzewka
{
    /// <param name="zglaszajBledy">false — błędy są pomijane (rozgrzewka jest tylko optymalizacją).</param>
    public static void Wykonaj(bool zglaszajBledy = false)
    {
        try
        {
            var pdf = Wizualizacja.PodgladPdf(new DaneWizualizacji("Rozgrzewka", "Stanowisko", null, DateTimeOffset.Now));
            DokumentPdf.Renderuj(pdf, 0, 0.5);
            new TSAClientBouncyCastle("http://localhost/").GetMessageDigest().Digest(new byte[] { 1 });
        }
        catch (Exception) when (!zglaszajBledy)
        {
            // Błąd wyjdzie przy właściwym użyciu.
        }
    }
}
