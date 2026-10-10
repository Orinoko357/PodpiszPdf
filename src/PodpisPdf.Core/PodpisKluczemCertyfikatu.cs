using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using iText.Signatures;

namespace PodpisPdf.Core;

/// <summary>
/// Podpis kluczem prywatnym certyfikatu przez mechanizmy kryptograficzne systemu (CNG/CSP w Windows).
/// Przy kluczu na karcie okno PIN-u wyświetla sterownik producenta — aplikacja nie widzi PIN-u.
/// </summary>
internal sealed class PodpisKluczemCertyfikatu : IExternalSignature
{
    private readonly X509Certificate2 _certyfikat;
    private readonly Action<AsymmetricAlgorithm>? _przygotujKlucz;
    private readonly string _algorytm;

    public PodpisKluczemCertyfikatu(X509Certificate2 certyfikat, Action<AsymmetricAlgorithm>? przygotujKlucz)
    {
        _certyfikat = certyfikat;
        _przygotujKlucz = przygotujKlucz;
        _algorytm = certyfikat.GetKeyAlgorithm() switch
        {
            "1.2.840.113549.1.1.1" => "RSA",
            "1.2.840.10045.2.1" => "ECDSA",
            var oid => throw new BladPodpisu($"Nieobsługiwany rodzaj klucza certyfikatu (OID {oid}). Obsługiwane są klucze RSA i ECDSA."),
        };
    }

    public string GetDigestAlgorithmName() => "SHA-256";

    public string GetSignatureAlgorithmName() => _algorytm;

    public ISignatureMechanismParams? GetSignatureMechanismParameters() => null;

    public byte[] Sign(byte[] message)
    {
        if (_algorytm == "RSA")
        {
            using var rsa = _certyfikat.GetRSAPrivateKey() ?? throw BrakKlucza();
            _przygotujKlucz?.Invoke(rsa);
            return rsa.SignData(message, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }

        using var ecdsa = _certyfikat.GetECDsaPrivateKey() ?? throw BrakKlucza();
        _przygotujKlucz?.Invoke(ecdsa);
        // CMS wymaga podpisu ECDSA w postaci DER (SEQUENCE { r, s }).
        return ecdsa.SignData(message, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
    }

    private static BladPodpisu BrakKlucza() =>
        new("Nie można uzyskać dostępu do klucza prywatnego certyfikatu. Sprawdź, czy karta jest włożona do czytnika.");
}
