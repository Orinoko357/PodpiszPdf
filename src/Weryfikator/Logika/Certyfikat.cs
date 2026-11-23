using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Weryfikator.Logika
{
    /// <summary>Dane odczytane z certyfikatu podpisującego.</summary>
    internal sealed class DaneCertyfikatu
    {
        public string Nazwa = "";
        public string? NumerPesel;
        public string NumerSeryjnyPodmiotu = "";
        public string Organizacja = "";
        public string Wystawca = "";
        public bool Kwalifikowany;
        public bool Pieczec;
        public bool WygladaNaZaufany;
        public bool WygladaNaOsobisty;

        public static DaneCertyfikatu Z(X509Certificate2 c)
        {
            var d = new DaneCertyfikatu();
            var podmiot = Atrybuty(c.SubjectName);
            var wystawca = Atrybuty(c.IssuerName);
            string cn = Pierwszy(podmiot, "2.5.4.3"), imie = Pierwszy(podmiot, "2.5.4.42"), nazwisko = Pierwszy(podmiot, "2.5.4.4");
            d.Nazwa = imie != "" && nazwisko != "" ? imie + " " + nazwisko : cn;
            d.Organizacja = Pierwszy(podmiot, "2.5.4.10");
            d.NumerSeryjnyPodmiotu = Pierwszy(podmiot, "2.5.4.5");
            d.Wystawca = Pierwszy(wystawca, "2.5.4.3");
            if (d.Wystawca == "") d.Wystawca = Pierwszy(wystawca, "2.5.4.10");

            // PESEL: „PNOPL-…” (ETSI EN 319 412-1), dawniej „PESEL: …”; czasem sam numer.
            foreach (var sn in podmiot.Where(a => a.Key == "2.5.4.5").Select(a => a.Value))
            {
                d.NumerPesel = Pesel.ZEtykiety(sn) ?? (Pesel.Poprawny(sn.Trim()) ? sn.Trim() : null);
                if (d.NumerPesel != null) break;
            }

            try
            {
                var qc = c.Extensions["1.3.6.1.5.5.7.1.3"];
                if (qc != null)
                {
                    var oidy = new List<string>();
                    Oidy(Der.Czytaj(qc.RawData), oidy);
                    d.Kwalifikowany = oidy.Contains("0.4.0.1862.1.1");
                    d.Pieczec = oidy.Contains("0.4.0.1862.1.6.2");
                }
            }
            catch (FormatException) { }

            var p = Uprosc(cn + " " + d.Organizacja);
            d.WygladaNaZaufany = p.Contains("podpisu zaufanego") || p.Contains("podpis zaufany") || p.Contains("profil zaufany") || p.Contains("profilu zaufanego");
            var w = Uprosc(string.Join(" ", wystawca.Select(a => a.Value)));
            d.WygladaNaOsobisty = w.Contains("pl.id") || w.Contains("e-dowod") || w.Contains("edowod") ||
                                  w.Contains("spraw wewnetrznych") || w.Contains("centrum personalizacji");
            return d;
        }

        private static void Oidy(Der e, List<string> l)
        {
            if (e.Znacznik == 0x06) l.Add(e.Oid());
            else if (e.Zlozony) foreach (var x in e.Dzieci()) Oidy(x, l);
        }

        private static string Pierwszy(List<KeyValuePair<string, string>> a, string oid) =>
            a.Where(x => x.Key == oid).Select(x => x.Value).FirstOrDefault() ?? "";

        internal static List<KeyValuePair<string, string>> Atrybuty(X500DistinguishedName nazwa)
        {
            var l = new List<KeyValuePair<string, string>>();
            try
            {
                foreach (var rdn in Der.Czytaj(nazwa.RawData).Dzieci())
                    foreach (var atv in rdn.Dzieci())
                    {
                        var p = atv.Dzieci();
                        l.Add(new KeyValuePair<string, string>(p[0].Oid(), p[1].Tekst()));
                    }
            }
            catch (FormatException) { }
            return l;
        }

        /// <summary>Małe litery bez polskich znaków — do porównań nazw.</summary>
        internal static string Uprosc(string s)
        {
            var sb = new StringBuilder();
            foreach (var z in s.ToLowerInvariant().Replace('ł', 'l').Normalize(NormalizationForm.FormD))
                if (CharUnicodeInfo.GetUnicodeCategory(z) != UnicodeCategory.NonSpacingMark) sb.Append(z);
            return sb.ToString();
        }
    }
}
