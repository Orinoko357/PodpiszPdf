using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace Weryfikator.Logika
{
    /// <summary>Minimalny zapis PDF: strony z tekstem (osadzone czcionki TrueType, Unicode), prostokątami i liniami.</summary>
    internal sealed class Pdf
    {
        public readonly double Szerokosc, Wysokosc;
        private readonly List<StringBuilder> _strony = new List<StringBuilder>();
        private readonly CzcionkaTtf[] _czcionki;
        private StringBuilder _s = null!;

        public Pdf(double szerokosc, double wysokosc, params CzcionkaTtf[] czcionki)
        {
            Szerokosc = szerokosc; Wysokosc = wysokosc; _czcionki = czcionki;
            NowaStrona();
        }

        public int LiczbaStron => _strony.Count;
        public void NowaStrona() { _s = new StringBuilder(); _strony.Add(_s); }
        public void Strona(int nr) { _s = _strony[nr]; }

        private static string L(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
        private static string Kolor(int rgb) => $"{L(((rgb >> 16) & 255) / 255.0)} {L(((rgb >> 8) & 255) / 255.0)} {L((rgb & 255) / 255.0)}";

        public double SzerokoscTekstu(string tekst, int czcionka, double rozmiar) => _czcionki[czcionka].SzerokoscTekstu(tekst, rozmiar);

        /// <summary>Tekst; (x, y) to lewy koniec linii bazowej liczony od góry strony.</summary>
        public void Tekst(double x, double y, string tekst, int czcionka, double rozmiar, int rgb = 0)
        {
            if (tekst.Length == 0) return;
            var c = _czcionki[czcionka];
            var hex = new StringBuilder(tekst.Length * 4);
            foreach (var z in tekst) hex.Append(c.Glif(z).ToString("X4"));
            _s.Append($"BT /F{czcionka} {L(rozmiar)} Tf {Kolor(rgb)} rg {L(x)} {L(Wysokosc - y)} Td <{hex}> Tj ET\n");
        }

        public void Prostokat(double x, double y, double szer, double wys, int rgb) =>
            _s.Append($"{Kolor(rgb)} rg {L(x)} {L(Wysokosc - y - wys)} {L(szer)} {L(wys)} re f\n");

        public void Linia(double x1, double y1, double x2, double y2, int rgb, double grubosc = 0.5) =>
            _s.Append($"{Kolor(rgb)} RG {L(grubosc)} w {L(x1)} {L(Wysokosc - y1)} m {L(x2)} {L(Wysokosc - y2)} l S\n");

        /// <summary>Podział tekstu na wiersze mieszczące się w szerokości (długie wyrazy, np. nazwy plików, są łamane).</summary>
        public List<string> Zawin(string tekst, int czcionka, double rozmiar, double szer)
        {
            var wiersze = new List<string>();
            foreach (var akapit in tekst.Replace("\r", "").Split('\n'))
            {
                var biezacy = "";
                foreach (var wyraz0 in akapit.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var wyraz = wyraz0;
                    while (true)
                    {
                        var proba = biezacy.Length == 0 ? wyraz : biezacy + " " + wyraz;
                        if (SzerokoscTekstu(proba, czcionka, rozmiar) <= szer) { biezacy = proba; break; }
                        if (biezacy.Length > 0 && SzerokoscTekstu(wyraz, czcionka, rozmiar) <= szer) { wiersze.Add(biezacy); biezacy = wyraz; break; }
                        // Wyraz dłuższy niż cała szerokość: tyle znaków, ile się zmieści.
                        var pocz = biezacy.Length == 0 ? "" : biezacy + " ";
                        int n = 0;
                        while (n < wyraz.Length && SzerokoscTekstu(pocz + wyraz.Substring(0, n + 1), czcionka, rozmiar) <= szer) n++;
                        if (n == 0 && pocz.Length == 0) n = 1;
                        if (n == 0) { wiersze.Add(biezacy); biezacy = ""; continue; }
                        wiersze.Add(pocz + wyraz.Substring(0, n));
                        biezacy = "";
                        wyraz = wyraz.Substring(n);
                        if (wyraz.Length == 0) break;
                    }
                }
                wiersze.Add(biezacy);
            }
            while (wiersze.Count > 1 && wiersze[wiersze.Count - 1].Length == 0) wiersze.RemoveAt(wiersze.Count - 1);
            return wiersze;
        }

        public void Zapisz(string sciezka, string tytul)
        {
            var ms = new MemoryStream();
            var pozycje = new List<long> { 0 };
            var lat = Encoding.GetEncoding(28591);
            void Pisz(string t) { var b = lat.GetBytes(t); ms.Write(b, 0, b.Length); }
            int Obiekt(string tresc, byte[]? strumien = null)
            {
                pozycje.Add(ms.Position);
                Pisz($"{pozycje.Count - 1} 0 obj\n{tresc}\n");
                if (strumien != null) { Pisz("stream\n"); ms.Write(strumien, 0, strumien.Length); Pisz("\nendstream\n"); }
                Pisz("endobj\n");
                return pozycje.Count - 1;
            }
            int Strumien(string slownik, byte[] dane)
            {
                var z = Zlib(dane);
                return Obiekt($"<< {slownik} /Filter /FlateDecode /Length {z.Length} >>", z);
            }

            ms.Write(new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x37, 0x0A, 0x25, 0xE2, 0xE3, 0xCF, 0xD3, 0x0A }, 0, 15);
            // Numery obiektów: 1 — katalog, 2 — drzewo stron (zapisywane na końcu, gdy znane są strony).
            pozycje.Add(0); pozycje.Add(0);

            var zasoby = new StringBuilder("<< /Font <<");
            for (int i = 0; i < _czcionki.Length; i++)
            {
                var c = _czcionki[i];
                var nazwa = "WERYF" + (char)('A' + i) + "+Czcionka" + i;
                var plik = c.Podzbior();
                int nrPliku = Strumien($"/Length1 {plik.Length}", plik);
                double sk = 1000.0 / c.JednostkiNaEm;
                int opis = Obiekt($"<< /Type /FontDescriptor /FontName /{nazwa} /Flags 4 /FontBBox [{L(c.XMin * sk)} {L(c.YMin * sk)} {L(c.XMax * sk)} {L(c.YMax * sk)}] " +
                                  $"/ItalicAngle 0 /Ascent {L(c.Wznios * sk)} /Descent {L(c.Opad * sk)} /CapHeight {L(c.Wznios * sk * 0.8)} /StemV 80 /FontFile2 {nrPliku} 0 R >>");
                var w = new StringBuilder();
                var mapa = new StringBuilder("/CIDInit /ProcSet findresource begin 12 dict begin begincmap /CIDSystemInfo << /Registry (Adobe) /Ordering (UCS) /Supplement 0 >> def " +
                                             "/CMapName /Adobe-Identity-UCS def /CMapType 2 def 1 begincodespacerange <0000> <FFFF> endcodespacerange\n");
                var uzyte = c.Uzyte.ToList();
                foreach (var g in uzyte) w.Append($"{g.Key} [{c.Szerokosc(g.Key)}] ");
                for (int od = 0; od < uzyte.Count; od += 100)
                {
                    var czesc = uzyte.Skip(od).Take(100).ToList();
                    mapa.Append($"{czesc.Count} beginbfchar\n");
                    foreach (var g in czesc) mapa.Append($"<{g.Key:X4}> <{g.Value:X4}>\n");
                    mapa.Append("endbfchar\n");
                }
                mapa.Append("endcmap CMapName currentdict /CMap defineresource pop end end");
                int cid = Obiekt($"<< /Type /Font /Subtype /CIDFontType2 /BaseFont /{nazwa} /CIDSystemInfo << /Registry (Adobe) /Ordering (Identity) /Supplement 0 >> " +
                                 $"/FontDescriptor {opis} 0 R /DW 1000 /W [{w}] /CIDToGIDMap /Identity >>");
                int naUnicode = Strumien("", lat.GetBytes(mapa.ToString()));
                int font = Obiekt($"<< /Type /Font /Subtype /Type0 /BaseFont /{nazwa} /Encoding /Identity-H /DescendantFonts [{cid} 0 R] /ToUnicode {naUnicode} 0 R >>");
                zasoby.Append($" /F{i} {font} 0 R");
            }
            zasoby.Append(" >> >>");
            int nrZasobow = Obiekt(zasoby.ToString());

            var strony = new List<int>();
            foreach (var s in _strony)
            {
                int tresc = Strumien("", lat.GetBytes(s.ToString()));
                strony.Add(Obiekt($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {L(Szerokosc)} {L(Wysokosc)}] /Resources {nrZasobow} 0 R /Contents {tresc} 0 R >>"));
            }
            var hexTytul = "FEFF" + string.Concat(tytul.Select(z => ((int)z).ToString("X4")));
            int info = Obiekt($"<< /Title <{hexTytul}> /Producer (Weryfikator) /CreationDate (D:{DateTime.UtcNow:yyyyMMddHHmmss}Z) >>");

            pozycje[1] = ms.Position;
            Pisz("1 0 obj\n<< /Type /Catalog /Pages 2 0 R /Lang (pl-PL) >>\nendobj\n");
            pozycje[2] = ms.Position;
            Pisz($"2 0 obj\n<< /Type /Pages /Count {strony.Count} /Kids [{string.Join(" ", strony.Select(n => n + " 0 R"))}] >>\nendobj\n");

            long xref = ms.Position;
            Pisz($"xref\n0 {pozycje.Count}\n0000000000 65535 f \n");
            for (int i = 1; i < pozycje.Count; i++) Pisz(pozycje[i].ToString("0000000000") + " 00000 n \n");
            Pisz($"trailer\n<< /Size {pozycje.Count} /Root 1 0 R /Info {info} 0 R >>\nstartxref\n{xref}\n%%EOF\n");
            File.WriteAllBytes(sciezka, ms.ToArray());
        }

        private static byte[] Zlib(byte[] dane)
        {
            var ms = new MemoryStream();
            ms.WriteByte(0x78); ms.WriteByte(0x9C);
            using (var d = new DeflateStream(ms, CompressionLevel.Optimal, true)) d.Write(dane, 0, dane.Length);
            uint a = 1, b = 0;
            foreach (var x in dane) { a = (a + x) % 65521; b = (b + a) % 65521; }
            uint adler = (b << 16) | a;
            ms.WriteByte((byte)(adler >> 24)); ms.WriteByte((byte)(adler >> 16)); ms.WriteByte((byte)(adler >> 8)); ms.WriteByte((byte)adler);
            return ms.ToArray();
        }
    }
}
