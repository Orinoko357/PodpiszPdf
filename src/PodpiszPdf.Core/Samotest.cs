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

namespace PodpiszPdf.Core;

/// <summary>
/// Próbny podpis bez karty i bez sieci (certyfikat i serwer znacznika czasu tworzone w pamięci).
/// Sprawdza w gotowym PodpiszPdf.exe cały łańcuch: dokument, wizualizacja, podpis PAdES B-T, weryfikacja
/// i podgląd PDFium — m.in. po to, by optymalizacja publikacji (przycinanie) niczego nie usunęła.
/// </summary>
public static class Samotest
{
    /// <summary>Wykonuje próbny podpis; przy błędzie rzuca wyjątek z opisem.</summary>
    public static void PodpisProbny()
    {
        var folder = Path.Combine(Path.GetTempPath(), "PodpiszPdf-samotest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var wejscie = Path.Combine(folder, "dokument.pdf");
            using (var dokument = new PdfDocument(new PdfWriter(wejscie)))
            using (var uklad = new Document(dokument))
                uklad.Add(new Paragraph("Próbny dokument PodpiszPdf — zażółć gęślą jaźń"));

            using var certyfikat = UtworzCertyfikat();
            var wyjscie = Path.Combine(folder, "dokument_podpisany.pdf");
            var wynik = new PodpisywaczPdf(new LokalnyTsa()).Podpisz(wejscie, wyjscie, certyfikat, new OpcjePodpisu
            {
                Stanowisko = "Samotest\nPodpiszPdf",
                Miejsce = new MiejscePodpisu(1, 300, 100),
            });

            using (var pdf = new PdfDocument(new PdfReader(wynik.SciezkaWyniku)))
            {
                var util = new SignatureUtil(pdf);
                var nazwy = util.GetSignatureNames();
                if (nazwy.Count != 1)
                    throw new InvalidOperationException($"Samotest: oczekiwano 1 podpisu, jest {nazwy.Count}.");
                var podpis = util.ReadSignatureData(nazwy[0]);
                if (!podpis.VerifySignatureIntegrityAndAuthenticity())
                    throw new InvalidOperationException("Samotest: podpis nie przeszedł weryfikacji.");
                if (podpis.GetTimeStampTokenInfo() == null)
                    throw new InvalidOperationException("Samotest: brak znacznika czasu.");
            }

            var obraz = DokumentPdf.Renderuj(File.ReadAllBytes(wynik.SciezkaWyniku), 0, 0.5);
            if (obraz.Szerokosc <= 0 || obraz.Bgra.Length == 0)
                throw new InvalidOperationException("Samotest: nie udało się wyrenderować podpisanego dokumentu.");
        }
        finally
        {
            try { Directory.Delete(folder, recursive: true); } catch (IOException) { /* pliki tymczasowe */ }
        }
    }

    private static X509Certificate2 UtworzCertyfikat()
    {
        using var klucz = RSA.Create(2048);
        var nazwa = new X500DistinguishedNameBuilder();
        nazwa.AddCountryOrRegion("PL");
        nazwa.Add("2.5.4.42", "Jan");
        nazwa.Add("2.5.4.4", "Próbny");
        nazwa.AddCommonName("Jan Próbny");
        var zadanie = new CertificateRequest(nazwa.Build(), klucz, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        zadanie.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.NonRepudiation, critical: true));
        using var certyfikat = zadanie.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        return new X509Certificate2(certyfikat.Export(X509ContentType.Pfx, "samotest"), "samotest", PodpiszPdf.Core.KluczeTymczasowe.Flagi);
    }

    /// <summary>Serwer znacznika czasu (RFC 3161) w pamięci.</summary>
    private sealed class LokalnyTsa : ITSAClient
    {
        private readonly AsymmetricCipherKeyPair _klucze;
        private readonly BcX509Certificate _certyfikat;
        private long _numer;

        public LokalnyTsa()
        {
            var generator = new RsaKeyPairGenerator();
            generator.Init(new KeyGenerationParameters(new SecureRandom(), 2048));
            _klucze = generator.GenerateKeyPair();
            var nazwa = new X509Name("CN=Samotest TSA, C=PL");
            var cg = new X509V3CertificateGenerator();
            cg.SetSerialNumber(BigInteger.One);
            cg.SetIssuerDN(nazwa);
            cg.SetSubjectDN(nazwa);
            cg.SetNotBefore(DateTime.UtcNow.AddDays(-1));
            cg.SetNotAfter(DateTime.UtcNow.AddDays(1));
            cg.SetPublicKey(_klucze.Public);
            cg.AddExtension(X509Extensions.ExtendedKeyUsage, true, new ExtendedKeyUsage(KeyPurposeID.id_kp_timeStamping));
            _certyfikat = cg.Generate(new Asn1SignatureFactory("SHA256WithRSA", _klucze.Private));
        }

        public int GetTokenSizeEstimate() => 8192;

        public IMessageDigest GetMessageDigest() => new TSAClientBouncyCastle("http://localhost/").GetMessageDigest();

        public byte[] GetTimeStampToken(byte[] imprint)
        {
            var generator = new TimeStampTokenGenerator(_klucze.Private, _certyfikat, TspAlgorithms.Sha256, "1.2.3.4");
            generator.SetCertificates(CollectionUtilities.CreateStore(new[] { _certyfikat }));
            var rg = new TimeStampRequestGenerator();
            rg.SetCertReq(true);
            var zadanie = rg.Generate(TspAlgorithms.Sha256, imprint);
            return generator.Generate(zadanie, BigInteger.ValueOf(Interlocked.Increment(ref _numer)), DateTime.UtcNow).GetEncoded();
        }
    }
}
