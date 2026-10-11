using System.Security.Cryptography;
using System.Text;

namespace PodpiszPdf.Core;

/// <summary>
/// Podpis wydania (ECDSA P-256, SHA-256) niezależny od GitHuba: klucz prywatny jest tylko w sekrecie
/// środowiska „wydanie” (dostępny po zatwierdzeniu wydania) i w kopii offline. Podpisana jest wiadomość
/// z numerem wersji, nazwą pliku i sumą SHA-256 instalatora — podmiana pliku, sumy albo numeru wersji
/// (np. podsunięcie starszego instalatora jako nowego) unieważnia podpis.
/// </summary>
public static class PodpisAktualizacji
{
    public const string NazwaPliku = "PodpiszPdf.msi.sig";

    /// <summary>Klucze publiczne (SubjectPublicKeyInfo, base64): główny i zapasowy (do wymiany po wycieku głównego).</summary>
    internal static readonly string[] KluczePubliczne =
    {
        "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEvFlWfC4grkaMo90M1U6r/TKqp29p9VQpthDqw+LK0HB9YIYjwmRsvE3twvNKKgjYCXa04Bafay1EGcmVIxEi9g==",
        "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE+Z2GDlGppwzjo9A5B2lWHVBgQMXJgkilMjcQWQ+kh/Og4x1BoP1aJuF5qXmlO1Sz4xXyjIWuliZMyAzkmw82nw==",
    };

    /// <summary>Podpisywana wiadomość — CI tworzy dokładnie te same bajty (zob. build.yml, zadanie „wydanie”).</summary>
    public static byte[] Wiadomosc(Version wersja, string plik, string sha256) =>
        Encoding.UTF8.GetBytes($"PodpiszPdf-aktualizacja\nwersja={wersja}\nplik={plik}\nsha256={sha256.ToUpperInvariant()}\n");

    /// <summary>Czy podpis (base64, DER — format openssl) pasuje do wiadomości dla któregoś z kluczy.</summary>
    public static bool Sprawdz(Version wersja, string plik, string sha256, string podpisBase64, IReadOnlyList<string>? klucze = null)
    {
        byte[] podpis;
        try
        {
            podpis = Convert.FromBase64String(podpisBase64.Trim());
        }
        catch (FormatException)
        {
            return false;
        }
        var wiadomosc = Wiadomosc(wersja, plik, sha256);
        foreach (var klucz in klucze ?? KluczePubliczne)
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(klucz), out _);
            if (ecdsa.VerifyData(wiadomosc, podpis, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence))
                return true;
        }
        return false;
    }
}
