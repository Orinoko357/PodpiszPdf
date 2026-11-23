using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Weryfikator.Logika
{
    /// <summary>Raport CSV (średniki, UTF-8 z BOM — otwiera się wprost w Excelu).</summary>
    internal static class Raport
    {
        public static readonly string[] Naglowki =
            { "Plik", "Nr podpisu", "Wynik", "Ważność podpisu", "Rodzaj podpisu", "Podpisujący", "PESEL", "PESEL na liście", "Czas podpisu", "Wystawca", "Uwagi", "Folder" };

        public static string[] Wiersz(WynikPodpisu w) => new[]
        {
            Path.GetFileName(w.Plik), w.Nr == 0 ? "" : w.Nr.ToString(), w.WPorzadku ? "OK" : "SPRAWDŹ", w.StatusTekst, w.RodzajTekst, w.Podpisujacy,
            w.PeselTekst, w.NaLiscieTekst, w.CzasTekst, w.Wystawca, string.Join(" ", w.Uwagi), Path.GetDirectoryName(w.Plik) ?? ""
        };

        public static void ZapiszCsv(string sciezka, IEnumerable<WynikPodpisu> wyniki)
        {
            var sb = new StringBuilder();
            sb.AppendLine(string.Join(";", Naglowki));
            foreach (var w in wyniki)
            {
                var p = Wiersz(w);
                for (int i = 0; i < p.Length; i++)
                {
                    // PESEL jako tekst (="…"), żeby Excel nie zgubił zer wiodących; reszta w cudzysłowach.
                    var v = p[i].Replace("\"", "\"\"").Replace("\r", " ").Replace("\n", " ");
                    if (v.Length > 0 && "=+-@".IndexOf(v[0]) >= 0) v = "'" + v;
                    sb.Append(i == 6 && Pesel.Poprawny(p[i]) ? "=\"" + p[i] + "\"" : "\"" + v + "\"");
                    sb.Append(i < p.Length - 1 ? ";" : "\r\n");
                }
            }
            File.WriteAllText(sciezka, sb.ToString(), new UTF8Encoding(true));
        }
    }
}
