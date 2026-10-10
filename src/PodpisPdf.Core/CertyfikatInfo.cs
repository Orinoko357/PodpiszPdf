using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace PodpisPdf.Core;

/// <summary>Certyfikat z magazynu Windows wraz z danymi potrzebnymi do wyświetlenia na liście.</summary>
public sealed class CertyfikatInfo
{
    private const string OidGivenName = "2.5.4.42";
    private const string OidSurname = "2.5.4.4";
    private const string OidCommonName = "2.5.4.3";
    private const string OidQcStatements = "1.3.6.1.5.5.7.1.3";
    private const string OidQcCompliance = "0.4.0.1862.1.1";

    public CertyfikatInfo(X509Certificate2 certyfikat)
    {
        Certyfikat = certyfikat;
        ImieNazwisko = PobierzImieNazwisko(certyfikat);
        Wystawca = PobierzCommonName(certyfikat.IssuerName) ?? certyfikat.Issuer;
        WaznyDo = certyfikat.NotAfter;
        Kwalifikowany = CzyKwalifikowany(certyfikat);
    }

    public X509Certificate2 Certyfikat { get; }
    public string ImieNazwisko { get; }
    public string Wystawca { get; }
    public DateTime WaznyDo { get; }
    public bool Kwalifikowany { get; }
    public string Odcisk => Certyfikat.Thumbprint;

    public override string ToString() =>
        $"{(Kwalifikowany ? "★ " : "")}{ImieNazwisko} — {Wystawca} (ważny do {WaznyDo:yyyy-MM-dd})";

    /// <summary>
    /// Czy certyfikat nadaje się do podpisu: ma klucz prywatny, jest ważny w podanej chwili
    /// i ma użycie klucza nonRepudiation.
    /// </summary>
    public static bool CzyNadajeSieDoPodpisu(X509Certificate2 cert, DateTime chwila)
    {
        if (!cert.HasPrivateKey)
            return false;
        if (chwila < cert.NotBefore || chwila > cert.NotAfter)
            return false;
        var uzycie = cert.Extensions.OfType<X509KeyUsageExtension>().FirstOrDefault();
        return uzycie != null && uzycie.KeyUsages.HasFlag(X509KeyUsageFlags.NonRepudiation);
    }

    /// <summary>Czy certyfikat zawiera deklarację QcCompliance (rozszerzenie qcStatements).</summary>
    public static bool CzyKwalifikowany(X509Certificate2 cert)
    {
        var ext = cert.Extensions[OidQcStatements];
        if (ext == null)
            return false;
        try
        {
            var reader = new AsnReader(ext.RawData, AsnEncodingRules.DER);
            var statements = reader.ReadSequence();
            while (statements.HasData)
            {
                var statement = statements.ReadSequence();
                if (statement.ReadObjectIdentifier() == OidQcCompliance)
                    return true;
            }
        }
        catch (AsnContentException)
        {
            // Uszkodzone rozszerzenie traktujemy jak brak deklaracji.
        }
        return false;
    }

    /// <summary>Imię i nazwisko z pól GivenName i Surname, a gdy ich brak — z pola CN.</summary>
    public static string PobierzImieNazwisko(X509Certificate2 cert)
    {
        string? imie = null, nazwisko = null, cn = null;
        foreach (var rdn in cert.SubjectName.EnumerateRelativeDistinguishedNames())
        {
            var oid = rdn.GetSingleElementType().Value;
            var wartosc = rdn.GetSingleElementValue();
            switch (oid)
            {
                case OidGivenName: imie ??= wartosc; break;
                case OidSurname: nazwisko ??= wartosc; break;
                case OidCommonName: cn ??= wartosc; break;
            }
        }

        if (!string.IsNullOrWhiteSpace(imie) || !string.IsNullOrWhiteSpace(nazwisko))
            return string.Join(" ", new[] { imie, nazwisko }.Where(s => !string.IsNullOrWhiteSpace(s))).Trim();
        return cn ?? cert.Subject;
    }

    private static string? PobierzCommonName(X500DistinguishedName nazwa)
    {
        foreach (var rdn in nazwa.EnumerateRelativeDistinguishedNames())
        {
            if (rdn.GetSingleElementType().Value == OidCommonName)
                return rdn.GetSingleElementValue();
        }
        return null;
    }
}
