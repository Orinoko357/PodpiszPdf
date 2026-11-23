using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace Weryfikator.Logika
{
    /// <summary>Komórka arkusza: tekst do pokazania i informacja, czy w pliku była liczbą.</summary>
    internal struct Komorka
    {
        public string Tekst;
        public bool Liczba;
    }

    internal sealed class Arkusz
    {
        public string Nazwa = "";
        public List<List<Komorka>> Wiersze = new List<List<Komorka>>();
        public int LiczbaKolumn => Wiersze.Count == 0 ? 0 : Wiersze.Max(w => w.Count);
    }

    /// <summary>
    /// Lista numerów PESEL z arkusza .xlsx albo z pliku tekstowego/CSV. Domyślnie numery są szukane we wszystkich
    /// komórkach wszystkich arkuszy; <see cref="Wybierz"/> ogranicza je do jednego arkusza i/lub jednej kolumny.
    /// </summary>
    internal sealed class ListaPesel
    {
        public HashSet<string> Numery { get; private set; } = new HashSet<string>();
        /// <summary>Wartości 11-cyfrowe z błędną sumą kontrolną (zapewne literówki na liście).</summary>
        public List<string> Bledne { get; private set; } = new List<string>();
        public List<Arkusz> Arkusze { get; } = new List<Arkusz>();
        public string Plik { get; private set; } = "";
        /// <summary>Wybrany arkusz i kolumna (od 0); -1 = wszystkie.</summary>
        public int WybranyArkusz { get; private set; } = -1;
        public int WybranaKolumna { get; private set; } = -1;

        public bool Zawiera(string pesel) => Numery.Contains(pesel);

        private static readonly Regex Cyfry11 = new Regex(@"(?<!\d)\d{11}(?!\d)");

        public static ListaPesel Wczytaj(string sciezka)
        {
            var l = new ListaPesel { Plik = sciezka };
            var roz = Path.GetExtension(sciezka).ToLowerInvariant();
            if (roz == ".xls")
                throw new InvalidDataException("Stary format .xls nie jest obsługiwany — zapisz listę w Excelu jako .xlsx.");
            if (roz == ".xlsx" || roz == ".xlsm") l.WczytajXlsx(sciezka);
            else l.WczytajTekst(sciezka);
            l.Wybierz(-1, -1);
            return l;
        }

        /// <summary>Litery kolumny jak w Excelu: 0 → A, 26 → AA.</summary>
        public static string NazwaKolumny(int k)
        {
            var s = "";
            for (k++; k > 0; k = (k - 1) / 26) s = (char)('A' + (k - 1) % 26) + s;
            return s;
        }

        /// <summary>Numer kolumny (od 0) z liter („B”) albo z numeru („2”); -1, gdy nie da się odczytać.</summary>
        public static int NumerKolumny(string s)
        {
            s = s.Trim().ToUpperInvariant();
            if (int.TryParse(s, out var n)) return n >= 1 ? n - 1 : -1;
            int k = 0;
            foreach (var z in s)
            {
                if (z < 'A' || z > 'Z') return -1;
                k = k * 26 + (z - 'A' + 1);
            }
            return k - 1;
        }

        /// <summary>Ogranicza listę do arkusza i/lub kolumny (-1 = wszystkie) i wyznacza numery od nowa.</summary>
        public void Wybierz(int arkusz, int kolumna)
        {
            if (arkusz >= Arkusze.Count) arkusz = -1;
            WybranyArkusz = arkusz; WybranaKolumna = kolumna;
            var numery = new HashSet<string>(); var bledne = new List<string>();
            for (int a = 0; a < Arkusze.Count; a++)
            {
                if (arkusz >= 0 && a != arkusz) continue;
                foreach (var w in Arkusze[a].Wiersze)
                    for (int k = 0; k < w.Count; k++)
                        if (kolumna < 0 || k == kolumna) Ocen(w[k], numery, bledne);
            }
            Numery = numery; Bledne = bledne;
        }

        /// <summary>Numery PESEL w komórce: poprawne trafiają do <paramref name="numery"/>, 11-cyfrowe z błędną sumą do <paramref name="bledne"/>.</summary>
        public static void Ocen(Komorka kom, HashSet<string> numery, List<string> bledne)
        {
            var tekst = kom.Tekst;
            if (string.IsNullOrEmpty(tekst)) return;
            if (kom.Liczba && decimal.TryParse(tekst, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
            {
                if (d <= 0 || d != decimal.Truncate(d) || d >= 100000000000m) return;
                var s = decimal.Truncate(d).ToString(CultureInfo.InvariantCulture);
                if (s.Length == 11) Dodaj(s, numery, bledne);
                // Excel gubi zera wiodące w komórkach liczbowych (osoby urodzone w latach 2000–2009).
                else if (s.Length >= 8 && Pesel.Poprawny(s.PadLeft(11, '0'))) numery.Add(s.PadLeft(11, '0'));
                return;
            }
            foreach (Match m in Cyfry11.Matches(tekst)) Dodaj(m.Value, numery, bledne);
            // Spacje i myślniki wewnątrz numeru (np. „850101 12345”) nie przeszkadzają.
            var zwarty = tekst.Replace(" ", "").Replace(" ", "").Replace("-", "");
            if (zwarty != tekst && zwarty.Length == 11 && Cyfry11.IsMatch(zwarty)) Dodaj(zwarty, numery, bledne);
        }

        private static void Dodaj(string s, HashSet<string> numery, List<string> bledne)
        {
            if (Pesel.Poprawny(s)) numery.Add(s);
            else if (!bledne.Contains(s)) bledne.Add(s);
        }

        private void WczytajTekst(string sciezka)
        {
            var a = new Arkusz { Nazwa = Path.GetFileName(sciezka) };
            var wiersze = File.ReadAllLines(sciezka);
            // Separator: ten, który występuje najczęściej w pierwszych wierszach.
            char sep = new[] { ';', '\t', ',' }.OrderByDescending(z => wiersze.Take(20).Sum(w => w.Count(c => c == z))).First();
            foreach (var w in wiersze)
                a.Wiersze.Add(w.Split(sep).Select(p => new Komorka { Tekst = p.Trim().Trim('"') }).ToList());
            Arkusze.Add(a);
        }

        private void WczytajXlsx(string sciezka)
        {
            using var plik = new FileStream(sciezka, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var zip = new ZipArchive(plik, ZipArchiveMode.Read);
            var wspolne = new List<string>();
            var ws = zip.GetEntry("xl/sharedStrings.xml");
            if (ws != null)
                using (var s = ws.Open())
                using (var r = Czytnik(s))
                {
                    StringBuilder? sb = null;
                    while (r.Read())
                    {
                        if (r.NodeType == XmlNodeType.Element && r.LocalName == "si") sb = new StringBuilder();
                        else if (r.NodeType == XmlNodeType.Element && r.LocalName == "rPh") r.Skip();
                        else if (r.NodeType == XmlNodeType.Element && r.LocalName == "t" && sb != null) sb.Append(r.ReadElementContentAsString());
                        if (r.NodeType == XmlNodeType.EndElement && r.LocalName == "si" && sb != null) { wspolne.Add(sb.ToString()); sb = null; }
                    }
                }

            var nazwy = NazwyArkuszy(zip);
            var wpisy = zip.Entries
                .Where(w => w.FullName.StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase) &&
                            w.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) && !w.FullName.Contains("_rels"))
                .OrderBy(w => { var m = Regex.Match(w.Name, @"\d+"); return m.Success ? int.Parse(m.Value) : int.MaxValue; }).ToList();
            foreach (var wpis in wpisy)
            {
                var a = new Arkusz { Nazwa = nazwy.TryGetValue(wpis.FullName, out var n) ? n : Path.GetFileNameWithoutExtension(wpis.Name) };
                using var s = wpis.Open();
                using var r = Czytnik(s);
                string typ = "";
                int wiersz = -1, kolumna = -1;
                void Ustaw(string tekst, bool liczba)
                {
                    if (wiersz < 0 || kolumna < 0 || wiersz > 1000000 || kolumna > 2000) return;
                    while (a.Wiersze.Count <= wiersz) a.Wiersze.Add(new List<Komorka>());
                    var w = a.Wiersze[wiersz];
                    while (w.Count <= kolumna) w.Add(new Komorka { Tekst = "" });
                    w[kolumna] = new Komorka { Tekst = tekst, Liczba = liczba };
                }
                while (r.Read())
                {
                    if (r.NodeType != XmlNodeType.Element) continue;
                    if (r.LocalName == "row")
                    {
                        wiersz = int.TryParse(r.GetAttribute("r"), out var nr) ? nr - 1 : wiersz + 1;
                        kolumna = -1;
                    }
                    else if (r.LocalName == "c")
                    {
                        typ = r.GetAttribute("t") ?? "";
                        var adres = r.GetAttribute("r");
                        var litery = adres == null ? "" : Regex.Match(adres, "^[A-Za-z]+").Value;
                        kolumna = litery != "" ? NumerKolumny(litery) : kolumna + 1;
                    }
                    else if (r.LocalName == "v")
                    {
                        var v = r.ReadElementContentAsString();
                        if (typ == "s") { if (int.TryParse(v, out var i) && i >= 0 && i < wspolne.Count) Ustaw(wspolne[i], false); }
                        else if (typ == "" || typ == "n") Ustaw(Liczba(v), true);
                        else Ustaw(v, false);
                    }
                    else if (r.LocalName == "t" && typ == "inlineStr") Ustaw(r.ReadElementContentAsString(), false);
                }
                // Puste wiersze na końcu nie są potrzebne.
                while (a.Wiersze.Count > 0 && a.Wiersze[a.Wiersze.Count - 1].All(k => string.IsNullOrEmpty(k.Tekst))) a.Wiersze.RemoveAt(a.Wiersze.Count - 1);
                Arkusze.Add(a);
            }
        }

        /// <summary>Liczba z pliku („8.5010112345E10”) w zwykłym zapisie.</summary>
        private static string Liczba(string v) =>
            decimal.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d.ToString(CultureInfo.InvariantCulture) : v;

        /// <summary>Nazwy arkuszy z workbook.xml (ścieżka pliku arkusza → nazwa).</summary>
        private static Dictionary<string, string> NazwyArkuszy(ZipArchive zip)
        {
            var wynik = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var rel = new Dictionary<string, string>();
                var er = zip.GetEntry("xl/_rels/workbook.xml.rels");
                var ew = zip.GetEntry("xl/workbook.xml");
                if (er == null || ew == null) return wynik;
                using (var s = er.Open())
                using (var r = Czytnik(s))
                    while (r.Read())
                        if (r.NodeType == XmlNodeType.Element && r.LocalName == "Relationship")
                            rel[r.GetAttribute("Id") ?? ""] = r.GetAttribute("Target") ?? "";
                using (var s = ew.Open())
                using (var r = Czytnik(s))
                    while (r.Read())
                        if (r.NodeType == XmlNodeType.Element && r.LocalName == "sheet")
                        {
                            var id = r.GetAttribute("id", "http://schemas.openxmlformats.org/officeDocument/2006/relationships") ?? "";
                            if (rel.TryGetValue(id, out var cel)) wynik[cel.StartsWith("/") ? cel.TrimStart('/') : "xl/" + cel] = r.GetAttribute("name") ?? "";
                        }
            }
            catch (Exception) { }
            return wynik;
        }

        private static XmlReader Czytnik(Stream s) =>
            XmlReader.Create(s, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
    }
}
