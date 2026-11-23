using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Xml;

namespace Weryfikator.Logika
{
    /// <summary>Sprawdzanie podpisów XML-DSig (listy zaufania, XAdES) — z algorytmami ECDSA i RSA-PSS, których SignedXml sam nie zna.</summary>
    internal static class PodpisXml
    {
        public const string NsDs = "http://www.w3.org/2000/09/xmldsig#";
        private static bool _zarejestrowano;
        private static readonly object Blokada = new object();

        public static void Rejestruj()
        {
            lock (Blokada)
            {
                if (_zarejestrowano) return;
                CryptoConfig.AddAlgorithm(typeof(OpisEcdsaSha1), "http://www.w3.org/2001/04/xmldsig-more#ecdsa-sha1");
                CryptoConfig.AddAlgorithm(typeof(OpisEcdsaSha256), "http://www.w3.org/2001/04/xmldsig-more#ecdsa-sha256");
                CryptoConfig.AddAlgorithm(typeof(OpisEcdsaSha384), "http://www.w3.org/2001/04/xmldsig-more#ecdsa-sha384");
                CryptoConfig.AddAlgorithm(typeof(OpisEcdsaSha512), "http://www.w3.org/2001/04/xmldsig-more#ecdsa-sha512");
                CryptoConfig.AddAlgorithm(typeof(OpisPssSha256), "http://www.w3.org/2007/05/xmldsig-more#sha256-rsa-MGF1");
                CryptoConfig.AddAlgorithm(typeof(OpisPssSha384), "http://www.w3.org/2007/05/xmldsig-more#sha384-rsa-MGF1");
                CryptoConfig.AddAlgorithm(typeof(OpisPssSha512), "http://www.w3.org/2007/05/xmldsig-more#sha512-rsa-MGF1");
                _zarejestrowano = true;
            }
        }

        public static XmlDocument Wczytaj(Stream s, string? adresBazowy = null)
        {
            var doc = new XmlDocument { PreserveWhitespace = true, XmlResolver = null };
            var ust = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
            using (var r = adresBazowy == null ? XmlReader.Create(s, ust) : XmlReader.Create(s, ust, adresBazowy))
                doc.Load(r);
            return doc;
        }

        public static List<X509Certificate2> Certyfikaty(XmlElement podpis)
        {
            var l = new List<X509Certificate2>();
            foreach (XmlNode k in podpis.ChildNodes)
            {
                if (!(k is XmlElement ki) || ki.LocalName != "KeyInfo" || ki.NamespaceURI != NsDs) continue;
                foreach (XmlElement c in ki.GetElementsByTagName("X509Certificate", NsDs))
                    try { l.Add(new X509Certificate2(Convert.FromBase64String(c.InnerText))); } catch (Exception) { }
            }
            return l;
        }

        /// <summary>Sprawdza podpis (skróty odwołań i wartość podpisu) kluczem jednego z certyfikatów; zwraca ten certyfikat albo null.</summary>
        public static X509Certificate2? Sprawdz(SignedXml sx, IEnumerable<X509Certificate2> kandydaci, out string? blad)
        {
            Rejestruj();
            blad = null;
            foreach (var c in kandydaci)
            {
                AsymmetricAlgorithm? klucz = null;
                try
                {
                    klucz = (AsymmetricAlgorithm?)c.GetRSAPublicKey() ?? c.GetECDsaPublicKey();
                    if (klucz == null) { blad = "Nieobsługiwany rodzaj klucza certyfikatu."; continue; }
                    if (sx.CheckSignature(klucz)) return c;
                    blad = "Podpis XML jest nieprawidłowy (treść zmieniona po podpisaniu albo uszkodzony plik).";
                }
                catch (Exception e) { blad = "Nie udało się sprawdzić podpisu XML: " + e.Message; }
                finally { klucz?.Dispose(); }
            }
            return null;
        }
    }

    /// <summary>Do odwołań podpisu XAdES „obok pliku” (detached) — udostępnia wyłącznie pliki z folderu podpisu.</summary>
    internal sealed class ResolverLokalny : XmlResolver
    {
        private readonly string _folder;
        public ResolverLokalny(string folder) { _folder = Path.GetFullPath(folder); }

