using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;

namespace Weryfikator.Logika
{
    internal sealed class ZnacznikCzasu
    {
        public DateTime Czas;
        public X509Certificate2? Cert;
        public X509Certificate2Collection Certy = new X509Certificate2Collection();
        public string OidSkrotu = "";
        public byte[] Odcisk = new byte[0];
    }

    /// <summary>Wynik sprawdzenia jednego podpisującego w strukturze CMS (PAdES, CAdES).</summary>
    internal sealed class WynikCms
    {
        public bool Poprawny;
        public string? Blad;
        public X509Certificate2? Cert;
        public X509Certificate2Collection Certy = new X509Certificate2Collection();
        public DateTime? CzasDeklarowany;
        public ZnacznikCzasu? Znacznik;
        public string? UwagaZnacznika;
        /// <summary>Atrybuty podpisane jako tekst (do wyszukania danych podpisu zaufanego).</summary>
        public string TekstAtrybutow = "";
        /// <summary>true — tekst to surowe bajty (CMS, UTF-8 czytane jako Latin-1); false — zwykły tekst (XML).</summary>
        public bool TekstToBajty = true;
    }

    internal static class Cms
    {
        public const string OidSignedData = "1.2.840.113549.1.7.2";
        public const string OidTstInfo = "1.2.840.113549.1.9.16.1.4";
        private const string OidCzasPodpisu = "1.2.840.113549.1.9.5";
        private const string OidZnacznikPodpisu = "1.2.840.113549.1.9.16.2.14";

        internal sealed class Struktura
        {
            public string TypTresci = "";
            public byte[]? Tresc;
            public List<Der> Podpisujacy = new List<Der>();
        }

        /// <summary>Usuwa dopełnienie zerami (pole /Contents w PDF jest dłuższe niż sam podpis).</summary>
        public static byte[] Przytnij(byte[] d)
        {
            try
            {
                var e = Der.Czytaj(d);
                if (e.Koniec == d.Length) return d;
                var t = new byte[e.Koniec];
                Buffer.BlockCopy(d, 0, t, 0, t.Length);
                return t;
            }
            catch (FormatException) { return d; }
        }

        internal static Struktura? Rozbierz(byte[] cms)
        {
            try
            {
                var ci = Der.Czytaj(cms).Dzieci();
                if (ci.Count < 2 || ci[0].Oid() != OidSignedData) return null;
                var sd = ci[1].Dzieci()[0].Dzieci();
                var s = new Struktura();
                var encap = sd[2].Dzieci();
                s.TypTresci = encap[0].Oid();
                if (encap.Count > 1)
                {
                    var oct = encap[1].Dzieci()[0];
                    s.Tresc = oct.Zlozony ? oct.Dzieci().SelectMany(x => x.Tresc()).ToArray() : oct.Tresc();
                }
                s.Podpisujacy = sd[sd.Count - 1].Dzieci();
                return s;
            }
            catch (Exception) { return null; }
        }

        public static HashAlgorithm? Skrot(string oid)
        {
            switch (oid)
            {
                case "1.3.14.3.2.26": return SHA1.Create();
                case "2.16.840.1.101.3.4.2.1": return SHA256.Create();
                case "2.16.840.1.101.3.4.2.2": return SHA384.Create();
                case "2.16.840.1.101.3.4.2.3": return SHA512.Create();
                default: return null;
            }
        }

