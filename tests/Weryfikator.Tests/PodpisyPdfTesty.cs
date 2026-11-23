#if NET8_0_OR_GREATER
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Xml;
using PodpisPdf.Core;
using Weryfikator.Logika;
using Xunit;
using WynikPodpisu = Weryfikator.Logika.WynikPodpisu;

namespace Weryfikator.Tests
{
    public class PodpisyPdfTesty
    {
        private readonly Urzad _urzad = new Urzad();
        private readonly Tsa _tsa = new Tsa();
        private readonly string _folder = Pliki.Folder();

        private Ustawienia Ust(bool zListaPesel = true, params WpisListy[] dodatkowe)
        {
            var u = new Ustawienia { Online = false, Lista = new ListaZaufania { Wpisy = { _urzad.Wpis(), Urzad.Wpis(_tsa.Cert, "TSA") } } };
            u.Lista.Wpisy.AddRange(dodatkowe);
            if (zListaPesel) u.Pesele = ListaPesel.Wczytaj(Pliki.Xlsx(_folder, new[] { Pliki.PeselA }, new string[0]));
            return u;
        }

        private string PodpiszPdf(X509Certificate2 cert, string nazwa = "wniosek.pdf", string? wejscie = null)
        {
            wejscie ??= Tsa.UtworzPdf(_folder, nazwa);
            var wyjscie = Path.Combine(_folder, Path.GetFileNameWithoutExtension(wejscie) + "_p.pdf");
            new PodpisywaczPdf(_tsa).Podpisz(wejscie, wyjscie, cert, new OpcjePodpisu());
            return wyjscie;
        }

        private static string Opis(WynikPodpisu w) => w.StatusTekst + ": " + string.Join(" | ", w.Uwagi);

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void PdfPodpisKwalifikowanyPeselNaLiscie(bool ecdsa)
        {
            using var cert = _urzad.Wystaw("Jan Kowalski", "PNOPL-" + Pliki.PeselA, ecdsa: ecdsa);
            var w = Sprawdzanie.Plik(PodpiszPdf(cert), Ust()).Single();
            Assert.True(w.Status == Status.Wazny, Opis(w));
            Assert.Equal(Rodzaj.Kwalifikowany, w.Rodzaj);
            Assert.Equal("Jan Kowalski", w.Podpisujacy);
            Assert.Equal(Pliki.PeselA, w.Pesel);
            Assert.True(w.NaLiscie);
            Assert.True(w.CzasZeZnacznika);
            Assert.True(w.WPorzadku);
            Assert.Equal(1, w.Nr);
        }

        [Fact]
        public void PdfPeselSpozaListyIStaryFormatPesel()
        {
            using var cert = _urzad.Wystaw("Anna Nowak", "PESEL: " + Pliki.PeselB);
            var w = Sprawdzanie.Plik(PodpiszPdf(cert), Ust()).Single();
            Assert.True(w.Status == Status.Wazny, Opis(w));
            Assert.Equal(Pliki.PeselB, w.Pesel);
            Assert.False(w.NaLiscie);
            Assert.False(w.WPorzadku);
        }

        [Fact]
        public void PdfZmienionyPoPodpisaniuJestNiewazny()
        {
            using var cert = _urzad.Wystaw("Jan Kowalski", "PNOPL-" + Pliki.PeselA);
            var plik = PodpiszPdf(cert);
            var d = File.ReadAllBytes(plik);
            int i = Encoding.ASCII.GetString(d).IndexOf("Wniosek", StringComparison.Ordinal);
            if (i < 0) i = 200;
            d[i] ^= 1;
            File.WriteAllBytes(plik, d);
            var w = Sprawdzanie.Plik(plik, Ust()).Single();
            Assert.Equal(Status.Niewazny, w.Status);
        }

