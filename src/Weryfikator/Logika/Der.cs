using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Weryfikator.Logika
{
    /// <summary>Minimalny czytnik DER (ASN.1) — tylko długości określone; wystarcza do CMS, nazw X.500 i znaczników czasu.</summary>
    internal sealed class Der
    {
        public byte[] Bufor = null!;
        public int Znacznik;
        public int Poczatek;
        public int PoczatekTresci;
        public int Dlugosc;

        public int Koniec => PoczatekTresci + Dlugosc;
        public bool Zlozony => (Znacznik & 0x20) != 0;
        /// <summary>Numer znacznika kontekstowego ([0], [1]…) albo -1.</summary>
        public int Kontekst => (Znacznik & 0xC0) == 0x80 ? Znacznik & 0x1F : -1;

        public static Der Czytaj(byte[] d, int poz = 0, int limit = -1)
        {
            if (limit < 0) limit = d.Length;
            if (poz + 2 > limit) throw new FormatException("DER: za krótkie dane");
            var e = new Der { Bufor = d, Poczatek = poz, Znacznik = d[poz] };
            if ((d[poz] & 0x1F) == 0x1F) throw new FormatException("DER: znacznik wielobajtowy");
            int b = d[poz + 1];
            int p = poz + 2;
            if (b < 0x80) e.Dlugosc = b;
            else
            {
                int n = b & 0x7F;
                if (n == 0 || n > 4 || p + n > limit) throw new FormatException("DER: nieobsługiwana długość");
                long dl = 0;
                for (int i = 0; i < n; i++) dl = (dl << 8) | d[p + i];
                if (dl > int.MaxValue) throw new FormatException("DER: za długi element");
                e.Dlugosc = (int)dl;
                p += n;
            }
            e.PoczatekTresci = p;
            if (e.Koniec > limit || e.Koniec < p) throw new FormatException("DER: element wychodzi poza dane");
            return e;
        }

        public List<Der> Dzieci()
        {
            var l = new List<Der>();
            int p = PoczatekTresci;
            while (p < Koniec)
            {
                var e = Czytaj(Bufor, p, Koniec);
                l.Add(e);
                p = e.Koniec;
            }
            return l;
        }

        public byte[] Tresc()
        {
            var t = new byte[Dlugosc];
            Buffer.BlockCopy(Bufor, PoczatekTresci, t, 0, Dlugosc);
            return t;
        }

        public byte[] Calosc()
        {
            var t = new byte[Koniec - Poczatek];
            Buffer.BlockCopy(Bufor, Poczatek, t, 0, t.Length);
            return t;
        }

        public string Oid()
        {
            if (Znacznik != 0x06 || Dlugosc == 0) throw new FormatException("DER: oczekiwano OID");
            var sb = new StringBuilder();
            long w = 0;
            bool pierwszy = true;
            for (int i = PoczatekTresci; i < Koniec; i++)
            {
                w = (w << 7) | (long)(Bufor[i] & 0x7F);
                if ((Bufor[i] & 0x80) != 0) continue;
                if (pierwszy)
                {
                    long a = w < 80 ? w / 40 : 2;
                    sb.Append(a).Append('.').Append(w - a * 40);
                    pierwszy = false;
                }
                else sb.Append('.').Append(w);
                w = 0;
            }
            return sb.ToString();
        }

        public string Tekst()
        {
            switch (Znacznik)
            {
                case 0x0C: return Encoding.UTF8.GetString(Bufor, PoczatekTresci, Dlugosc);
                case 0x1E: return Encoding.BigEndianUnicode.GetString(Bufor, PoczatekTresci, Dlugosc);
                default: return Encoding.GetEncoding(28591).GetString(Bufor, PoczatekTresci, Dlugosc);
            }
        }

        /// <summary>UTCTime albo GeneralizedTime jako czas UTC.</summary>
        public DateTime Czas()
        {
            var s = Encoding.ASCII.GetString(Bufor, PoczatekTresci, Dlugosc);
            if (Znacznik == 0x17)
            {
                var d = DateTime.ParseExact(s.Substring(0, 12), "yyMMddHHmmss", CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
                int rok = d.Year % 100;
                rok += rok >= 50 ? 1900 : 2000;
                return new DateTime(rok, d.Month, d.Day, d.Hour, d.Minute, d.Second, DateTimeKind.Utc);
            }
            if (Znacznik == 0x18)
                return DateTime.ParseExact(s.Substring(0, 14), "yyyyMMddHHmmss", CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
            throw new FormatException("DER: oczekiwano czasu");
        }
    }

    internal static class Hex
    {
        public static string Z(byte[] b) => BitConverter.ToString(b).Replace("-", "");
    }
}
