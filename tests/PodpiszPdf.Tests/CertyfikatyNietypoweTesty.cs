using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using PodpiszPdf.Core;
using Xunit;

namespace PodpiszPdf.Tests;

/// <summary>Nietypowe certyfikaty w magazynie (audyt 2026-10).</summary>
public class CertyfikatyNietypoweTesty
{
    /// <summary>Certyfikat z wielowartościowym RDN „CN=… + SERIALNUMBER=…” (spotykany w certyfikatach kwalifikowanych).</summary>
    private static X509Certificate2 ZWielowartosciowymRdn()
    {
        var w = new AsnWriter(AsnEncodingRules.DER);
        using (w.PushSequence())
        {
            using (w.PushSetOf())
            {
                using (w.PushSequence())
                {
                    w.WriteObjectIdentifier("2.5.4.6");
                    w.WriteCharacterString(UniversalTagNumber.PrintableString, "PL");
                }
            }
            using (w.PushSetOf())
            {
                using (w.PushSequence())
                {
                    w.WriteObjectIdentifier("2.5.4.3");
                    w.WriteCharacterString(UniversalTagNumber.UTF8String, "Jan Kowalski");
                }
                using (w.PushSequence())
                {
                    w.WriteObjectIdentifier("2.5.4.5");
                    w.WriteCharacterString(UniversalTagNumber.PrintableString, "PNOPL-1234");
                }
            }
        }
        var dn = new X500DistinguishedName(w.Encode());
        using var rsa = RSA.Create(2048);
        var zadanie = new CertificateRequest(dn, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        zadanie.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.NonRepudiation, critical: true));
        using var cert = zadanie.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        return new X509Certificate2(cert.Export(X509ContentType.Pfx, "test"), "test", X509KeyStorageFlags.EphemeralKeySet);
    }

    // C3
    [Fact]
    public void WielowartosciowyRdn()
    {
        using var c = ZWielowartosciowymRdn();
        Assert.Equal("Jan Kowalski", CertyfikatInfo.PobierzImieNazwisko(c));
        Assert.Equal("Jan Kowalski", new CertyfikatInfo(c).Wystawca);
    }

    [Fact]
    public void WadliwyCertyfikatNieBlokujeListy()
    {
        using var dobry = CertyfikatyTestowe.Utworz(imie: "Anna", nazwisko: "Nowak");
        using var wielowartosciowy = ZWielowartosciowymRdn();
        var wadliwy = CertyfikatyTestowe.Utworz(imie: "Zepsuty", nazwisko: "Certyfikat");
        wadliwy.Dispose(); // odczyt danych zwolnionego certyfikatu kończy się wyjątkiem

        var lista = MagazynCertyfikatow.ZbudujListe(new[] { wadliwy, dobry, wielowartosciowy }, DateTime.Now);

        Assert.Equal(new[] { "Anna Nowak", "Jan Kowalski" }, lista.Select(c => c.ImieNazwisko));
    }

    // S9
    [Fact]
    public void SprawdzenieWaznosciPrzedPodpisem()
    {
        using var dobry = CertyfikatyTestowe.Utworz();
        using var przeterminowany = CertyfikatyTestowe.Utworz(od: DateTimeOffset.UtcNow.AddYears(-2), doDnia: DateTimeOffset.UtcNow.AddDays(-1));
        Assert.Null(CertyfikatInfo.PowodNiewaznosci(dobry, DateTime.Now));
        Assert.Contains("wygasł", CertyfikatInfo.PowodNiewaznosci(przeterminowany, DateTime.Now));
        Assert.True(new CertyfikatInfo(dobry).Kwalifikowany);
    }
}