        [Fact]
        public void PdfDopisaneDanePoPodpisie()
        {
            using var cert = _urzad.Wystaw("Jan Kowalski", "PNOPL-" + Pliki.PeselA);
            var plik = PodpiszPdf(cert);
            File.AppendAllText(plik, "\n1 0 obj\n<< /Dopisane true >>\nendobj\n");
            var w = Sprawdzanie.Plik(plik, Ust()).Single();
            Assert.Equal(Status.Nieokreslony, w.Status);
            Assert.Contains(w.Uwagi, x => x.Contains("dopisano"));
        }

        [Fact]
        public void PdfDwaPodpisy()
        {
            using var c1 = _urzad.Wystaw("Jan Kowalski", "PNOPL-" + Pliki.PeselA);
            using var c2 = _urzad.Wystaw("Anna Nowak", "PNOPL-" + Pliki.PeselB);
            var plik = PodpiszPdf(c2, wejscie: PodpiszPdf(c1));
            var w = Sprawdzanie.Plik(plik, Ust());
            Assert.Equal(2, w.Count);
            Assert.All(w, x => Assert.True(x.Status == Status.Wazny, Opis(x)));
            Assert.Equal(new[] { "Jan Kowalski", "Anna Nowak" }, w.Select(x => x.Podpisujacy));
            Assert.Equal(new[] { 1, 2 }, w.Select(x => x.Nr));
            Assert.Equal(new bool?[] { true, false }, w.Select(x => x.NaLiscie));
        }

        [Fact]
        public void PdfBezPodpisu()
        {
            var w = Sprawdzanie.Plik(Tsa.UtworzPdf(_folder), Ust()).Single();
            Assert.Equal(Status.BrakPodpisu, w.Status);
            Assert.Equal(0, w.Nr);
        }

        [Fact]
        public void WystawcaSpozaListyZaufania()
        {
            var obcy = new Urzad("CN=Nieznany Urząd, C=PL");
            using var cert = obcy.Wystaw("Jan Kowalski", "PNOPL-" + Pliki.PeselA);
            var w = Sprawdzanie.Plik(PodpiszPdf(cert), Ust()).Single();
            Assert.Equal(Status.Niewazny, w.Status);
            Assert.Equal(Rodzaj.Inny, w.Rodzaj);
        }

        [Fact]
        public void CertyfikatNiekwalifikowanyZUrzeduNaLiscie()
        {
            using var cert = _urzad.Wystaw("Jan Kowalski", "PNOPL-" + Pliki.PeselA, kwalifikowany: false);
            var w = Sprawdzanie.Plik(PodpiszPdf(cert), Ust()).Single();
            Assert.Equal(Status.Nieokreslony, w.Status);
            Assert.Equal(Rodzaj.Inny, w.Rodzaj);
        }

        [Fact]
        public void CertyfikatWygaslyPrzedPodpisem()
        {
            using var cert = _urzad.Wystaw("Jan Kowalski", "PNOPL-" + Pliki.PeselA, od: DateTimeOffset.UtcNow.AddYears(-3), doDnia: DateTimeOffset.UtcNow.AddYears(-1));
            var w = Sprawdzanie.Plik(PodpiszPdf(cert), Ust()).Single();
            Assert.Equal(Status.Niewazny, w.Status);
            Assert.Contains(w.Uwagi, x => x.Contains("nie był ważny"));
        }

        [Fact]
        public void CertyfikatWygaslyPoPodpisieZeZnacznikiemCzasu()
        {
            // Podpis złożony 2 lata temu (znacznik czasu), certyfikat wygasł rok temu — podpis pozostaje ważny.
            _tsa.Czas = DateTime.UtcNow.AddYears(-2);
            using var cert = _urzad.Wystaw("Jan Kowalski", "PNOPL-" + Pliki.PeselA, od: DateTimeOffset.UtcNow.AddYears(-3), doDnia: DateTimeOffset.UtcNow.AddYears(-1));
            var w = Sprawdzanie.Plik(PodpiszPdf(cert), Ust()).Single();
            Assert.True(w.Status == Status.Wazny, Opis(w));
            Assert.True(w.CzasZeZnacznika);
        }

