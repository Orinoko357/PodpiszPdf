using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Weryfikator.Logika
{
    /// <summary>
    /// Czcionka TrueType do osadzenia w PDF: odwzorowanie znaków na glify, szerokości i podzbiór (tylko użyte glify),
    /// żeby raport był mały. Obsługuje zwykłe pliki .ttf (kontury w tabeli glyf).
    /// </summary>
    internal sealed class CzcionkaTtf
    {
        private readonly byte[] _d;
        private readonly Dictionary<string, KeyValuePair<int, int>> _tabele = new Dictionary<string, KeyValuePair<int, int>>();
        private readonly Dictionary<int, int> _cmap = new Dictionary<int, int>();
        private readonly int _liczbaGlifow, _liczbaMetryk, _formatLoca;
        public readonly int JednostkiNaEm, Wznios, Opad, XMin, YMin, XMax, YMax;
        public readonly SortedDictionary<int, int> Uzyte = new SortedDictionary<int, int>();   // glif → znak Unicode

        public CzcionkaTtf(byte[] dane)
        {
            _d = dane;
            if (U32(0) != 0x00010000 && U32(0) != 0x74727565) throw new InvalidDataException("Nieobsługiwany format czcionki (wymagany TrueType .ttf).");
            int n = U16(4);
            for (int i = 0; i < n; i++)
            {
                int p = 12 + i * 16;
                _tabele[System.Text.Encoding.ASCII.GetString(_d, p, 4)] = new KeyValuePair<int, int>((int)U32(p + 8), (int)U32(p + 12));
            }
            foreach (var t in new[] { "head", "hhea", "maxp", "hmtx", "cmap", "loca", "glyf" })
                if (!_tabele.ContainsKey(t)) throw new InvalidDataException("Czcionka bez tabeli " + t + ".");
            int head = _tabele["head"].Key, hhea = _tabele["hhea"].Key;
            JednostkiNaEm = U16(head + 18);
            XMin = S16(head + 36); YMin = S16(head + 38); XMax = S16(head + 40); YMax = S16(head + 42);
            _formatLoca = S16(head + 50);
            Wznios = S16(hhea + 4); Opad = S16(hhea + 6);
            _liczbaMetryk = U16(hhea + 34);
            _liczbaGlifow = U16(_tabele["maxp"].Key + 4);
            CzytajCmap();
        }

        private int U16(int p) => (_d[p] << 8) | _d[p + 1];
        private int S16(int p) => (short)U16(p);
        private uint U32(int p) => ((uint)_d[p] << 24) | ((uint)_d[p + 1] << 16) | ((uint)_d[p + 2] << 8) | _d[p + 3];

        private void CzytajCmap()
        {
            int cmap = _tabele["cmap"].Key, n = U16(cmap + 2), f4 = -1, f12 = -1;
            for (int i = 0; i < n; i++)
            {
                int plat = U16(cmap + 4 + i * 8), kod = U16(cmap + 6 + i * 8), p = cmap + (int)U32(cmap + 8 + i * 8);
                if (plat != 3 && plat != 0) continue;
                int format = U16(p);
                if (format == 12 && (plat == 0 || kod == 10)) f12 = p;
                else if (format == 4 && (plat == 0 || kod == 1) && f4 < 0) f4 = p;
            }
            if (f12 >= 0)
            {
                int grup = (int)U32(f12 + 12);
                for (int g = 0; g < grup; g++)
                {
                    int p = f12 + 16 + g * 12;
                    uint od = U32(p), doKodu = U32(p + 4), glif = U32(p + 8);
                    for (uint c = od; c <= doKodu && c <= 0xFFFF; c++) _cmap[(int)c] = (int)(glif + (c - od));
                }
            }
            else if (f4 >= 0)
            {
                int seg = U16(f4 + 6) / 2, konce = f4 + 14, pocz = konce + seg * 2 + 2, delty = pocz + seg * 2, przes = delty + seg * 2;
                for (int s = 0; s < seg; s++)
                {
                    int koniec = U16(konce + s * 2), start = U16(pocz + s * 2), delta = U16(delty + s * 2), ro = U16(przes + s * 2);
                    for (int c = start; c <= koniec && c < 0xFFFF; c++)
                    {
                        int g;
                        if (ro == 0) g = (c + delta) & 0xFFFF;
                        else
                        {
                            int p = przes + s * 2 + ro + (c - start) * 2;
                            if (p + 1 >= _d.Length) continue;
                            g = U16(p);
                            if (g != 0) g = (g + delta) & 0xFFFF;
                        }
                        if (g != 0) _cmap[c] = g;
                    }
                }
            }
            else throw new InvalidDataException("Czcionka bez tablicy znaków Unicode.");
        }

        /// <summary>Numer glifu znaku (0 — brak w czcionce); zapamiętuje użycie.</summary>
        public int Glif(char z)
        {
            int kod = z == ' ' ? ' ' : z;
            if (!_cmap.TryGetValue(kod, out var g) && !_cmap.TryGetValue('?', out g)) g = 0;
            if (!Uzyte.ContainsKey(g)) Uzyte[g] = kod;
            return g;
        }

        /// <summary>Szerokość glifu w tysięcznych częściach rozmiaru czcionki.</summary>
        public int Szerokosc(int glif)
        {
            int hmtx = _tabele["hmtx"].Key;
            int w = U16(hmtx + Math.Min(glif, _liczbaMetryk - 1) * 4);
            return w * 1000 / JednostkiNaEm;
        }

        public double SzerokoscTekstu(string tekst, double rozmiar)
        {
            long suma = 0;
            foreach (var z in tekst) suma += Szerokosc(Glif(z));
            return suma * rozmiar / 1000.0;
        }

        private void Zakres(int glif, out int od, out int dl)
        {
            int loca = _tabele["loca"].Key;
            int a, b;
            if (_formatLoca == 0) { a = U16(loca + glif * 2) * 2; b = U16(loca + glif * 2 + 2) * 2; }
            else { a = (int)U32(loca + glif * 4); b = (int)U32(loca + glif * 4 + 4); }
            od = _tabele["glyf"].Key + a; dl = Math.Max(0, b - a);
        }

        /// <summary>Plik czcionki zawierający tylko użyte glify (numeracja glifów bez zmian).</summary>
        public byte[] Podzbior()
        {
            var potrzebne = new HashSet<int> { 0 };
            var kolejka = new Queue<int>(Uzyte.Keys);
            while (kolejka.Count > 0)
            {
                int g = kolejka.Dequeue();
                if (g >= _liczbaGlifow || !potrzebne.Add(g) && g != 0) continue;
                Zakres(g, out var od, out var dl);
                if (dl < 10 || S16(od) >= 0) continue;
                // Glif złożony: potrzebne są też jego składniki.
                int p = od + 10;
                while (p + 4 <= od + dl)
                {
                    int flagi = U16(p), skladnik = U16(p + 2);
                    kolejka.Enqueue(skladnik);
                    p += 4 + ((flagi & 1) != 0 ? 4 : 2);
                    if ((flagi & 8) != 0) p += 2; else if ((flagi & 0x40) != 0) p += 4; else if ((flagi & 0x80) != 0) p += 8;
                    if ((flagi & 0x20) == 0) break;
                }
            }

            var glyf = new MemoryStream();
            var loca = new byte[(_liczbaGlifow + 1) * 4];
            for (int g = 0; g < _liczbaGlifow; g++)
            {
                Zapisz32(loca, g * 4, (uint)glyf.Length);
                if (!potrzebne.Contains(g)) continue;
                Zakres(g, out var od, out var dl);
                glyf.Write(_d, od, dl);
                while (glyf.Length % 4 != 0) glyf.WriteByte(0);
            }
            Zapisz32(loca, _liczbaGlifow * 4, (uint)glyf.Length);

            var tabele = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (var t in new[] { "head", "hhea", "maxp", "hmtx", "cvt ", "fpgm", "prep" })
                if (_tabele.TryGetValue(t, out var z)) { var b = new byte[z.Value]; Buffer.BlockCopy(_d, z.Key, b, 0, z.Value); tabele[t] = b; }
            tabele["glyf"] = glyf.ToArray();
            tabele["loca"] = loca;
            var head = tabele["head"];
            head[8] = head[9] = head[10] = head[11] = 0;   // checkSumAdjustment
            head[50] = 0; head[51] = 1;                     // loca w formacie długim

            int n = tabele.Count, wykl = 0;
            while ((2 << wykl) <= n) wykl++;
            var wynik = new MemoryStream();
            void W16(int v) { wynik.WriteByte((byte)(v >> 8)); wynik.WriteByte((byte)v); }
            void W32(uint v) { W16((int)(v >> 16)); W16((int)(v & 0xFFFF)); }
            W32(0x00010000); W16(n); W16((1 << wykl) * 16); W16(wykl); W16(n * 16 - (1 << wykl) * 16);
            int poz = 12 + n * 16, pozHead = 0;
            foreach (var t in tabele)
            {
                wynik.Write(System.Text.Encoding.ASCII.GetBytes(t.Key), 0, 4);
                W32(Suma(t.Value)); W32((uint)poz); W32((uint)t.Value.Length);
                if (t.Key == "head") pozHead = poz;
                poz += (t.Value.Length + 3) & ~3;
            }
            foreach (var t in tabele)
            {
                wynik.Write(t.Value, 0, t.Value.Length);
                while (wynik.Length % 4 != 0) wynik.WriteByte(0);
            }
            var plik = wynik.ToArray();
            Zapisz32(plik, pozHead + 8, 0xB1B0AFBA - Suma(plik));
            return plik;
        }

        private static void Zapisz32(byte[] b, int p, uint v) { b[p] = (byte)(v >> 24); b[p + 1] = (byte)(v >> 16); b[p + 2] = (byte)(v >> 8); b[p + 3] = (byte)v; }

        private static uint Suma(byte[] b)
        {
            uint s = 0;
            for (int i = 0; i < b.Length; i += 4)
                s += ((uint)b[i] << 24) | ((uint)(i + 1 < b.Length ? b[i + 1] : 0) << 16) | ((uint)(i + 2 < b.Length ? b[i + 2] : 0) << 8) | (i + 3 < b.Length ? b[i + 3] : (uint)0);
            return s;
        }
    }
}
