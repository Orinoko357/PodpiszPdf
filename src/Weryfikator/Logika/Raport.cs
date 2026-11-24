using System;
using System.Collections.Generic;
using System.Linq;
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

        // ---------------------------------------------------------------- PDF

        /// <summary>Dane do nagłówka raportu.</summary>
        internal sealed class Naglowek
        {
            public string Wersja = "";
            public DateTime Data = DateTime.Now;
            public string ListaPesel = "nie użyto";
            public string ListaZaufania = "brak";
        }

        private const int Granat = 0x1665AC, Szary = 0x666666, JasnySzary = 0xDDDDDD, Zielony = 0xE4F3DC, Zolty = 0xFFF3CD, Czerwony = 0xFADADD,
            CiemnyZielony = 0x1E7B34, CiemnyZolty = 0x8A6100, CiemnyCzerwony = 0xB02A37;

        private static int Tlo(WynikPodpisu w) => w.WPorzadku ? Zielony : Zly(w) ? Czerwony : Zolty;
        private static int Akcent(WynikPodpisu w) => w.WPorzadku ? CiemnyZielony : Zly(w) ? CiemnyCzerwony : CiemnyZolty;
        private static bool Zly(WynikPodpisu w) => w.Status == Status.Niewazny || w.Status == Status.BrakPodpisu || w.Status == Status.Blad || w.NaLiscie == false;

        /// <summary>Raport PDF (A4 poziomo): podsumowanie i tabela podpisów z uwagami. Czcionki: pliki .ttf (zwykła i pogrubiona).</summary>
        public static void ZapiszPdf(string sciezka, IList<WynikPodpisu> wyniki, Naglowek n, byte[] czcionka, byte[] czcionkaPogrubiona)
        {
            const double M = 36, R = 8, RU = 7.5, Odstep = 4;
            var pdf = new Pdf(841.89, 595.28, new CzcionkaTtf(czcionka), new CzcionkaTtf(czcionkaPogrubiona));
            double szer = pdf.Szerokosc - 2 * M, y = M;

            // Nagłówek.
            pdf.Prostokat(M, y, szer, 3, Granat);
            y += 26;
            pdf.Tekst(M, y, "Raport weryfikacji podpisów elektronicznych", 1, 17, Granat);
            var data = "Sporządzono " + n.Data.ToString("dd.MM.yyyy") + " o " + n.Data.ToString("HH:mm");
            pdf.Tekst(M + szer - pdf.SzerokoscTekstu(data, 0, 9), y, data, 0, 9, Szary);
            y += 16;
            pdf.Tekst(M, y, "Lista numerów PESEL: " + n.ListaPesel, 0, 9, Szary);
            y += 12;
            pdf.Tekst(M, y, "Lista zaufania UE: " + n.ListaZaufania, 0, 9, Szary);
            y += 14;

            // Kafelki z podsumowaniem.
            bool zLista = wyniki.Any(w => !w.BezListyPesel);
            var kafelki = new[]
            {
                new { Liczba = wyniki.Select(w => w.Plik).Distinct().Count(), Opis = "plików", Tlo = 0xEEF3F9, Kolor = Granat },
                new { Liczba = wyniki.Count(w => w.Nr > 0), Opis = "podpisów", Tlo = 0xEEF3F9, Kolor = Granat },
                new { Liczba = wyniki.Count(w => w.WPorzadku), Opis = zLista ? "w porządku (ważny podpis, PESEL na liście)" : "w porządku (ważny podpis)", Tlo = Zielony, Kolor = CiemnyZielony },
                new { Liczba = wyniki.Count(w => !w.WPorzadku && !Zly(w)), Opis = "do wyjaśnienia", Tlo = Zolty, Kolor = CiemnyZolty },
                new { Liczba = wyniki.Count(Zly), Opis = "nieprawidłowych (nieważny, brak podpisu, PESEL spoza listy)", Tlo = Czerwony, Kolor = CiemnyCzerwony },
            };
            double[] szerKafelkow = { 0.11, 0.11, 0.26, 0.17, 0.35 };
            double kx = M;
            for (int i = 0; i < kafelki.Length; i++)
            {
                double ks = szer * szerKafelkow[i] - (i < kafelki.Length - 1 ? 6 : 0);
                pdf.Prostokat(kx, y, ks, 34, kafelki[i].Tlo);
                pdf.Prostokat(kx, y, 3, 34, kafelki[i].Kolor);
                var liczba = kafelki[i].Liczba.ToString();
                pdf.Tekst(kx + 11, y + 23, liczba, 1, 18, kafelki[i].Kolor);
                double lx = kx + 17 + pdf.SzerokoscTekstu(liczba, 1, 18);
                var opis = pdf.Zawin(kafelki[i].Opis, 0, 8, kx + ks - lx - 4);
                for (int k = 0; k < opis.Count && k < 2; k++) pdf.Tekst(lx, y + (opis.Count == 1 ? 21 : 15) + k * 10, opis[k], 0, 8, 0x333333);
                kx += szer * szerKafelkow[i];
            }
            y += 46;

            // Tabela: wiersz z danymi podpisu, pod nim uwagi na całą szerokość.
            string[] nagl = { "Wynik", "Plik", "Nr", "Ważność podpisu", "Rodzaj podpisu", "Podpisujący", "PESEL", "Na liście", "Czas podpisu" };
            double[] kol = { 54, 196, 20, 84, 82, 132, 64, 44, szer - 676 };
            void NaglowekTabeli()
            {
                pdf.Prostokat(M, y, szer, 18, Granat);
                double x = M;
                for (int i = 0; i < nagl.Length; i++) { pdf.Tekst(x + Odstep, y + 12, nagl[i], 1, R, 0xFFFFFF); x += kol[i]; }
                y += 18;
            }
            NaglowekTabeli();
            double dol = pdf.Wysokosc - M - 16;
            foreach (var w in wyniki)
            {
                string[] pola = { w.WPorzadku ? "OK" : "SPRAWDŹ", Path.GetFileName(w.Plik), w.Nr == 0 ? "" : w.Nr.ToString(), w.StatusTekst, w.RodzajTekst, w.Podpisujacy,
                                  w.PeselTekst, w.NaLiscieTekst, w.CzasTekst.Replace(" (znacznik czasu)", "\n(znacznik czasu)") };
                var linie = new List<string>[pola.Length];
                int maks = 1;
                for (int i = 0; i < pola.Length; i++)
                {
                    linie[i] = pdf.Zawin(pola[i], i == 0 || i == 3 ? 1 : 0, R, kol[i] - 2 * Odstep);
                    maks = Math.Max(maks, linie[i].Count);
                }
                var uwagi = w.Uwagi.Count == 0 ? new List<string>() : pdf.Zawin(string.Join(" ", w.Uwagi), 0, RU, szer - kol[0] - 2 * Odstep);
                double wys = 6 + maks * 10 + (uwagi.Count > 0 ? uwagi.Count * 9.5 + 2 : 0) + 3;
                if (y + wys > dol) { pdf.NowaStrona(); y = M; NaglowekTabeli(); }

                pdf.Prostokat(M, y, szer, wys, Tlo(w));
                pdf.Prostokat(M, y, 3, wys, Akcent(w));
                double x = M;
                for (int i = 0; i < pola.Length; i++)
                {
                    for (int k = 0; k < linie[i].Count; k++)
                        pdf.Tekst(x + Odstep + (i == 0 ? 2 : 0), y + 12 + k * 10, linie[i][k], i == 0 || i == 3 ? 1 : 0, R, i == 0 || i == 3 ? Akcent(w) : 0x111111);
                    x += kol[i];
                }
                double uy = y + 6 + maks * 10 + 9;
                foreach (var u in uwagi) { pdf.Tekst(M + kol[0] + Odstep, uy, u, 0, RU, 0x444444); uy += 9.5; }
                y += wys;
                pdf.Linia(M, y, M + szer, y, 0xFFFFFF, 1.2);
            }

            // Objaśnienie pod tabelą.
            var nota = pdf.Zawin("Wynik „OK” oznacza ważny podpis" + (zLista ? " i numer PESEL podpisującego obecny na liście" : "") +
                                 ". Raport ma charakter pomocniczy: opiera się na liście zaufania UE i danych z certyfikatów; nie jest wynikiem kwalifikowanej usługi walidacji podpisów.", 0, 7.5, szer);
            if (y + 10 + nota.Count * 9.5 > dol) { pdf.NowaStrona(); y = M; }
            y += 14;
            foreach (var l in nota) { pdf.Tekst(M, y, l, 0, 7.5, Szary); y += 9.5; }

            // Stopki z numerami stron.
            for (int i = 0; i < pdf.LiczbaStron; i++)
            {
                pdf.Strona(i);
                double sy = pdf.Wysokosc - M + 6;
                pdf.Linia(M, sy - 11, M + szer, sy - 11, JasnySzary);
                pdf.Tekst(M, sy, "Weryfikator " + n.Wersja, 0, 7.5, Szary);
                var nr = $"Strona {i + 1} z {pdf.LiczbaStron}";
                pdf.Tekst(M + szer - pdf.SzerokoscTekstu(nr, 0, 7.5), sy, nr, 0, 7.5, Szary);
            }
            pdf.Zapisz(sciezka, "Raport weryfikacji podpisów elektronicznych");
        }

        /// <summary>Zapis w formacie wynikającym z rozszerzenia pliku (.pdf albo .csv).</summary>
        public static void Zapisz(string sciezka, IList<WynikPodpisu> wyniki, Naglowek n, Func<byte[][]> czcionki)
        {
            if (Path.GetExtension(sciezka).ToLowerInvariant() == ".pdf")
            {
                var c = czcionki();
                ZapiszPdf(sciezka, wyniki, n, c[0], c[1]);
            }
            else ZapiszCsv(sciezka, wyniki);
        }
    }
}
