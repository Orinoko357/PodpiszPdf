using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using iText.Commons.Digest;
using iText.Kernel.Pdf;
using iText.Layout;
using iText.Layout.Element;
using iText.Signatures;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Operators;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Tsp;
using Org.BouncyCastle.Utilities.Collections;
using Org.BouncyCastle.X509;
using BcX509Certificate = Org.BouncyCastle.X509.X509Certificate;

namespace PodpiszPdf.Tests;

/// <summary>Certyfikaty testowe (odpowiednik pliku PFX) generowane w locie.</summary>
internal static class CertyfikatyTestowe
{
    public static X509Certificate2 Utworz(
        string? imie = "Zażółć",
        string? nazwisko = "Gęślą-Jaźń",
        string cn = "Zażółć Gęślą-Jaźń",
        bool kwalifikowany = true,
        X509KeyUsageFlags uzycie = X509KeyUsageFlags.NonRepudiation,
        bool ecdsa = false,
        DateTimeOffset? od = null,
        DateTimeOffset? doDnia = null)
    {
        var nazwa = new X500DistinguishedNameBuilder();
        nazwa.AddCountryOrRegion("PL");
        if (imie != null) nazwa.Add("2.5.4.42", imie);
        if (nazwisko != null) nazwa.Add("2.5.4.4", nazwisko);
        nazwa.AddCommonName(cn);
        var dn = nazwa.Build();

        CertificateRequest zadanie;
        AsymmetricAlgorithm klucz;
        if (ecdsa)
        {
            var ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            zadanie = new CertificateRequest(dn, ec, HashAlgorithmName.SHA256);
            klucz = ec;
        }
        else
        {
            var rsa = RSA.Create(2048);
            zadanie = new CertificateRequest(dn, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            klucz = rsa;
        }

        zadanie.CertificateExtensions.Add(new X509KeyUsageExtension(uzycie, critical: true));
        if (kwalifikowany)
            zadanie.CertificateExtensions.Add(new System.Security.Cryptography.X509Certificates.X509Extension("1.3.6.1.5.5.7.1.3", QcStatements(), critical: false));

        using var cert = zadanie.CreateSelfSigned(
            od ?? DateTimeOffset.UtcNow.AddDays(-1),
            doDnia ?? DateTimeOffset.UtcNow.AddYears(1));
        var pfx = cert.Export(X509ContentType.Pfx, "test");
        klucz.Dispose();
        return new X509Certificate2(pfx, "test", PodpiszPdf.Core.KluczeTymczasowe.Flagi);
    }

    private static byte[] QcStatements()
    {
        var w = new AsnWriter(AsnEncodingRules.DER);
        using (w.PushSequence())
        {
            using (w.PushSequence())
                w.WriteObjectIdentifier("0.4.0.1862.1.1"); // QcCompliance
            using (w.PushSequence())
                w.WriteObjectIdentifier("0.4.0.1862.1.4"); // QcSSCD
        }
        return w.Encode();
    }
}

/// <summary>Lokalny serwer znacznika czasu (RFC 3161) do testów bez sieci.</summary>
internal sealed class TestowyTsa : ITSAClient
{
    private readonly AsymmetricCipherKeyPair _klucze;
    private readonly BcX509Certificate _certyfikat;
    private long _numer;

    public TestowyTsa()
    {
        var gen = new RsaKeyPairGenerator();
        gen.Init(new KeyGenerationParameters(new SecureRandom(), 2048));
        _klucze = gen.GenerateKeyPair();

        var cg = new X509V3CertificateGenerator();
        var nazwa = new X509Name("CN=Testowy TSA, C=PL");
        cg.SetSerialNumber(BigInteger.One);
        cg.SetIssuerDN(nazwa);
        cg.SetSubjectDN(nazwa);
        cg.SetNotBefore(DateTime.UtcNow.AddDays(-1));
        cg.SetNotAfter(DateTime.UtcNow.AddYears(1));
        cg.SetPublicKey(_klucze.Public);
        cg.AddExtension(X509Extensions.ExtendedKeyUsage, true, new ExtendedKeyUsage(KeyPurposeID.id_kp_timeStamping));
        _certyfikat = cg.Generate(new Asn1SignatureFactory("SHA256WithRSA", _klucze.Private));
    }

    public bool Awaria { get; set; }
    public int LiczbaZadan { get; private set; }

    public int GetTokenSizeEstimate() => 8192;

    public IMessageDigest GetMessageDigest() => new TSAClientBouncyCastle("http://localhost/").GetMessageDigest();

