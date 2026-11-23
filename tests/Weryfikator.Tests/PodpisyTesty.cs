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
using Weryfikator.Logika;
using Xunit;

namespace Weryfikator.Tests
{
    /// <summary>Podpisy CAdES i XAdES — testy wspólne dla .NET 8 i .NET Framework 4.8.</summary>
    public class PodpisyTesty
    {
        private readonly Urzad _urzad = new Urzad();
        private readonly string _folder = Pliki.Folder();

        private Ustawienia Ust()
        {
            var u = new Ustawienia { Online = false, Lista = new ListaZaufania { Wpisy = { _urzad.Wpis() } } };
            u.Pesele = ListaPesel.Wczytaj(Pliki.Xlsx(_folder, new[] { Pliki.PeselA }, new string[0]));
            return u;
        }

        internal const string DaneZp = "<ppZP:PodpisZP xmlns:ppZP=\"http://crd.gov.pl/xml/schematy/ppzp/\" xmlns:os=\"http://crd.gov.pl/xml/schematy/osoba/2009/03/06/\"><ppZP:DaneZP><ppZP:DaneZPOsobyFizycznej>" +
            "<os:Nazwisko>Gęślą</os:Nazwisko><os:Imie>Zażółć</os:Imie><os:PESEL>" + Pliki.PeselA + "</os:PESEL></ppZP:DaneZPOsobyFizycznej></ppZP:DaneZP></ppZP:PodpisZP>";

        private static string Opis(WynikPodpisu w) => w.StatusTekst + ": " + string.Join(" | ", w.Uwagi);

        // ---------- CAdES

        internal static byte[] PodpiszCms(byte[] dane, X509Certificate2 cert, bool osobno, Action<CmsSigner>? zmien = null)
        {
            var cms = new SignedCms(new ContentInfo(dane), osobno);
            var s = new CmsSigner(SubjectIdentifierType.IssuerAndSerialNumber, cert) { IncludeOption = X509IncludeOption.EndCertOnly };
            s.SignedAttributes.Add(new Pkcs9SigningTime(DateTime.UtcNow));
            zmien?.Invoke(s);
            cms.ComputeSignature(s);
            return cms.Encode();
        }

        [Fact]
        public void CadesPodpisZewnetrznyIWewnetrzny()
        {
            using var cert = _urzad.Wystaw("Jan Kowalski", "PNOPL-" + Pliki.PeselA);
            var dok = Path.Combine(_folder, "umowa.docx");
            File.WriteAllBytes(dok, Encoding.UTF8.GetBytes("treść umowy"));
            var sig = dok + ".sig";
            File.WriteAllBytes(sig, PodpiszCms(File.ReadAllBytes(dok), cert, true));
            var w = Sprawdzanie.Plik(sig, Ust()).Single();
            Assert.True(w.Status == Status.Wazny, Opis(w));
            Assert.Equal(Rodzaj.Kwalifikowany, w.Rodzaj);
            Assert.False(w.CzasZeZnacznika);
            Assert.NotNull(w.CzasUtc);

            File.WriteAllBytes(dok, Encoding.UTF8.GetBytes("treść zmieniona"));
            Assert.Equal(Status.Niewazny, Sprawdzanie.Plik(sig, Ust()).Single().Status);
            File.Delete(dok);
            Assert.NotEqual(Status.Wazny, Sprawdzanie.Plik(sig, Ust()).Single().Status);

            var p7m = Path.Combine(_folder, "pismo.p7m");
            File.WriteAllBytes(p7m, PodpiszCms(Encoding.UTF8.GetBytes("pismo"), cert, false));
            var w2 = Sprawdzanie.Plik(p7m, Ust()).Single();
            Assert.True(w2.Status == Status.Wazny, Opis(w2));
        }

        [Fact]
        public void PodpisZaufanyPeselZAtrybutowPodpisanych()
        {
            // Podpis zaufany: pieczęć ministra, a dane osoby w podpisanych atrybutach.
            using var pieczec = _urzad.Wystaw("Minister do spraw informatyzacji - pieczęć podpisu zaufanego", null, pieczec: true);
            var xml = DaneZp;
            var p7m = Path.Combine(_folder, "zaufany.p7m");
            File.WriteAllBytes(p7m, PodpiszCms(Encoding.UTF8.GetBytes("pismo"), pieczec, false, s =>
                s.SignedAttributes.Add(new AsnEncodedData(new Oid("1.2.616.1.999.1"), OctetString(Encoding.UTF8.GetBytes(xml))))));
            var w = Sprawdzanie.Plik(p7m, Ust()).Single();
            Assert.True(w.Status == Status.Wazny, Opis(w));
            Assert.Equal(Rodzaj.Zaufany, w.Rodzaj);
            Assert.Equal(Pliki.PeselA, w.Pesel);
            Assert.Equal("Zażółć Gęślą", w.Podpisujacy);
            Assert.True(w.WPorzadku);
        }

