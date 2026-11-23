using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace Weryfikator.Logika
{
    /// <summary>Lista numerów PESEL wczytana z arkusza .xlsx (wszystkie arkusze, wszystkie komórki) albo z pliku tekstowego/CSV.</summary>
    internal sealed class ListaPesel
    {
        public HashSet<string> Numery { get; } = new HashSet<string>();
        /// <summary>Wartości 11-cyfrowe z błędną sumą kontrolną (zapewne literówki na liście).</summary>
        public List<string> Bledne { get; } = new List<string>();
        public string Plik { get; private set; } = "";

        public bool Zawiera(string pesel) => Numery.Contains(pesel);

        private static readonly Regex Cyfry11 = new Regex(@"(?<!\d)\d{11}(?!\d)");

        public static ListaPesel Wczytaj(string sciezka)
        {
            var l = new ListaPesel { Plik = sciezka };
            var roz = Path.GetExtension(sciezka).ToLowerInvariant();
            if (roz == ".xls")
                throw new InvalidDataException("Stary format .xls nie jest obsługiwany — zapisz listę w Excelu jako .xlsx.");
            if (roz == ".xlsx" || roz == ".xlsm")
                l.WczytajXlsx(sciezka);
            else
                foreach (var wiersz in File.ReadAllLines(sciezka))
                    l.DodajTekst(wiersz);
            return l;
        }

        private void DodajTekst(string tekst)
        {
            // Spacje i myślniki wewnątrz numeru (np. „850101 12345”) nie przeszkadzają.
            foreach (Match m in Cyfry11.Matches(tekst)) Dodaj(m.Value);
            var zwarty = tekst.Replace(" ", "").Replace(" ", "").Replace("-", "");
            if (zwarty != tekst && zwarty.Length == 11 && Cyfry11.IsMatch(zwarty)) Dodaj(zwarty);
        }

        private void DodajLiczbe(string v)
        {
            if (!decimal.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) { DodajTekst(v); return; }
            if (d <= 0 || d != decimal.Truncate(d) || d >= 100000000000m) return;
            var s = decimal.Truncate(d).ToString(CultureInfo.InvariantCulture);
            if (s.Length == 11) { Dodaj(s); return; }
            // Excel gubi zera wiodące w komórkach liczbowych (osoby urodzone w latach 2000–2009).
            if (s.Length >= 8)
            {
                var uzup = s.PadLeft(11, '0');
                if (Pesel.Poprawny(uzup)) Numery.Add(uzup);
            }
        }

        private void Dodaj(string s)
        {
            if (Pesel.Poprawny(s)) Numery.Add(s);
            else if (!Bledne.Contains(s)) Bledne.Add(s);
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

            foreach (var wpis in zip.Entries)
            {
                if (!wpis.FullName.StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase) ||
                    !wpis.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) || wpis.FullName.Contains("_rels")) continue;
                using var s = wpis.Open();
                using var r = Czytnik(s);
                string typ = "";
                while (r.Read())
                {
                    if (r.NodeType != XmlNodeType.Element) continue;
                    if (r.LocalName == "c") typ = r.GetAttribute("t") ?? "";
                    else if (r.LocalName == "v")
                    {
                        var v = r.ReadElementContentAsString();
                        if (typ == "s")
                        {
                            if (int.TryParse(v, out var i) && i >= 0 && i < wspolne.Count) DodajTekst(wspolne[i]);
                        }
                        else if (typ == "str" || typ == "inlineStr") DodajTekst(v);
                        else if (typ == "" || typ == "n") DodajLiczbe(v);
                    }
                    else if (r.LocalName == "t" && typ == "inlineStr") DodajTekst(r.ReadElementContentAsString());
                }
            }
        }

        private static XmlReader Czytnik(Stream s) =>
            XmlReader.Create(s, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
    }
}