        /// <summary>Sprawdza podpisy w strukturze CMS. <paramref name="tresc"/> = podpisane dane, gdy nie ma ich w samej strukturze.</summary>
        public static List<WynikCms> Sprawdz(byte[] cmsBajty, byte[]? tresc)
        {
            cmsBajty = Przytnij(cmsBajty);
            var wyniki = new List<WynikCms>();
            var str = Rozbierz(cmsBajty);
            SignedCms cms;
            try
            {
                bool osobno = str == null ? tresc != null : str.Tresc == null;
                if (osobno && tresc == null)
                {
                    wyniki.Add(new WynikCms { Blad = "Podpis nie zawiera podpisanych danych, a nie znaleziono podpisanego pliku obok." });
                    return wyniki;
                }
                cms = osobno ? new SignedCms(new ContentInfo(tresc!), true) : new SignedCms();
                cms.Decode(cmsBajty);
            }
            catch (Exception e)
            {
                wyniki.Add(new WynikCms { Blad = "Nie można odczytać struktury podpisu: " + e.Message });
                return wyniki;
            }

            int nr = 0;
            foreach (SignerInfo si in cms.SignerInfos)
            {
                var w = new WynikCms { Cert = si.Certificate, Certy = cms.Certificates };
                wyniki.Add(w);
                if (w.Cert == null) w.Blad = "Podpis nie zawiera certyfikatu podpisującego.";
                else
                {
                    try { si.CheckSignature(true); w.Poprawny = true; }
                    catch (Exception e) { w.Blad = "Podpis jest nieprawidłowy — dokument zmieniono po podpisaniu albo podpis jest uszkodzony (" + e.Message.Trim() + ")"; }
                }
                if (str != null && nr < str.Podpisujacy.Count)
                {
                    try { Atrybuty(str.Podpisujacy[nr], w); }
                    catch (Exception e) { w.UwagaZnacznika = "Nie udało się odczytać atrybutów podpisu: " + e.Message; }
                }
                nr++;
            }
            if (wyniki.Count == 0) wyniki.Add(new WynikCms { Blad = "Struktura podpisu nie zawiera żadnego podpisu." });
            return wyniki;
        }

        private static void Atrybuty(Der signerInfo, WynikCms w)
        {
            var el = signerInfo.Dzieci();
            byte[]? podpis = null;
            for (int i = 3; i < el.Count; i++)
            {
                var e = el[i];
                if (e.Kontekst == 0 && podpis == null)
                {
                    w.TekstAtrybutow = System.Text.Encoding.GetEncoding(28591).GetString(e.Bufor, e.PoczatekTresci, e.Dlugosc);
                    foreach (var a in e.Dzieci())
                    {
                        var ad = a.Dzieci();
                        if (ad[0].Oid() == OidCzasPodpisu) w.CzasDeklarowany = ad[1].Dzieci()[0].Czas();
                    }
                }
                else if (e.Znacznik == 0x04) podpis = e.Tresc();
                else if (e.Kontekst == 1 && podpis != null)
                {
                    foreach (var a in e.Dzieci())
                    {
                        var ad = a.Dzieci();
                        if (ad[0].Oid() != OidZnacznikPodpisu) continue;
                        var z = CzytajZnacznik(ad[1].Dzieci()[0].Calosc(), out var blad);
                        if (z == null) { w.UwagaZnacznika = "Znacznik czasu jest nieprawidłowy: " + blad; continue; }
                        using (var h = Skrot(z.OidSkrotu))
                        {
                            if (h == null || !h.ComputeHash(podpis).SequenceEqual(z.Odcisk))
                            { w.UwagaZnacznika = "Znacznik czasu nie dotyczy tego podpisu."; continue; }
                        }
                        if (w.Znacznik == null || z.Czas < w.Znacznik.Czas) w.Znacznik = z;
                    }
                }
            }
        }

        /// <summary>Odczytuje i sprawdza kryptograficznie znacznik czasu RFC 3161 (bez oceny zaufania do wystawcy).</summary>
        public static ZnacznikCzasu? CzytajZnacznik(byte[] token, out string? blad)
        {
            blad = null;
            try
            {
                token = Przytnij(token);
                var cms = new SignedCms();
                cms.Decode(token);
                if (cms.ContentInfo.ContentType.Value != OidTstInfo) { blad = "to nie jest znacznik czasu"; return null; }
                if (cms.SignerInfos.Count != 1) { blad = "nieoczekiwana liczba podpisów"; return null; }
                cms.SignerInfos[0].CheckSignature(true);
                var tst = Der.Czytaj(cms.ContentInfo.Content).Dzieci();
                var odcisk = tst[2].Dzieci();
                var z = new ZnacznikCzasu
                {
                    Cert = cms.SignerInfos[0].Certificate,
                    Certy = cms.Certificates,
                    OidSkrotu = odcisk[0].Dzieci()[0].Oid(),
                    Odcisk = odcisk[1].Tresc(),
                };
                foreach (var e in tst)
                    if (e.Znacznik == 0x18) { z.Czas = e.Czas(); break; }
                if (z.Cert == null || z.Czas == default(DateTime)) { blad = "niepełne dane znacznika"; return null; }
                return z;
            }
            catch (Exception e) { blad = e.Message; return null; }
        }
    }
}
