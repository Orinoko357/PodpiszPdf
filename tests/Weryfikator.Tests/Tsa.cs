#nullable enable
#if NET8_0_OR_GREATER
using System;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
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

namespace Weryfikator.Tests
{
    /// <summary>Lokalny serwer znacznika czasu (RFC 3161) — jak w testach PodpisPdf, z dostępem do certyfikatu.</summary>
    internal sealed class Tsa : ITSAClient
    {
        private readonly AsymmetricCipherKeyPair _klucze;
        private readonly Org.BouncyCastle.X509.X509Certificate _cert;
        private long _numer;
        public DateTime Czas = DateTime.UtcNow;

        public Tsa()
        {
            var gen = new RsaKeyPairGenerator();
            gen.Init(new KeyGenerationParameters(new SecureRandom(), 2048));
            _klucze = gen.GenerateKeyPair();
            var cg = new X509V3CertificateGenerator();
            var nazwa = new X509Name("CN=Testowy TSA, C=PL");
            cg.SetSerialNumber(BigInteger.One);
            cg.SetIssuerDN(nazwa); cg.SetSubjectDN(nazwa);
            cg.SetNotBefore(DateTime.UtcNow.AddYears(-5)); cg.SetNotAfter(DateTime.UtcNow.AddYears(5));
            cg.SetPublicKey(_klucze.Public);
            cg.AddExtension(X509Extensions.ExtendedKeyUsage, true, new ExtendedKeyUsage(KeyPurposeID.id_kp_timeStamping));
            _cert = cg.Generate(new Asn1SignatureFactory("SHA256WithRSA", _klucze.Private));
        }

        public X509Certificate2 Cert => new X509Certificate2(_cert.GetEncoded());
        public int GetTokenSizeEstimate() => 8192;
        public IMessageDigest GetMessageDigest() => new TSAClientBouncyCastle("http://localhost/").GetMessageDigest();

        public byte[] GetTimeStampToken(byte[] imprint)
        {
            var gen = new TimeStampTokenGenerator(_klucze.Private, _cert, TspAlgorithms.Sha256, "1.2.3.4");
            gen.SetCertificates(CollectionUtilities.CreateStore(new[] { _cert }));
            var rg = new TimeStampRequestGenerator();
            rg.SetCertReq(true);
            return gen.Generate(rg.Generate(TspAlgorithms.Sha256, imprint), BigInteger.ValueOf(Interlocked.Increment(ref _numer)), Czas).GetEncoded();
        }

        public static string UtworzPdf(string folder, string nazwa = "wniosek.pdf")
        {
            var sciezka = Path.Combine(folder, nazwa);
            using var pdf = new PdfDocument(new PdfWriter(sciezka));
            using var doc = new Document(pdf);
            doc.Add(new Paragraph("Wniosek testowy"));
            return sciezka;
        }
    }

    /// <summary>Podpis kluczem RSA certyfikatu (dla iText).</summary>
    internal sealed class PodpisRsa : IExternalSignature
    {
        private readonly X509Certificate2 _cert;
        public PodpisRsa(X509Certificate2 cert) { _cert = cert; }
        public string GetDigestAlgorithmName() => "SHA-256";
        public string GetSignatureAlgorithmName() => "RSA";
        public ISignatureMechanismParams? GetSignatureMechanismParameters() => null;
        public byte[] Sign(byte[] message)
        {
            using var rsa = System.Security.Cryptography.X509Certificates.RSACertificateExtensions.GetRSAPrivateKey(_cert)!;
            return rsa.SignData(message, System.Security.Cryptography.HashAlgorithmName.SHA256, System.Security.Cryptography.RSASignaturePadding.Pkcs1);
        }
    }

    internal sealed class TsaAwaria : ITSAClient
    {
        public int GetTokenSizeEstimate() => 8192;
        public IMessageDigest GetMessageDigest() => new TSAClientBouncyCastle("http://localhost/").GetMessageDigest();
        public byte[] GetTimeStampToken(byte[] imprint) => throw new IOException("Connection refused");
    }
}
#endif