        [Fact]
        public void ZnacznikCzasuSpozaListyNieJestZaufany()
        {
            using var cert = _urzad.Wystaw("Jan Kowalski", "PNOPL-" + Pliki.PeselA);
            var plik = PodpiszPdf(cert);
            var u = Ust();
            u.Lista!.Wpisy.RemoveAll(x => x.Typ == "TSA");
            var w = Sprawdzanie.Plik(plik, u).Single();
            Assert.True(w.Status == Status.Wazny, Opis(w));
            Assert.False(w.CzasZeZnacznika);
            Assert.NotNull(w.CzasUtc);
        }

        [Fact]
        public void UslugaWycofanaZListy()
        {
            using var cert = _urzad.Wystaw("Jan Kowalski", "PNOPL-" + Pliki.PeselA);
            var u = Ust();
            u.Lista!.Wpisy[0] = _urzad.Wpis(status: "withdrawn");
            var w = Sprawdzanie.Plik(PodpiszPdf(cert), u).Single();
            Assert.Equal(Status.Nieokreslony, w.Status);
        }

        [Fact]
        public void PodpisOsobistyZCertyfikatemGlownymUzytkownika()
        {
            var plId = new Urzad("CN=pl.ID Root CA, O=Minister Spraw Wewnętrznych i Administracji, C=PL");
            using var cert = plId.Wystaw("Jan Kowalski", "PNOPL-" + Pliki.PeselA, kwalifikowany: false, ecdsa: true);
            var plik = PodpiszPdf(cert);
            var u = Ust();
            var bez = Sprawdzanie.Plik(plik, u).Single();
            Assert.Equal(Rodzaj.Osobisty, bez.Rodzaj);
            Assert.Equal(Status.Nieokreslony, bez.Status);
            Assert.Contains(bez.Uwagi, x => x.Contains("Zaufane certyfikaty"));

            u.CertyfikatyUzytkownika.Add(new X509Certificate2(plId.Cert.RawData));
            var w = Sprawdzanie.Plik(plik, u).Single();
            Assert.True(w.Status == Status.Wazny, Opis(w));
            Assert.Equal(Rodzaj.Osobisty, w.Rodzaj);
            Assert.Equal(Pliki.PeselA, w.Pesel);
            Assert.True(w.WPorzadku);
        }