        internal static byte[] OctetString(byte[] d)
        {
            return DerT.Octet(d);
        }

        // ---------- XAdES

        private string PodpiszXades(X509Certificate2 cert, string? daneZp, string? plikObok, string nazwa = "pismo.xml") => PodpiszXades(_folder, cert, daneZp, plikObok, nazwa);

        internal static string PodpiszXades(string _folder, X509Certificate2 cert, string? daneZp, string? plikObok, string nazwa)
        {
            PodpisXml.Rejestruj();
            var doc = new XmlDocument { PreserveWhitespace = true };
            doc.LoadXml("<Pismo><Tresc>Wniosek o przyjęcie</Tresc></Pismo>");
            AsymmetricAlgorithm klucz = (AsymmetricAlgorithm?)cert.GetRSAPrivateKey() ?? cert.GetECDsaPrivateKey()!;
            var sx = new SignedXml(doc) { SigningKey = klucz };
            if (klucz is ECDsa) sx.SignedInfo.SignatureMethod = "http://www.w3.org/2001/04/xmldsig-more#ecdsa-sha384";
            var r = new Reference("") { DigestMethod = "http://www.w3.org/2001/04/xmlenc#sha256" };
            r.AddTransform(new XmlDsigEnvelopedSignatureTransform());
            sx.AddReference(r);
            var wlasciwosci = new XmlDocument();
            wlasciwosci.LoadXml("<xades:QualifyingProperties xmlns:xades=\"http://uri.etsi.org/01903/v1.3.2#\"><xades:SignedProperties Id=\"sp1\"><xades:SignedSignatureProperties>" +
                                "<xades:SigningTime>" + DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ") + "</xades:SigningTime></xades:SignedSignatureProperties>" + (daneZp ?? "") +
                                "</xades:SignedProperties></xades:QualifyingProperties>");
            sx.AddObject(new DataObject { Data = wlasciwosci.ChildNodes, Id = "obj1" });
            sx.AddReference(new Reference("#obj1") { DigestMethod = "http://www.w3.org/2001/04/xmlenc#sha256" });
            var ki = new KeyInfo();
            ki.AddClause(new KeyInfoX509Data(cert));
            sx.KeyInfo = ki;
            var sciezka = Path.Combine(_folder, nazwa);
            doc.Save(sciezka); // adres bazowy dla odwołania do pliku obok
            var zBaza = new XmlDocument { PreserveWhitespace = true };
            zBaza.Load(sciezka);
            sx = PrzeniesNa(_folder, zBaza, sx, klucz);
            sx.ComputeSignature();
            var podpis = (XmlElement)zBaza.DocumentElement!.AppendChild(zBaza.ImportNode(sx.GetXml(), true))!;
            if (plikObok != null)
            {
                // SignedXml nie liczy skrótów plików zewnętrznych: odwołanie dopisywane ręcznie, a wartość podpisu
                // liczona od nowa nad kanoniczną postacią SignedInfo.
                var si = (XmlElement)podpis.GetElementsByTagName("SignedInfo", PodpisXml.NsDs)[0]!;
                var r2 = zBaza.CreateElement("Reference", PodpisXml.NsDs);
                r2.SetAttribute("URI", Uri.EscapeDataString(Path.GetFileName(plikObok)));
                var dm = zBaza.CreateElement("DigestMethod", PodpisXml.NsDs);
                dm.SetAttribute("Algorithm", "http://www.w3.org/2001/04/xmlenc#sha256");
                var dv = zBaza.CreateElement("DigestValue", PodpisXml.NsDs);
                using (var sha = SHA256.Create()) dv.InnerText = Convert.ToBase64String(sha.ComputeHash(File.ReadAllBytes(plikObok)));
                r2.AppendChild(dm); r2.AppendChild(dv); si.AppendChild(r2);
                var osobny = new XmlDocument { PreserveWhitespace = true };
                osobny.LoadXml(si.OuterXml);
                var c14n = new XmlDsigC14NTransform();
                c14n.LoadInput(osobny);
                byte[] kanon;
                using (var ms = new MemoryStream()) { ((Stream)c14n.GetOutput(typeof(Stream))).CopyTo(ms); kanon = ms.ToArray(); }
                var wartosc = klucz is RSA rsa ? rsa.SignData(kanon, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1) : ((ECDsa)klucz).SignData(kanon, HashAlgorithmName.SHA384);
                podpis.GetElementsByTagName("SignatureValue", PodpisXml.NsDs)[0]!.InnerText = Convert.ToBase64String(wartosc);
            }
            zBaza.Save(sciezka);
            return sciezka;
        }

