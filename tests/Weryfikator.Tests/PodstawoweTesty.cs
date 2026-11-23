using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Xml;
using Weryfikator.Logika;
using Xunit;

namespace Weryfikator.Tests
{
    public class PodstawoweTesty
    {
        [Theory]
        [InlineData("44051401359", true)]
        [InlineData("02070803628", true)]
        [InlineData("44051401358", false)]
        [InlineData("00000000000", false)]
        [InlineData("4405140135", false)]
        public void SumaKontrolnaPesel(string pesel, bool poprawny) => Assert.Equal(poprawny, Pesel.Poprawny(pesel));

        [Fact]
        public void PeselZEtykiety()
        {
            Assert.Equal(Pliki.PeselA, Pesel.ZEtykiety("PNOPL-" + Pliki.PeselA));
            Assert.Equal(Pliki.PeselA, Pesel.ZEtykiety("PESEL: " + Pliki.PeselA));
            Assert.Equal(Pliki.PeselA, Pesel.ZEtykiety("<os:PESEL>" + Pliki.PeselA + "</os:PESEL>"));
            Assert.Null(Pesel.ZEtykiety("NIP 5170359458, tel. 12345678901"));
        }

        [Fact]
        public void ListaPeselZXlsx()
        {
            var folder = Pliki.Folder();
            // Tekst, tekst ze spacjami, liczba, liczba bez zera wiodącego, notacja naukowa, literówka.
            var plik = Pliki.Xlsx(folder, new[] { Pliki.PeselA, " 90090515836 ", "44051401358" }, new[] { "2070803628", "8.1100216357E10" });
            var l = ListaPesel.Wczytaj(plik);
            Assert.True(l.Zawiera(Pliki.PeselA));
            Assert.True(l.Zawiera("90090515836"));
            Assert.True(l.Zawiera(Pliki.PeselB));
            Assert.True(l.Zawiera("81100216357"));
            Assert.Equal(4, l.Numery.Count);
            Assert.Equal(new[] { "44051401358" }, l.Bledne);
        }

        [Fact]
        public void ListaPeselWybranaKolumna()
        {
            var folder = Pliki.Folder();
            // Kolumna A: numery (tekst i liczby), kolumna B: „Jan”; dodatkowo PESEL w kolumnie C pierwszego wiersza danych.
            var plik = Pliki.Xlsx(folder, new[] { Pliki.PeselA }, new[] { "2070803628" }, "<c r=\"C2\" t=\"inlineStr\"><is><t>81100216357</t></is></c>");
            var l = ListaPesel.Wczytaj(plik);
            Assert.Equal(3, l.Numery.Count);
            var a = l.Arkusze.Single();
            Assert.Equal(3, a.LiczbaKolumn);
            Assert.Equal("PESEL", a.Wiersze[0][0].Tekst);
            Assert.Equal("Jan", a.Wiersze[1][1].Tekst);
            Assert.Equal("2070803628", a.Wiersze[2][0].Tekst);

            l.Wybierz(0, 0);
            Assert.Equal(new[] { Pliki.PeselB, Pliki.PeselA }, l.Numery.OrderBy(x => x));
            l.Wybierz(-1, ListaPesel.NumerKolumny("C"));
            Assert.Equal(new[] { "81100216357" }, l.Numery);
            l.Wybierz(0, 1);
            Assert.Empty(l.Numery);

            Assert.Equal("A", ListaPesel.NazwaKolumny(0));
            Assert.Equal("AA", ListaPesel.NazwaKolumny(26));
            Assert.Equal(27, ListaPesel.NumerKolumny("AB"));
            Assert.Equal(1, ListaPesel.NumerKolumny("2"));

            var csv = Path.Combine(folder, "lista.csv");
            File.WriteAllText(csv, "Nazwisko;PESEL\nKowalski;" + Pliki.PeselA + "\nNowak;" + Pliki.PeselB + "\n");
            var t = ListaPesel.Wczytaj(csv);
            Assert.Equal(2, t.Numery.Count);
            t.Wybierz(0, 0);
            Assert.Empty(t.Numery);
        }

        [Fact]
        public void DaneZCertyfikatu()
        {
            var urzad = new Urzad();
            using var c = urzad.Wystaw("Zażółć Gęślą", "PNOPL-" + Pliki.PeselA);
            var d = DaneCertyfikatu.Z(c);
            Assert.Equal("Zażółć Gęślą", d.Nazwa);
            Assert.Equal(Pliki.PeselA, d.NumerPesel);
            Assert.True(d.Kwalifikowany);
            Assert.False(d.Pieczec);
            Assert.False(d.WygladaNaZaufany);
            using var p = urzad.Wystaw("Minister do spraw informatyzacji - pieczęć podpisu zaufanego", null, pieczec: true);
            var dp = DaneCertyfikatu.Z(p);
            Assert.True(dp.WygladaNaZaufany);
            Assert.True(dp.Pieczec);
            Assert.Null(dp.NumerPesel);
        }

