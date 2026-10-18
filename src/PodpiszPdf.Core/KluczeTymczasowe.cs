using System.Security.Cryptography.X509Certificates;

namespace PodpiszPdf.Core;

/// <summary>Wczytywanie certyfikatów z kluczem (PFX) bez zapisu klucza na stałe.</summary>
public static class KluczeTymczasowe
{
    /// <summary>
    /// Klucz tylko w pamięci (Windows, Linux). macOS tego nie obsługuje — tam klucz trafia do tymczasowego
    /// pęku kluczy, usuwanego po zwolnieniu certyfikatu.
    /// </summary>
    public static X509KeyStorageFlags Flagi =>
        OperatingSystem.IsMacOS() ? X509KeyStorageFlags.DefaultKeySet : X509KeyStorageFlags.EphemeralKeySet;
}