    public byte[] GetTimeStampToken(byte[] imprint)
    {
        LiczbaZadan++;
        if (Awaria)
            throw new IOException("Connection refused");

        var gen = new TimeStampTokenGenerator(_klucze.Private, _certyfikat, TspAlgorithms.Sha256, "1.2.3.4");
        gen.SetCertificates(CollectionUtilities.CreateStore(new[] { _certyfikat }));
        var rg = new TimeStampRequestGenerator();
        rg.SetCertReq(true);
        var req = rg.Generate(TspAlgorithms.Sha256, imprint);
        return gen.Generate(req, BigInteger.ValueOf(Interlocked.Increment(ref _numer)), DateTime.UtcNow).GetEncoded();
    }
}

internal static class PlikiTestowe
{
    public static string Folder()
    {
        var f = Path.Combine(Path.GetTempPath(), "PodpiszPdfTesty", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(f);
        return f;
    }

    public static string UtworzPdf(string folder, int strony = 2, string nazwa = "wniosek.pdf")
    {
        var sciezka = Path.Combine(folder, nazwa);
        using var pdf = new PdfDocument(new PdfWriter(sciezka));
        using var doc = new Document(pdf);
        for (var i = 1; i <= strony; i++)
        {
            if (i > 1) doc.Add(new AreaBreak());
            doc.Add(new Paragraph($"Wniosek testowy - strona {i}"));
        }
        return sciezka;
    }

    /// <summary>Pionowy PNG 20x40 px (proporcje zbliżone do herbu).</summary>
    public static string UtworzLogoPionowe(string folder)
    {
        var sciezka = Path.Combine(folder, "logo-pionowe.png");
        File.WriteAllBytes(sciezka, Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAABQAAAAoCAIAAABxU02MAAAAIklEQVR4nGOQowAwjGoe1TyqeVTzqOZRzaOaRzWPaiYXAACtDBlQIWwIRQAAAABJRU5ErkJggg=="));
        return sciezka;
    }

    /// <summary>PNG 60x30 px z kanałem alfa (półprzezroczysty prostokąt i koło na przezroczystym tle).</summary>
    public static string UtworzLogoZAlfa(string folder)
    {
        var sciezka = Path.Combine(folder, "logo-alfa.png");
        File.WriteAllBytes(sciezka, Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAADwAAAAeCAYAAABwmH1PAAAApUlEQVR4nO3YyxGAMAhFUbQqi7Bci7AsV2wyQP4ZiO/s43BloyECAAjkmPnwl+hpPXsR3SNnYUOCe8Jq9b6I5uCVkZqW+OpgD6GpmvDiYI+hqZLwbHCE0JQVfloHI8YS2XOrwVFjmTa/ueEdicHRt8ukDmx4d2LwrO/Y1aQObJhF37I2v7nhqNHW3PiWzvEYPuVvKeUhfMn/sOQXNx4lPN5pAUBsH1bpIiAx1lPIAAAAAElFTkSuQmCC"));
        return sciezka;
    }

    /// <summary>
    /// Dokument PDF/A (część 1 lub 2, poziom B) z osadzoną czcionką i profilem sRGB
    /// (wbudowany profil lcms „sRGB built-in”, bez praw autorskich).
    /// </summary>
    public static string UtworzPdfA(string folder, int czesc, string nazwa = "pdfa.pdf")
    {
        const string ProfilSrgb =
            "AAACTGxjbXMEQAAAbW50clJHQiBYWVogB+oACgALAAQAMAAoYWNzcEFQUEwAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAPbWAAEAAAAA0y1sY21zAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAALZGVzYwAAAQgAAAA2Y3BydAAAAUAAAABMd3RwdAAAAYwAAAAUY2hhZAAAAaAAAAAsclhZWgAAAcwAAAAUYlhZWgAAAeAAAAAUZ1hZWgAAAfQAAAAUclRSQwAAAggAAAAgZ1RSQwAAAggAAAAgYlRSQwAAAggAAAAgY2hybQAAAigAAAAkbWx1YwAAAAAAAAABAAAADGVuVVMAAAAaAAAAHABzAFIARwBCACAAYgB1AGkAbAB0AC0AaQBuAABtbHVjAAAAAAAAAAEAAAAMZW5VUwAAADAAAAAcAE4AbwAgAGMAbwBwAHkAcgBpAGcAaAB0ACwAIAB1AHMAZQAgAGYAcgBlAGUAbAB5WFlaIAAAAAAAAPbWAAEAAAAA0y1zZjMyAAAAAAABDEIAAAXe///zJQAAB5MAAP2Q///7of///aIAAAPcAADAblhZWiAAAAAAAABvoAAAOPUAAAOQWFlaIAAAAAAAACSfAAAPhAAAtsNYWVogAAAAAAAAYpcAALeHAAAY2XBhcmEAAAAAAAMAAAACZmYAAPKnAAANWQAAE9AAAApbY2hybQAAAAAAAwAAAACj1wAAVHsAAEzNAACZmgAAJmYAAA9c";
        var sciezka = Path.Combine(folder, nazwa);
        var zgodnosc = czesc == 1 ? PdfAConformance.PDF_A_1B : PdfAConformance.PDF_A_2B;
        var profil = new PdfOutputIntent("Custom", "", "http://www.color.org", "sRGB IEC61966-2.1",
            new MemoryStream(Convert.FromBase64String(ProfilSrgb)));
        using (var pdf = new iText.Pdfa.PdfADocument(new PdfWriter(sciezka), zgodnosc, profil))
        using (var doc = new Document(pdf))
        {
            using var zasob = typeof(PodpiszPdf.Core.Wizualizacja).Assembly.GetManifestResourceStream("PodpiszPdf.Core.LiberationSans-Regular.ttf")!;
            using var bajty = new MemoryStream();
            zasob.CopyTo(bajty);
            var czcionka = iText.Kernel.Font.PdfFontFactory.CreateFont(bajty.ToArray(), iText.IO.Font.PdfEncodings.IDENTITY_H,
                iText.Kernel.Font.PdfFontFactory.EmbeddingStrategy.FORCE_EMBEDDED);
            doc.Add(new Paragraph("Dokument PDF/A z systemu EZD").SetFont(czcionka));
        }
        return sciezka;
    }

    /// <summary>Mały PNG 40x20 px (dwa kolorowe pola).</summary>
    public static string UtworzLogo(string folder)
    {
        var sciezka = Path.Combine(folder, "logo.png");
        File.WriteAllBytes(sciezka, Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAACgAAAAUCAIAAABwJOjsAAAAKUlEQVR4nGM4IadBNhKxWUA2Yhi1eNTiUYtHLR61eNTiUYtHLR45FgMA/rgdDtYNtksAAAAASUVORK5CYII="));
        return sciezka;
    }
}