        [Fact]
        public void ListaZaufaniaZXmlIPamiecPodreczna()
        {
            var urzad = new Urzad();
            using var podpisujacyListe = urzad.Wystaw("Operator listy", null, kwalifikowany: false);
            var xml = "<TrustServiceStatusList xmlns=\"http://uri.etsi.org/02231/v2#\"><SchemeInformation><PointersToOtherTSL>" +
                      "<OtherTSLPointer><ServiceDigitalIdentities><ServiceDigitalIdentity><DigitalId><X509Certificate>" + Convert.ToBase64String(podpisujacyListe.RawData) +
                      "</X509Certificate></DigitalId></ServiceDigitalIdentity></ServiceDigitalIdentities><TSLLocation>https://przyklad.test/tl.xml</TSLLocation>" +
                      "<AdditionalInformation><OtherInformation><SchemeTerritory>PL</SchemeTerritory></OtherInformation><OtherInformation><MimeType xmlns=\"x\">application/vnd.etsi.tsl+xml</MimeType></OtherInformation></AdditionalInformation></OtherTSLPointer>" +
                      "</PointersToOtherTSL></SchemeInformation><TrustServiceProviderList><TrustServiceProvider><TSPInformation><TSPName>\n<Name xml:lang=\"en\">Dostawca Testowy</Name></TSPName></TSPInformation><TSPServices>" +
                      "<TSPService><ServiceInformation><ServiceTypeIdentifier>http://uri.etsi.org/TrstSvc/Svctype/CA/QC</ServiceTypeIdentifier>" +
                      "<ServiceDigitalIdentity><DigitalId><X509Certificate>" + Convert.ToBase64String(urzad.Cert.RawData) + "</X509Certificate></DigitalId></ServiceDigitalIdentity>" +
                      "<ServiceStatus>http://uri.etsi.org/TrstSvc/TrustedList/Svcstatus/withdrawn</ServiceStatus><StatusStartingTime>2024-01-01T00:00:00Z</StatusStartingTime></ServiceInformation>" +
                      "<ServiceHistory><ServiceHistoryInstance><ServiceStatus>http://uri.etsi.org/TrstSvc/TrustedList/Svcstatus/granted</ServiceStatus><StatusStartingTime>2016-07-01T00:00:00Z</StatusStartingTime></ServiceHistoryInstance></ServiceHistory>" +
                      "</TSPService><TSPService><ServiceInformation><ServiceTypeIdentifier>http://uri.etsi.org/TrstSvc/Svctype/IdV</ServiceTypeIdentifier></ServiceInformation></TSPService>" +
                      "</TSPServices></TrustServiceProvider></TrustServiceProviderList></TrustServiceStatusList>";
            var doc = new XmlDocument { PreserveWhitespace = true };
            doc.LoadXml(xml);

            Assert.Equal("lista nie jest podpisana", ListaZaufania.SprawdzPodpisListy(doc, new() { podpisujacyListe }));
            Podpisz(doc, podpisujacyListe);
            var wsk = ListaZaufania.Wskazniki(doc).Single();
            Assert.Equal("PL", wsk.Kraj);
            Assert.Null(ListaZaufania.SprawdzPodpisListy(doc, wsk.Certy));
            using var obcy = urzad.Wystaw("Ktoś inny", null);
            Assert.NotNull(ListaZaufania.SprawdzPodpisListy(doc, new() { obcy }));

            var wpis = ListaZaufania.Uslugi(doc, "PL").Single();
            Assert.Equal("Dostawca Testowy", wpis.Dostawca);
            Assert.True(wpis.AktywnaW(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
            Assert.False(wpis.AktywnaW(new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc)));

            // Zmiana treści po podpisaniu unieważnia podpis listy.
            doc.GetElementsByTagName("TSLLocation", "*")[0]!.InnerText = "https://inny.test/";
            Assert.NotNull(ListaZaufania.SprawdzPodpisListy(doc, wsk.Certy));

            var lista = new ListaZaufania { Pobrano = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc), Wpisy = { wpis } };
            var plik = Path.Combine(Pliki.Folder(), "lista.txt");
            lista.Zapisz(plik);
            var wczytana = ListaZaufania.Wczytaj(plik)!;
            Assert.Equal(lista.Pobrano, wczytana.Pobrano);
            Assert.Single(wczytana.Znajdz(urzad.Cert));
            Assert.False(wczytana.Wpisy[0].AktywnaW(DateTime.UtcNow));
        }

        internal static void Podpisz(XmlDocument doc, X509Certificate2 cert, string? metoda = null)
        {
            PodpisXml.Rejestruj();
            AsymmetricAlgorithm klucz = (AsymmetricAlgorithm?)cert.GetRSAPrivateKey() ?? cert.GetECDsaPrivateKey()!;
            var sx = new SignedXml(doc) { SigningKey = klucz };
            if (metoda != null) sx.SignedInfo.SignatureMethod = metoda;
            var r = new Reference("") { DigestMethod = "http://www.w3.org/2001/04/xmlenc#sha256" };
            r.AddTransform(new XmlDsigEnvelopedSignatureTransform());
            r.AddTransform(new XmlDsigExcC14NTransform());
            sx.AddReference(r);
            var ki = new KeyInfo();
            ki.AddClause(new KeyInfoX509Data(cert));
            sx.KeyInfo = ki;
            sx.ComputeSignature();
            doc.DocumentElement!.AppendChild(doc.ImportNode(sx.GetXml(), true));
        }
    }
}
