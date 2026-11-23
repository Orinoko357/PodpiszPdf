using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

    /// <summary>
    /// Podpis XAdES z odwołaniami do plików leżących obok (detached). SignedXml ich nie obsługuje (w .NET Framework
    /// wyłączone ze względów bezpieczeństwa, w .NET usunięte), więc: wartość podpisu nad SignedInfo i skróty plików
    /// liczone są tutaj, a skróty odwołań wewnętrznych przelicza SignedXml na kopii dokumentu.
    /// </summary>
    internal static class PodpisXmlZPlikami
    {
        public static bool MaOdwolaniaDoPlikow(SignedXml sx)
        {
            foreach (Reference r in sx.SignedInfo.References) if (Zewnetrzne(r.Uri)) return true;
            return false;
        }

        private static bool Zewnetrzne(string? uri) => !string.IsNullOrEmpty(uri) && uri![0] != '#';

        private static HashAlgorithm? Skrot(string? uri)
        {
            uri = (uri ?? "").ToLowerInvariant();
            if (uri.EndsWith("sha1")) return SHA1.Create();
            if (uri.EndsWith("sha256")) return SHA256.Create();
            if (uri.EndsWith("sha384")) return SHA384.Create();
            if (uri.EndsWith("sha512")) return SHA512.Create();
            return null;
        }

        internal static byte[] Kanonicznie(XmlElement e, string? metoda)
        {
            bool komentarze = metoda != null && metoda.EndsWith("#WithComments");
            Transform tr = metoda != null && metoda.Contains("xml-exc-c14n") ? (Transform)new XmlDsigExcC14NTransform(komentarze) : new XmlDsigC14NTransform(komentarze);
            // Osobny dokument z samym elementem (deklaracje przestrzeni nazw przodków przenosi OuterXml) — tak jak robi to SignedXml.
            var d = new XmlDocument { PreserveWhitespace = true };
            d.LoadXml(e.OuterXml);
            tr.LoadInput(d);
            using var s = (Stream)tr.GetOutput(typeof(Stream));
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            return ms.ToArray();
        }

        public static X509Certificate2? Sprawdz(XmlDocument doc, XmlElement podpis, SignedXml sx, List<X509Certificate2> certy, string folder,
            Func<XmlDocument, SignedXml> nowy, out string? blad)
        {
            blad = "Podpis XML jest nieprawidłowy (treść zmieniona po podpisaniu albo uszkodzony plik).";
            XmlElement? signedInfo = null;
            foreach (XmlNode n in podpis.ChildNodes) if (n is XmlElement x && x.LocalName == "SignedInfo" && x.NamespaceURI == PodpisXml.NsDs) signedInfo = x;
            if (signedInfo == null) return null;

            // 1. Wartość podpisu nad SignedInfo.
            var metoda = (sx.SignedInfo.SignatureMethod ?? "").ToLowerInvariant();
            using var skrot = Skrot(metoda.Replace("-rsa-mgf1", ""));
            if (skrot == null) { blad = "Nieobsługiwany algorytm podpisu: " + sx.SignedInfo.SignatureMethod; return null; }
            var nazwa = skrot is SHA1 ? HashAlgorithmName.SHA1 : skrot is SHA384 ? HashAlgorithmName.SHA384 : skrot is SHA512 ? HashAlgorithmName.SHA512 : HashAlgorithmName.SHA256;
            var hash = skrot.ComputeHash(Kanonicznie(signedInfo, sx.SignedInfo.CanonicalizationMethod));
            X509Certificate2? podpisal = null;
            foreach (var c in certy)
            {
                try
                {
                    if (metoda.Contains("ecdsa")) { using var ec = c.GetECDsaPublicKey(); if (ec != null && ec.VerifyHash(hash, sx.SignatureValue)) podpisal = c; }
                    else
                    {
                        using var rsa = c.GetRSAPublicKey();
                        if (rsa != null && rsa.VerifyHash(hash, sx.SignatureValue, nazwa, metoda.Contains("mgf1") ? RSASignaturePadding.Pss : RSASignaturePadding.Pkcs1)) podpisal = c;
                    }
                }
                catch (CryptographicException) { }
                if (podpisal != null) break;
            }
            if (podpisal == null) return null;

            // 2. Skróty plików obok.
            var folderPelny = Path.GetFullPath(folder);
            foreach (Reference r in sx.SignedInfo.References)
            {
                if (!Zewnetrzne(r.Uri)) continue;
                var plik = Uri.UnescapeDataString(r.Uri);
                if (plik != Path.GetFileName(plik) || r.TransformChain.Count > 0) { blad = "Nieobsługiwane odwołanie w podpisie: " + r.Uri; return null; }
                var sciezka = Path.Combine(folderPelny, plik);
                if (!File.Exists(sciezka)) { blad = "Nie znaleziono podpisanego pliku „" + plik + "” (powinien leżeć obok pliku podpisu)."; return null; }
                using var h = Skrot(r.DigestMethod);
                if (h == null) { blad = "Nieobsługiwany algorytm skrótu: " + r.DigestMethod; return null; }
                using var fs = new FileStream(sciezka, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (!h.ComputeHash(fs).SequenceEqual(r.DigestValue)) { blad = "Plik „" + plik + "” został zmieniony po podpisaniu."; return null; }
            }

            // 3. Odwołania wewnętrzne: SignedXml przelicza skróty na kopii bez odwołań do plików; porównanie z podpisanymi.
            var kopia = (XmlDocument)doc.CloneNode(true);
            int nr = doc.GetElementsByTagName("Signature", PodpisXml.NsDs).Cast<XmlElement>().ToList().IndexOf(podpis);
            var podpis2 = (XmlElement)kopia.GetElementsByTagName("Signature", PodpisXml.NsDs)[nr]!;
            var oczekiwane = new List<byte[]>();
            foreach (XmlElement re in podpis2.GetElementsByTagName("Reference", PodpisXml.NsDs).Cast<XmlElement>().ToList())
            {
                if (re.ParentNode == null || re.ParentNode.LocalName != "SignedInfo" || re.ParentNode.ParentNode != podpis2) continue;
                if (Zewnetrzne(re.GetAttribute("URI"))) re.ParentNode.RemoveChild(re);
            }
            var sx2 = nowy(kopia);
            sx2.LoadXml(podpis2);
            foreach (Reference r in sx2.SignedInfo.References) oczekiwane.Add(r.DigestValue);
            if (oczekiwane.Count > 0)
            {
                using var hmac = new HMACSHA256(new byte[32]);
                sx2.ComputeSignature(hmac);
                int i = 0;
                foreach (Reference r in sx2.SignedInfo.References)
                    if (!r.DigestValue.SequenceEqual(oczekiwane[i++])) return null;
            }
            blad = null;
            return podpisal;
        }
    }

    /// <summary>Resolver dla SignedXml — udostępnia wyłącznie pliki z folderu podpisu.</summary>
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