        private static SignedXml PrzeniesNa(string _folder, XmlDocument doc, SignedXml wzor, AsymmetricAlgorithm klucz)
        {
            var sx = new SignedXml(doc) { SigningKey = klucz, KeyInfo = wzor.KeyInfo, Resolver = new ResolverLokalny(_folder) };
            sx.SignedInfo.SignatureMethod = wzor.SignedInfo.SignatureMethod;
            foreach (DataObject o in wzor.Signature.ObjectList) sx.AddObject(o);
            foreach (Reference r in wzor.SignedInfo.References) sx.AddReference(r);
            return sx;
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void XadesPodpisKwalifikowany(bool ecdsa)
        {
            using var cert = _urzad.Wystaw("Jan Kowalski", "PNOPL-" + Pliki.PeselA, ecdsa: ecdsa);
            var plik = PodpiszXades(cert, null, null);
            var w = Sprawdzanie.Plik(plik, Ust()).Single();
            Assert.True(w.Status == Status.Wazny, Opis(w));
            Assert.Equal(Rodzaj.Kwalifikowany, w.Rodzaj);
            Assert.Equal(Pliki.PeselA, w.Pesel);
            Assert.NotNull(w.CzasUtc);

            File.WriteAllText(plik, File.ReadAllText(plik).Replace("przyjęcie", "odrzucenie"));
            Assert.Equal(Status.Niewazny, Sprawdzanie.Plik(plik, Ust()).Single().Status);
        }

        [Fact]
        public void XadesPodpisZaufanyIPlikObok()
        {
            using var pieczec = _urzad.Wystaw("Minister do spraw informatyzacji - pieczęć podpisu zaufanego", null, pieczec: true);
            string? obok = Path.Combine(_folder, "załącznik nr 1.pdf");
            File.WriteAllText(obok, "zawartość");
            var dane = "<ppZP:PodpisZP xmlns:ppZP=\"http://crd.gov.pl/xml/schematy/ppzp/\" xmlns:os=\"http://crd.gov.pl/xml/schematy/osoba/2009/03/06/\">" +
                       "<os:Imie>Anna</os:Imie><os:Nazwisko>Nowak</os:Nazwisko><os:PESEL>" + Pliki.PeselB + "</os:PESEL></ppZP:PodpisZP>";
            var plik = PodpiszXades(pieczec, dane, obok);
            var w = Sprawdzanie.Plik(plik, Ust()).Single();
            Assert.True(w.Status == Status.Wazny, Opis(w));
            Assert.Equal(Rodzaj.Zaufany, w.Rodzaj);
            Assert.Equal(Pliki.PeselB, w.Pesel);
            Assert.Equal("Anna Nowak", w.Podpisujacy);
            Assert.False(w.NaLiscie);

            // Zmiana danych osoby (odwołanie wewnętrzne) albo pliku obok unieważnia podpis.
            var xml = File.ReadAllText(plik);
            File.WriteAllText(plik, xml.Replace(">Anna<", ">Hanna<"));
            Assert.Equal(Status.Niewazny, Sprawdzanie.Plik(plik, Ust()).Single().Status);
            File.WriteAllText(plik, xml);
            Assert.Equal(Status.Wazny, Sprawdzanie.Plik(plik, Ust()).Single().Status);
            File.WriteAllText(obok, "podmieniona");
            var zly = Sprawdzanie.Plik(plik, Ust()).Single();
            Assert.Equal(Status.Niewazny, zly.Status);
            Assert.Contains(zly.Uwagi, x => x.Contains("zmieniony po podpisaniu"));
        }

        [Fact]
        public void XmlBezPodpisuIRaport()
        {
            var plik = Path.Combine(_folder, "zwykly.xml");
            File.WriteAllText(plik, "<a/>");
            var w = Sprawdzanie.Plik(plik, Ust());
            Assert.Equal(Status.BrakPodpisu, w.Single().Status);
            var csv = Path.Combine(_folder, "raport.csv");
            Raport.ZapiszCsv(csv, w);
            var tekst = File.ReadAllText(csv);
            Assert.Contains("BRAK PODPISU", tekst);
            Assert.Contains("zwykly.xml", tekst);
        }
    }
}