        public override object GetEntity(Uri absoluteUri, string? role, Type? ofObjectToReturn)
        {
            if (!absoluteUri.IsFile) throw new IOException("Odwołanie poza folder podpisu: " + absoluteUri);
            var p = Path.GetFullPath(absoluteUri.LocalPath);
            if (!string.Equals(Path.GetDirectoryName(p), _folder.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                throw new IOException("Odwołanie poza folder podpisu: " + absoluteUri);
            return new FileStream(p, FileMode.Open, FileAccess.Read, FileShare.Read);
        }
    }

    public abstract class OpisPodpisu : SignatureDescription
    {
        private readonly HashAlgorithmName _skrot;
        private readonly bool _pss;
        protected OpisPodpisu(HashAlgorithmName skrot, bool pss)
        {
            _skrot = skrot; _pss = pss;
            // SignedXml sprawdza, czy klucz pasuje do typu podanego w opisie algorytmu.
            KeyAlgorithm = (pss ? typeof(RSA) : typeof(ECDsa)).AssemblyQualifiedName;
            DigestAlgorithm = skrot.Name;
        }

        public override HashAlgorithm CreateDigest()
        {
            if (_skrot == HashAlgorithmName.SHA1) return SHA1.Create();
            if (_skrot == HashAlgorithmName.SHA384) return SHA384.Create();
            if (_skrot == HashAlgorithmName.SHA512) return SHA512.Create();
            return SHA256.Create();
        }
        public override AsymmetricSignatureDeformatter CreateDeformatter(AsymmetricAlgorithm key) => new Deformatter(key, _skrot, _pss);
        public override AsymmetricSignatureFormatter CreateFormatter(AsymmetricAlgorithm key) => new Formatter(key, _skrot, _pss);

        private sealed class Deformatter : AsymmetricSignatureDeformatter
        {
            private AsymmetricAlgorithm _k; private readonly HashAlgorithmName _s; private readonly bool _pss;
            public Deformatter(AsymmetricAlgorithm k, HashAlgorithmName s, bool pss) { _k = k; _s = s; _pss = pss; }
            public override void SetKey(AsymmetricAlgorithm key) { _k = key; }
            public override void SetHashAlgorithm(string strName) { }
            public override bool VerifySignature(byte[] rgbHash, byte[] rgbSignature)
            {
                if (_k is ECDsa ec) return !_pss && ec.VerifyHash(rgbHash, rgbSignature);
                if (_k is RSA rsa) return rsa.VerifyHash(rgbHash, rgbSignature, _s, _pss ? RSASignaturePadding.Pss : RSASignaturePadding.Pkcs1);
                return false;
            }
        }

        private sealed class Formatter : AsymmetricSignatureFormatter
        {
            private AsymmetricAlgorithm _k; private readonly HashAlgorithmName _s; private readonly bool _pss;
            public Formatter(AsymmetricAlgorithm k, HashAlgorithmName s, bool pss) { _k = k; _s = s; _pss = pss; }
            public override void SetKey(AsymmetricAlgorithm key) { _k = key; }
            public override void SetHashAlgorithm(string strName) { }
            public override byte[] CreateSignature(byte[] rgbHash)
            {
                if (_k is ECDsa ec) return ec.SignHash(rgbHash);
                return ((RSA)_k).SignHash(rgbHash, _s, _pss ? RSASignaturePadding.Pss : RSASignaturePadding.Pkcs1);
            }
        }
    }

    public sealed class OpisEcdsaSha1 : OpisPodpisu { public OpisEcdsaSha1() : base(HashAlgorithmName.SHA1, false) { } }
    public sealed class OpisEcdsaSha256 : OpisPodpisu { public OpisEcdsaSha256() : base(HashAlgorithmName.SHA256, false) { } }
    public sealed class OpisEcdsaSha384 : OpisPodpisu { public OpisEcdsaSha384() : base(HashAlgorithmName.SHA384, false) { } }
    public sealed class OpisEcdsaSha512 : OpisPodpisu { public OpisEcdsaSha512() : base(HashAlgorithmName.SHA512, false) { } }
    public sealed class OpisPssSha256 : OpisPodpisu { public OpisPssSha256() : base(HashAlgorithmName.SHA256, true) { } }
    public sealed class OpisPssSha384 : OpisPodpisu { public OpisPssSha384() : base(HashAlgorithmName.SHA384, true) { } }
    public sealed class OpisPssSha512 : OpisPodpisu { public OpisPssSha512() : base(HashAlgorithmName.SHA512, true) { } }
}
