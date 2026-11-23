using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Weryfikator.Logika;

namespace Weryfikator.Tests
{
    /// <summary>Testowy urząd certyfikacji i certyfikaty osób (z numerem PESEL w polu serialNumber).</summary>
    internal sealed class Urzad
    {
        public X509Certificate2 Cert;
        private readonly RSA _klucz = RSA.Create(2048);
        private long _numer = 100;

        public Urzad(string nazwa = "CN=Testowy Urząd Kwalifikowany, O=Test, C=PL")
        {
            var z = new CertificateRequest(new X500DistinguishedName(nazwa), _klucz, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            z.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
            z.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
            Cert = z.CreateSelfSigned(DateTimeOffset.UtcNow.AddYears(-5), DateTimeOffset.UtcNow.AddYears(5));
        }

        public X509Certificate2 Wystaw(string cn, string? serialNumber, bool kwalifikowany = true, bool pieczec = false, bool ecdsa = false,
            DateTimeOffset? od = null, DateTimeOffset? doDnia = null, bool tsa = false)
        {
            var dn = new X500DistinguishedName("CN=" + cn + (serialNumber != null ? ", SERIALNUMBER=" + serialNumber : "") + ", C=PL");
            CertificateRequest z; AsymmetricAlgorithm klucz;
            if (ecdsa) { var ec = ECDsa.Create(ECCurve.NamedCurves.nistP384); klucz = ec; z = new CertificateRequest(dn, ec, HashAlgorithmName.SHA384); }
            else { var rsa = RSA.Create(2048); klucz = rsa; z = new CertificateRequest(dn, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1); }
            z.CertificateExtensions.Add(new X509KeyUsageExtension(tsa ? X509KeyUsageFlags.DigitalSignature : X509KeyUsageFlags.NonRepudiation, true));
            if (tsa) z.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new Oid("1.3.6.1.5.5.7.3.8") }, true));
            if (kwalifikowany)
            {
                // QcCompliance (0.4.0.1862.1.1) i QcType (0.4.0.1862.1.6): podpis (.1) albo pieczęć (.2).
                var qc = DerT.Seq(DerT.Seq(DerT.Oid(0x04, 0x00, 0x8E, 0x46, 0x01, 0x01)),
                    DerT.Seq(DerT.Oid(0x04, 0x00, 0x8E, 0x46, 0x01, 0x06), DerT.Seq(DerT.Oid(0x04, 0x00, 0x8E, 0x46, 0x01, 0x06, (byte)(pieczec ? 2 : 1)))));
                z.CertificateExtensions.Add(new X509Extension("1.3.6.1.5.5.7.1.3", qc, false));
            }
            var numer = BitConverter.GetBytes(System.Threading.Interlocked.Increment(ref _numer));
            Array.Reverse(numer);
            using var cert = z.Create(Cert.SubjectName, X509SignatureGenerator.CreateForRSA(_klucz, RSASignaturePadding.Pkcs1),
                od ?? DateTimeOffset.UtcNow.AddDays(-30), doDnia ?? DateTimeOffset.UtcNow.AddYears(1), numer);
            using var zKluczem = klucz is RSA r ? cert.CopyWithPrivateKey(r) : cert.CopyWithPrivateKey((ECDsa)klucz);
            return new X509Certificate2(zKluczem.Export(X509ContentType.Pfx, "t"), "t", X509KeyStorageFlags.Exportable);
        }

        public WpisListy Wpis(string typ = "CA/QC", string status = "granted") => Wpis(Cert, typ, status);

        public static WpisListy Wpis(X509Certificate2 c, string typ, string status = "granted") => new WpisListy
        {
            Kraj = "PL", Dostawca = "Test", Typ = typ, Cert = new X509Certificate2(c.RawData),
            Historia = { new KeyValuePair<DateTime, string>(new DateTime(2016, 7, 1, 0, 0, 0, DateTimeKind.Utc), status) }
        };
    }

    /// <summary>Minimalny zapis DER do testów.</summary>
    internal static class DerT
    {
        public static byte[] Tlv(byte znacznik, byte[] tresc)
        {
            var l = new List<byte> { znacznik };
            if (tresc.Length < 128) l.Add((byte)tresc.Length);
            else if (tresc.Length < 256) { l.Add(0x81); l.Add((byte)tresc.Length); }
            else { l.Add(0x82); l.Add((byte)(tresc.Length >> 8)); l.Add((byte)tresc.Length); }
            l.AddRange(tresc);
            return l.ToArray();
        }
        public static byte[] Seq(params byte[][] e) => Tlv(0x30, System.Linq.Enumerable.ToArray(System.Linq.Enumerable.SelectMany(e, x => x)));
        public static byte[] Oid(params byte[] b) => Tlv(0x06, b);
        public static byte[] Octet(byte[] b) => Tlv(0x04, b);
    }

    internal static class Pliki
    {
        // Poprawne numery PESEL (suma kontrolna) używane w testach.
        public const string PeselA = "44051401359";
        public const string PeselB = "02070803628";

        public static string Folder()
        {
            var f = Path.Combine(Path.GetTempPath(), "WeryfikatorTesty", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(f);
            return f;
        }

        /// <summary>Minimalny arkusz xlsx: komórki tekstowe (wspólne napisy) i liczbowe.</summary>
        public static string Xlsx(string folder, string[] teksty, string[] liczby, string dodatkowaKomorka = "")
        {
            var p = Path.Combine(folder, "lista.xlsx");
            File.Delete(p);
            using var fs = new FileStream(p, FileMode.Create);
            using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
            var ss = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\"?><sst xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");
            var sh = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>");
            int w = 1;
            sh.Append("<row r=\"1\"><c r=\"A1\" t=\"s\"><v>0</v></c></row>");
            ss.Append("<si><t>PESEL</t></si>");
            for (int i = 0; i < teksty.Length; i++)
            {
                ss.Append("<si><r><t>").Append(teksty[i]).Append("</t></r></si>");
                sh.Append($"<row r=\"{++w}\"><c r=\"A{w}\" t=\"s\"><v>{i + 1}</v></c><c r=\"B{w}\" t=\"inlineStr\"><is><t>Jan</t></is></c>{(i == 0 ? dodatkowaKomorka : "")}</row>");
            }
            foreach (var l in liczby) sh.Append($"<row r=\"{++w}\"><c r=\"A{w}\"><v>{l}</v></c></row>");
            ss.Append("</sst>"); sh.Append("</sheetData></worksheet>");
            Wpis(zip, "xl/sharedStrings.xml", ss.ToString());
            Wpis(zip, "xl/worksheets/sheet1.xml", sh.ToString());
            return p;
        }

        private static void Wpis(ZipArchive zip, string nazwa, string tresc)
        {
            using var s = new StreamWriter(zip.CreateEntry(nazwa).Open(), new UTF8Encoding(false));
            s.Write(tresc);
        }
    }
}