        /// <summary>
        /// Zestaw próbek do testu gotowego Weryfikator.exe (GitHub Actions, zmienna WERYFIKATOR_PROBKI = folder):
        /// podpisane pliki, lista PESEL, lista zaufania i plik „oczekiwane.csv” (plik;ważność;PESEL na liście).
        /// </summary>
        [Fact]
        public void ProbkiDlaProgramu()
        {
            var folder = Environment.GetEnvironmentVariable("WERYFIKATOR_PROBKI");
            if (string.IsNullOrEmpty(folder)) return;
            var pliki = Path.Combine(folder, "pliki");
            Directory.CreateDirectory(pliki);
            Directory.CreateDirectory(Path.Combine(folder, "certyfikaty"));
            var oczekiwane = new List<string>();
            void Dodaj(string plik, string nazwa, string waznosc, string naLiscie)
            {
                File.Copy(plik, Path.Combine(pliki, nazwa), true);
                oczekiwane.Add(nazwa + ";" + waznosc + ";" + naLiscie);
            }

            using var jan = _urzad.Wystaw("Jan Kowalski", "PNOPL-" + Pliki.PeselA);
            using var janEc = _urzad.Wystaw("Jan Kowalski", "PNOPL-" + Pliki.PeselA, ecdsa: true);
            using var anna = _urzad.Wystaw("Anna Nowak", "PESEL: " + Pliki.PeselB);
            using var pieczec = _urzad.Wystaw("Minister do spraw informatyzacji - pieczęć podpisu zaufanego", null, pieczec: true);
            var obcy = new Urzad("CN=Nieznany Urząd, C=PL");
            using var odObcego = obcy.Wystaw("Jan Kowalski", "PNOPL-" + Pliki.PeselA);
            var plId = new Urzad("CN=pl.ID Root CA, O=Minister Spraw Wewnętrznych i Administracji, C=PL");
            using var osobisty = plId.Wystaw("Jan Kowalski", "PNOPL-" + Pliki.PeselA, kwalifikowany: false, ecdsa: true);
            File.WriteAllBytes(Path.Combine(folder, "certyfikaty", "pl-id-test.cer"), plId.Cert.RawData);

            Dodaj(PodpiszPdf(jan), "01 kwalifikowany RSA.pdf", "WAŻNY", "TAK");
            Dodaj(PodpiszPdf(janEc), "02 kwalifikowany ECDSA.pdf", "WAŻNY", "TAK");
            Dodaj(PodpiszPdf(anna), "03 PESEL spoza listy.pdf", "WAŻNY", "NIE");
            var zmieniony = PodpiszPdf(jan);
            var d = File.ReadAllBytes(zmieniony);
            d[Math.Max(Encoding.ASCII.GetString(d).IndexOf("Wniosek", StringComparison.Ordinal), 200)] ^= 1;
            File.WriteAllBytes(zmieniony, d);
            Dodaj(zmieniony, "04 zmieniony po podpisaniu.pdf", "NIEWAŻNY", "TAK");
            Dodaj(Tsa.UtworzPdf(_folder, "bez.pdf"), "05 bez podpisu.pdf", "BRAK PODPISU", "");
            Dodaj(PodpiszPdf(odObcego), "06 wystawca spoza listy zaufania.pdf", "NIEWAŻNY", "TAK");
            Dodaj(PodpiszPdf(osobisty), "07 podpis osobisty.pdf", "WAŻNY", "TAK");
            var zaufany = Path.Combine(_folder, "zaufany.p7m");
            File.WriteAllBytes(zaufany, PodpisyTesty.PodpiszCms(Encoding.UTF8.GetBytes("pismo"), pieczec, false, s =>
                s.SignedAttributes.Add(new AsnEncodedData(new Oid("1.2.616.1.999.1"), PodpisyTesty.OctetString(Encoding.UTF8.GetBytes(PodpisyTesty.DaneZp))))));
            Dodaj(zaufany, "08 podpis zaufany.p7m", "WAŻNY", "TAK");
            Dodaj(PodpisyTesty.PodpiszXades(_folder, janEc, null, null, "x1.xml"), "09 XAdES ECDSA.xml", "WAŻNY", "TAK");
            Dodaj(PodpisyTesty.PodpiszXades(_folder, pieczec, PodpisyTesty.DaneZp, null, "x2.xml"), "10 XAdES podpis zaufany.xml", "WAŻNY", "TAK");
            var dok = Path.Combine(pliki, "11 umowa.docx");
            File.WriteAllText(dok, "treść umowy");
            File.WriteAllBytes(dok + ".sig", PodpisyTesty.PodpiszCms(File.ReadAllBytes(dok), anna, true));
            oczekiwane.Add("11 umowa.docx.sig;WAŻNY;NIE");
            var dwa = PodpiszPdf(anna, wejscie: PodpiszPdf(jan));
            Dodaj(dwa, "12 dwa podpisy.pdf", "WAŻNY", "TAK");
            oczekiwane.Add("12 dwa podpisy.pdf;WAŻNY;NIE");

            File.WriteAllLines(Path.Combine(folder, "oczekiwane.csv"), oczekiwane, new UTF8Encoding(false));
            File.Copy(Pliki.Xlsx(_folder, new[] { Pliki.PeselA, "81100216357" }, new string[0]), Path.Combine(folder, "pesele.xlsx"), true);
            Ust().Lista!.Zapisz(Path.Combine(folder, "lista.txt"));
        }
    }
}
#endif
