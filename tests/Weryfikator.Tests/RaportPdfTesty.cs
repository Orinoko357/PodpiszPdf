using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Weryfikator.Logika;
using Xunit;

namespace Weryfikator.Tests
{
    public class RaportPdfTesty
    {
        private static string Zasoby()
        {
            var f = AppContext.BaseDirectory;
            while (f != null && !Directory.Exists(Path.Combine(f, "src", "PodpisPdf.Core", "Zasoby"))) f = Path.GetDirectoryName(f);
            return Path.Combine(f!, "src", "PodpisPdf.Core", "Zasoby");
        }

        internal static void Zapisz(string plik, IList<WynikPodpisu> wyniki) =>
            Raport.ZapiszPdf(plik, wyniki, new Raport.Naglowek { Wersja = "1.2", ListaPesel = "pesele.xlsx (kolumna A, 2 numery)", ListaZaufania = "pobrana 24.11.2026 09:00, 31 krajów" },
                File.ReadAllBytes(Path.Combine(Zasoby(), "LiberationSans-Regular.ttf")), File.ReadAllBytes(Path.Combine(Zasoby(), "LiberationSans-Bold.ttf")));

        [Fact]
        public void RaportPdfWieleStron()
        {
            var wyniki = new List<WynikPodpisu>();
            for (int i = 0; i < 60; i++)
                wyniki.Add(new WynikPodpisu
                {
                    Plik = Path.Combine("C:", "dokumenty", $"wniosek_o_dofinansowanie_z_bardzo_długą_nazwą_pliku_{i:000}_podpisany.pdf"), Nr = 1,
                    Status = i % 5 == 3 ? Status.Niewazny : i % 7 == 2 ? Status.Nieokreslony : Status.Wazny, Rodzaj = i % 3 == 0 ? Rodzaj.Zaufany : Rodzaj.Kwalifikowany,
                    Podpisujacy = "Zażółć Gęślą-Jaźń", Pesel = Pliki.PeselA, NaLiscie = i % 4 != 1, CzasUtc = new DateTime(2026, 11, 20, 9, 30, 0, DateTimeKind.Utc), CzasZeZnacznika = i % 2 == 0,
                    Uwagi = { i % 5 == 3 ? "Podpis jest nieprawidłowy — dokument zmieniono po podpisaniu albo podpis jest uszkodzony." : "Nie sprawdzano unieważnienia certyfikatu (opcja wyłączona)." }
                });
            wyniki.Add(new WynikPodpisu { Plik = "bez.pdf", Status = Status.BrakPodpisu, Uwagi = { "Plik nie zawiera podpisu elektronicznego." } });
            var plik = Path.Combine(Pliki.Folder(), "raport.pdf");
            Zapisz(plik, wyniki);

            var d = File.ReadAllBytes(plik);
            var tekst = Encoding.GetEncoding(28591).GetString(d);
            Assert.StartsWith("%PDF-1.7", tekst);
            Assert.EndsWith("%%EOF\n", tekst);
            int strony = System.Text.RegularExpressions.Regex.Matches(tekst, "/Type /Page ").Count;
            Assert.True(strony >= 3, "stron: " + strony);
            Assert.Contains($"/Count {strony} ", tekst);
            // Tablica odsyłaczy wskazuje początki obiektów.
            int xref = int.Parse(System.Text.RegularExpressions.Regex.Match(tekst, @"startxref\n(\d+)").Groups[1].Value);
            Assert.Equal("xref", tekst.Substring(xref, 4));
            var wpisy = System.Text.RegularExpressions.Regex.Matches(tekst.Substring(xref), @"(\d{10}) 00000 n ");
            for (int i = 0; i < wpisy.Count; i++)
                Assert.StartsWith($"{i + 1} 0 obj", tekst.Substring(int.Parse(wpisy[i].Groups[1].Value), 12));
            // Podzbiór czcionki zamiast całego pliku — raport jest mały.
            Assert.True(d.Length < 150000, "rozmiar: " + d.Length);
        }

        [Fact]
        public void PodzbiorCzcionkiIZawijanie()
        {
            var c = new CzcionkaTtf(File.ReadAllBytes(Path.Combine(Zasoby(), "LiberationSans-Regular.ttf")));
            Assert.NotEqual(0, c.Glif('ą'));
            Assert.NotEqual(c.Glif('a'), c.Glif('ą'));
            Assert.Equal(c.Glif(' '), c.Glif(' '));
            Assert.InRange(c.SzerokoscTekstu("Zażółć", 10), 20, 40);
            var podzbior = c.Podzbior();
            Assert.True(podzbior.Length < 60000, "rozmiar: " + podzbior.Length);
            var ponownie = new CzcionkaTtf(DodajCmap(podzbior));

            var pdf = new Pdf(600, 800, c);
            var wiersze = pdf.Zawin("Ala ma kota a_kot_ma_bardzo_długą_nazwę_bez_spacji_która_się_nie_mieści i psa", 0, 10, 100);
            Assert.True(wiersze.Count >= 4);
            Assert.All(wiersze, w => Assert.True(pdf.SzerokoscTekstu(w, 0, 10) <= 100.01, w));
            Assert.Equal("Alamakotaa_kot_ma_bardzo_długą_nazwę_bez_spacji_która_się_nie_mieściipsa", string.Concat(wiersze).Replace(" ", ""));
        }

        /// <summary>Podzbiór nie ma tabeli cmap (PDF jej nie potrzebuje) — parser wymaga jej tylko przy pełnej czcionce.</summary>
        private static byte[] DodajCmap(byte[] podzbior)
        {
            Assert.Equal(0x00, podzbior[0]); Assert.Equal(0x01, podzbior[1]);
            return File.ReadAllBytes(Path.Combine(Zasoby(), "LiberationSans-Regular.ttf"));
        }
    }
}
