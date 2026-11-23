using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace Weryfikator.Logika
{
    internal sealed class Ustawienia
    {
        public ListaZaufania? Lista;
        public ListaPesel? Pesele;
        public List<X509Certificate2> CertyfikatyUzytkownika = new List<X509Certificate2>();
        public bool Online = true;
        public DateTime Teraz = DateTime.UtcNow;
    }

    /// <summary>Sprawdzanie podpisów w plikach PDF (PAdES), XML (XAdES) oraz .sig/.p7s/.p7m (CAdES).</summary>
    internal static class Sprawdzanie
    {
        public static readonly string[] Rozszerzenia = { ".pdf", ".xml", ".xades", ".sig", ".p7s", ".p7m" };

        public static bool Obslugiwany(string plik) => Array.IndexOf(Rozszerzenia, Path.GetExtension(plik).ToLowerInvariant()) >= 0;

        public static List<WynikPodpisu> Plik(string sciezka, Ustawienia u)
        {
            List<WynikPodpisu> wyniki;
            try
            {
                var roz = Path.GetExtension(sciezka).ToLowerInvariant();
                var dane = File.ReadAllBytes(sciezka);
                if (roz == ".pdf" || Zaczyna(dane, "%PDF")) wyniki = Pdf(dane, u);
                else if (roz == ".xml" || roz == ".xades" || WygladaNaXml(dane)) wyniki = Xades(sciezka, dane, u);
                else wyniki = Cades(sciezka, dane, u);
            }
            catch (Exception e)
            {
                wyniki = new List<WynikPodpisu> { new WynikPodpisu { Status = Status.Blad, Uwagi = { "Nie można sprawdzić pliku: " + e.Message } } };
            }
            if (wyniki.Count == 0) wyniki.Add(new WynikPodpisu { Status = Status.BrakPodpisu, Uwagi = { "Plik nie zawiera podpisu elektronicznego." } });
            int nr = 0;
            foreach (var w in wyniki)
            {
                w.Plik = sciezka;
                w.BezListyPesel = u.Pesele == null;
                if (w.Status != Status.BrakPodpisu && w.Status != Status.Blad || wyniki.Count > 1) w.Nr = ++nr;
                if (w.Pesel != null && u.Pesele != null)
                {
                    w.NaLiscie = u.Pesele.Zawiera(w.Pesel);
                    if (w.NaLiscie == false) w.Uwagi.Add("Numeru PESEL podpisującego nie ma na liście.");
                }
            }
            return wyniki;
        }

        private static bool Zaczyna(byte[] d, string s)
        {
            int p = 0;
            if (d.Length >= 3 && d[0] == 0xEF && d[1] == 0xBB && d[2] == 0xBF) p = 3;
            while (p < d.Length && p < 1024 && (d[p] == ' ' || d[p] == '\r' || d[p] == '\n' || d[p] == '\t')) p++;
            if (p + s.Length > d.Length) return false;
            for (int i = 0; i < s.Length; i++) if (d[p + i] != s[i]) return false;
            return true;
        }

        private static bool WygladaNaXml(byte[] d) => Zaczyna(d, "<");

        // ---------------------------------------------------------------- PDF

        private static readonly byte[] ZnByteRange = Encoding.ASCII.GetBytes("/ByteRange");
        private static readonly Regex WzZakres = new Regex(@"^\s*\[\s*(\d+)\s+(\d+)\s+(\d+)\s+(\d+)\s*\]");

        private static readonly Regex WzCzasM = new Regex(@"/M\s*\(D:(\d{14})([+\-Z])?(\d\d)?'?(\d\d)?");

        internal static List<WynikPodpisu> Pdf(byte[] d, Ustawienia u)
        {
            var wyniki = new List<WynikPodpisu>();
            var podpisy = new List<KeyValuePair<long, WynikPodpisu>>();
            long najdalej = 0;
            var lat = Encoding.GetEncoding(28591);
            for (int poz = Szukaj(d, ZnByteRange, 0); poz >= 0; poz = Szukaj(d, ZnByteRange, poz + 1))
            {
                var m = WzZakres.Match(lat.GetString(d, poz + ZnByteRange.Length, Math.Min(100, d.Length - poz - ZnByteRange.Length)));
                if (!m.Success) continue;
                long a = long.Parse(m.Groups[1].Value), b = long.Parse(m.Groups[2].Value), c = long.Parse(m.Groups[3].Value), e = long.Parse(m.Groups[4].Value);
                // Zakres musi obejmować wszystko poza samą wartością /Contents <…> — inaczej to nie jest poprawny podpis.
                if (a != 0 || b <= 0 || c <= b + 2 || e < 0 || c + e > d.Length || d[b] != '<' || d[c - 1] != '>' || poz > c + e) continue;
                byte[] cms;
                try { cms = ZHex(d, (int)b + 1, (int)c - 1); } catch (FormatException) { continue; }
                var tresc = new byte[b + e];
                Buffer.BlockCopy(d, 0, tresc, 0, (int)b);
                Buffer.BlockCopy(d, (int)c, tresc, (int)b, (int)e);

                var str = Cms.Rozbierz(Cms.Przytnij(cms));
                if (str != null && str.TypTresci == Cms.OidTstInfo)
                {
                    // Znacznik czasu dokumentu (PAdES-LTA) — nie jest podpisem osoby; liczy się tylko to, że obejmuje plik.
                    najdalej = Math.Max(najdalej, c + e);
                    continue;
                }
                // Słownik podpisu (jest w podpisanej części pliku) — tam bywa nazwisko podpisującego.
                int odSl = Math.Max(0, Math.Min(poz, (int)b) - 3000);
                var slownik = lat.GetString(d, odSl, (int)b - odSl) + lat.GetString(d, (int)c, (int)Math.Min(3000, e));
                // PAdES nie ma atrybutu „czas podpisu” — deklarowany czas jest we wpisie /M słownika (najbliższym /ByteRange).
                DateTime? czasM = null;
                int odleglosc = int.MaxValue, pozWSlowniku = Math.Min(poz, (int)b) - odSl;
                foreach (Match mm in WzCzasM.Matches(slownik))
                {
                    if (Math.Abs(mm.Index - pozWSlowniku) >= odleglosc) continue;
                    if (!DateTime.TryParseExact(mm.Groups[1].Value, "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var tm)) continue;
                    if (mm.Groups[2].Value == "+" || mm.Groups[2].Value == "-")
                    {
                        var przes = new TimeSpan(mm.Groups[3].Success ? int.Parse(mm.Groups[3].Value) : 0, mm.Groups[4].Success ? int.Parse(mm.Groups[4].Value) : 0, 0);
                        tm = mm.Groups[2].Value == "+" ? tm - przes : tm + przes;
                    }
                    czasM = tm; odleglosc = Math.Abs(mm.Index - pozWSlowniku);
                }
                foreach (var wc in Cms.Sprawdz(cms, tresc))
                {
                    if (wc.CzasDeklarowany == null) wc.CzasDeklarowany = czasM;
                    var w = Ocen(wc, u, slownik);
                    wyniki.Add(w);
                    podpisy.Add(new KeyValuePair<long, WynikPodpisu>(c + e, w));
                }
                najdalej = Math.Max(najdalej, c + e);
            }
            // Dopisanie treści po ostatnim podpisie/znaczniku — podpis jej nie obejmuje.
            if (wyniki.Count > 0 && d.Length - najdalej > 0 && !SameBialeZnaki(d, najdalej))
                foreach (var p in podpisy)
                {
                    if (p.Value.Status == Status.Wazny) p.Value.Status = Status.Nieokreslony;
                    p.Value.Uwagi.Add("Po złożeniu podpisu do pliku dopisano dane, których podpis nie obejmuje — sprawdź dokument w programie Adobe Reader.");
                }
            return wyniki.OrderBy(w => podpisy.First(p => p.Value == w).Key).ToList();
        }

        private static bool SameBialeZnaki(byte[] d, long od)
        {
            for (long i = od; i < d.Length; i++) if (d[i] != '\r' && d[i] != '\n' && d[i] != ' ') return false;
            return true;
        }

        private static int Szukaj(byte[] d, byte[] wz, int od)
        {
            for (int i = od; i <= d.Length - wz.Length; i++)
            {
                if (d[i] != wz[0]) continue;
                int j = 1;
                while (j < wz.Length && d[i + j] == wz[j]) j++;
                if (j == wz.Length) return i;
            }
            return -1;
        }

        private static byte[] ZHex(byte[] d, int od, int doPoz)
        {
            var ms = new MemoryStream((doPoz - od) / 2);
            int pol = -1;
            for (int i = od; i < doPoz; i++)
            {
                int z = d[i], v;
                if (z >= '0' && z <= '9') v = z - '0';
                else if (z >= 'a' && z <= 'f') v = z - 'a' + 10;
                else if (z >= 'A' && z <= 'F') v = z - 'A' + 10;
                else if (z == ' ' || z == '\r' || z == '\n' || z == '\t') continue;
                else throw new FormatException();
                if (pol < 0) pol = v; else { ms.WriteByte((byte)(pol * 16 + v)); pol = -1; }
            }
            return ms.ToArray();
        }

        // ---------------------------------------------------------------- CAdES

        internal static List<WynikPodpisu> Cades(string sciezka, byte[] dane, Ustawienia u)
        {
            if (Zaczyna(dane, "-----BEGIN"))
            {
                var t = Encoding.ASCII.GetString(dane);
                dane = Convert.FromBase64String(Regex.Replace(t, @"-----[^-]+-----|\s", ""));
            }
            dane = Cms.Przytnij(dane);
            var str = Cms.Rozbierz(dane);
            if (str == null)
                return new List<WynikPodpisu> { new WynikPodpisu { Status = Status.BrakPodpisu, Uwagi = { "Plik nie jest podpisem w formacie CAdES/PKCS#7." } } };
            byte[]? tresc = null;
            string? uwaga = null;
            if (str.Tresc == null)
            {
                var obok = PlikPodpisany(sciezka);
                if (obok != null) { tresc = File.ReadAllBytes(obok); uwaga = "Podpis zewnętrzny pliku „" + Path.GetFileName(obok) + "”."; }
            }
            var wyniki = Cms.Sprawdz(dane, tresc).Select(wc => Ocen(wc, u, "")).ToList();
            if (uwaga != null) foreach (var w in wyniki) w.Uwagi.Insert(0, uwaga);
            return wyniki;
        }

        /// <summary>Plik podpisany podpisem zewnętrznym: „umowa.pdf.sig” → „umowa.pdf”, „umowa.sig” → „umowa.*”.</summary>
        private static string? PlikPodpisany(string podpis)
        {
            var folder = Path.GetDirectoryName(Path.GetFullPath(podpis))!;
            var bez = Path.Combine(folder, Path.GetFileNameWithoutExtension(podpis));
            if (File.Exists(bez)) return bez;
            var kandydaci = Directory.GetFiles(folder, Path.GetFileNameWithoutExtension(podpis) + ".*")
                .Where(p => !Obslugiwany(p) || Path.GetExtension(p).ToLowerInvariant() == ".pdf" || Path.GetExtension(p).ToLowerInvariant() == ".xml")
                .Where(p => !string.Equals(p, Path.GetFullPath(podpis), StringComparison.OrdinalIgnoreCase)).ToList();
            return kandydaci.Count == 1 ? kandydaci[0] : null;
        }

        // ---------------------------------------------------------------- XAdES

        private sealed class SignedXmlXades : SignedXml
        {
            public SignedXmlXades(XmlDocument d) : base(d) { }
            public XmlElement? Znajdz(XmlDocument d, string id) => GetIdElement(d, id);
        }

        internal static List<WynikPodpisu> Xades(string sciezka, byte[] dane, Ustawienia u)
        {
            PodpisXml.Rejestruj();
            var pelna = Path.GetFullPath(sciezka);
            XmlDocument doc;
            using (var ms = new MemoryStream(dane)) doc = PodpisXml.Wczytaj(ms, new Uri(pelna).AbsoluteUri);
            var wyniki = new List<WynikPodpisu>();
            var podpisy = doc.GetElementsByTagName("Signature", PodpisXml.NsDs).Cast<XmlElement>().ToList();
            foreach (var el in podpisy)
            {
                var wc = new WynikCms();
                var sx = new SignedXmlXades(doc) { Resolver = new ResolverLokalny(Path.GetDirectoryName(pelna)!) };
                string tekstPodpisu = el.OuterXml;
                try
                {
                    sx.LoadXml(el);
                    var certy = PodpisXml.Certyfikaty(el);
                    foreach (var c in certy) wc.Certy.Add(c);
                    if (certy.Count == 0) wc.Blad = "Podpis nie zawiera certyfikatu podpisującego.";
                    else
                    {
                        string? blad;
                        wc.Cert = PodpisXmlZPlikami.MaOdwolaniaDoPlikow(sx)
                            ? PodpisXmlZPlikami.Sprawdz(doc, el, sx, certy, Path.GetDirectoryName(pelna)!, d => new SignedXmlXades(d), out blad)
                            : PodpisXml.Sprawdz(sx, certy, out blad);
                        wc.Poprawny = wc.Cert != null;
                        if (!wc.Poprawny) { wc.Blad = blad; wc.Cert = certy[0]; }
                    }
                    // Dane podpisane: tylko elementy objęte odwołaniami podpisu (tam są dane podpisu zaufanego).
                    var sb = new StringBuilder();
                    var objete = new List<XmlElement>();
                    foreach (Reference r in sx.SignedInfo.References)
                    {
                        if (string.IsNullOrEmpty(r.Uri) || r.Uri[0] != '#') continue;
                        var cel = sx.Znajdz(doc, r.Uri.Substring(1));
                        if (cel == null) continue;
                        objete.Add(cel);
                        if (cel.OuterXml.Length < 2000000) sb.Append(cel.OuterXml).Append('\n');
                    }
                    tekstPodpisu = sb.ToString();
                    var czas = el.GetElementsByTagName("SigningTime", "*").Cast<XmlElement>().FirstOrDefault();
                    bool czasPodpisany = false;
                    for (XmlNode? n = czas; n != null && !czasPodpisany; n = n.ParentNode) czasPodpisany = objete.Contains(n);
                    if (czas != null && czasPodpisany &&
                        DateTime.TryParse(czas.InnerText.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var t))
                        wc.CzasDeklarowany = t;
                    ZnacznikXades(el, wc);
                }
                catch (Exception e) { wc.Blad = "Nie udało się sprawdzić podpisu XML: " + e.Message; }
                wc.TekstAtrybutow = tekstPodpisu;
                wc.TekstToBajty = false;
                wyniki.Add(Ocen(wc, u, ""));
            }
            return wyniki;
        }

        private static void ZnacznikXades(XmlElement podpis, WynikCms wc)
        {
            var wartosc = podpis.ChildNodes.OfType<XmlElement>().FirstOrDefault(e => e.LocalName == "SignatureValue");
            if (wartosc == null) return;
            foreach (XmlElement ts in podpis.GetElementsByTagName("SignatureTimeStamp", "*"))
            {
                var enc = ts.GetElementsByTagName("EncapsulatedTimeStamp", "*").Cast<XmlElement>().FirstOrDefault();
                if (enc == null) continue;
                var z = Cms.CzytajZnacznik(Convert.FromBase64String(enc.InnerText), out var blad);
                if (z == null) { wc.UwagaZnacznika = "Znacznik czasu jest nieprawidłowy: " + blad; continue; }
                var metoda = ts.GetElementsByTagName("CanonicalizationMethod", PodpisXml.NsDs).Cast<XmlElement>().FirstOrDefault()?.GetAttribute("Algorithm");
                using (var h = Cms.Skrot(z.OidSkrotu))
                {
                    if (h == null || !h.ComputeHash(PodpisXmlZPlikami.Kanonicznie(wartosc, metoda)).SequenceEqual(z.Odcisk)) { wc.UwagaZnacznika = "Znacznik czasu nie dotyczy tego podpisu."; continue; }
                }
                if (wc.Znacznik == null || z.Czas < wc.Znacznik.Czas) wc.Znacznik = z;
            }
        }

        // ---------------------------------------------------------------- ocena

        private static readonly Regex WzImie = new Regex(@"<(?:\w+:)?Imi[eę][^>]*>\s*([^<]+?)\s*<", RegexOptions.IgnoreCase);
        private static readonly Regex WzNazwisko = new Regex(@"<(?:\w+:)?Nazwisko[^>]*>\s*([^<]+?)\s*<", RegexOptions.IgnoreCase);

        internal static WynikPodpisu Ocen(WynikCms wc, Ustawienia u, string slownikPdf)
        {
            var w = new WynikPodpisu();
            if (wc.Cert == null)
            {
                w.Status = Status.Blad;
                w.Uwagi.Add(wc.Blad ?? "Nie można odczytać podpisu.");
                return w;
            }
            var dc = DaneCertyfikatu.Z(wc.Cert);
            w.Podpisujacy = dc.Nazwa;
            w.Pesel = dc.NumerPesel;
            w.Wystawca = dc.Wystawca;

            // Czas podpisu: zaufany znacznik czasu, a gdy go nie ma — czas podany przez podpisującego.
            DateTime czas = u.Teraz;
            if (wc.CzasDeklarowany != null) { czas = wc.CzasDeklarowany.Value; w.CzasUtc = czas; }
            if (wc.UwagaZnacznika != null) w.Uwagi.Add(wc.UwagaZnacznika);
            if (wc.Znacznik != null)
            {
                var lz = Lancuch.Zbuduj(wc.Znacznik.Cert!, wc.Znacznik.Certy, wc.Znacznik.Czas, u.Lista, u.CertyfikatyUzytkownika, false, "TSA");
                if (lz.Kotwica != Kotwica.Brak && lz.Bledy.Count == 0)
                {
                    czas = wc.Znacznik.Czas; w.CzasUtc = czas; w.CzasZeZnacznika = true;
                }
                else w.Uwagi.Add("Znacznik czasu pochodzi od wystawcy spoza listy zaufania — przyjęto czas podany przez podpisującego.");
            }
            if (czas > u.Teraz.AddMinutes(10)) w.Uwagi.Add("Podany czas podpisu jest z przyszłości.");

            var l = Lancuch.Zbuduj(wc.Cert, wc.Certy, czas, u.Lista, u.CertyfikatyUzytkownika, u.Online, "CA/QC");
            var status = Status.Wazny;
            void Gorszy(Status s) { if (s > status) status = s; }

            if (!wc.Poprawny) { Gorszy(Status.Niewazny); w.Uwagi.Add(wc.Blad ?? "Podpis jest nieprawidłowy."); }

            switch (l.Kotwica)
            {
                case Kotwica.ListaUE:
                    if (l.Wpis != null) w.Wystawca = dc.Wystawca + " (" + l.Wpis.Kraj + ", lista zaufania UE)";
                    if (dc.WygladaNaZaufany) w.Rodzaj = Rodzaj.Zaufany;
                    else if (dc.Kwalifikowany) w.Rodzaj = dc.Pieczec ? Rodzaj.PieczecKwalifikowana : Rodzaj.Kwalifikowany;
                    else
                    {
                        w.Rodzaj = Rodzaj.Inny; Gorszy(Status.Nieokreslony);
                        w.Uwagi.Add("Wystawca jest na liście zaufania UE, ale certyfikat nie jest oznaczony jako kwalifikowany.");
                    }
                    if (!l.UslugaAktywna)
                    {
                        Gorszy(Status.Nieokreslony);
                        w.Uwagi.Add("Usługa wystawcy miała w czasie podpisu na liście zaufania status „" + l.Wpis!.StatusW(czas) + "”.");
                    }
                    break;
                case Kotwica.System:
                case Kotwica.Uzytkownik:
                    if (dc.WygladaNaOsobisty) w.Rodzaj = Rodzaj.Osobisty;
                    else
                    {
                        w.Rodzaj = Rodzaj.Inny; Gorszy(Status.Nieokreslony);
                        w.Uwagi.Add("Podpis niekwalifikowany: wystawca certyfikatu jest zaufany na tym komputerze, ale nie ma go na liście zaufania UE.");
                    }
                    break;
                default:
                    if (dc.WygladaNaOsobisty)
                    {
                        w.Rodzaj = Rodzaj.Osobisty; Gorszy(Status.Nieokreslony);
                        w.Uwagi.Add("Podpis osobisty (e-dowód): brak zaufanego certyfikatu głównego. Dodaj certyfikaty urzędu e-dowodu przyciskiem „Zaufane certyfikaty…” i sprawdź ponownie." +
                                    (l.Korzen != null ? " Ścieżka kończy się na: " + l.Korzen + "." : ""));
                    }
                    else if (u.Lista == null)
                    {
                        w.Rodzaj = Rodzaj.Inny; Gorszy(Status.Nieokreslony);
                        w.Uwagi.Add("Nie pobrano listy zaufania UE — nie można ocenić wystawcy certyfikatu.");
                    }
                    else
                    {
                        w.Rodzaj = Rodzaj.Inny; Gorszy(Status.Niewazny);
                        w.Uwagi.Add("Wystawcy certyfikatu („" + dc.Wystawca + "”) nie ma na liście zaufania UE ani wśród zaufanych certyfikatów.");
                    }
                    break;
            }

            if (l.Uniewazniony && w.CzasZeZnacznika)
            {
                Gorszy(Status.Nieokreslony);
                w.Uwagi.Add("Certyfikat jest unieważniony — podpis pozostaje ważny tylko, jeśli unieważnienie nastąpiło po dacie znacznika czasu.");
            }
            else if (l.Bledy.Count > 0) Gorszy(Status.Niewazny);
            w.Uwagi.AddRange(l.Bledy);

            if (status != Status.Niewazny)
            {
                if (!w.CzasZeZnacznika && wc.Cert.NotAfter.ToUniversalTime() < u.Teraz && !l.NiewaznyCzasowo)
                {
                    Gorszy(Status.Nieokreslony);
                    w.Uwagi.Add($"Certyfikat wygasł {wc.Cert.NotAfter:dd.MM.yyyy}, a podpis nie ma zaufanego znacznika czasu — nie da się potwierdzić, że złożono go w okresie ważności certyfikatu.");
                }
                if (l.BezSprawdzeniaUniewaznienia && l.Kotwica != Kotwica.Brak)
                {
                    if (u.Online) { Gorszy(Status.Nieokreslony); w.Uwagi.Add("Nie udało się sprawdzić, czy certyfikat nie został unieważniony (brak dostępu do list CRL/OCSP)."); }
                    else w.Uwagi.Add("Nie sprawdzano unieważnienia certyfikatu (opcja wyłączona).");
                }
            }

            // Podpis zaufany składa pieczęć ministra; dane osoby (imię, nazwisko, PESEL) są w podpisanych atrybutach.
            if (w.Rodzaj == Rodzaj.Zaufany || (w.Pesel == null && dc.Pieczec))
            {
                var tekst = wc.TekstAtrybutow;
                var utf = wc.TekstToBajty ? Encoding.UTF8.GetString(Encoding.GetEncoding(28591).GetBytes(tekst)) : tekst;
                var pesel = Pesel.ZEtykiety(tekst) ?? Pesel.ZEtykiety(slownikPdf);
                if (pesel == null && w.Rodzaj == Rodzaj.Zaufany)
                {
                    var kandydaci = Pesel.Samodzielne(tekst);
                    if (kandydaci.Count == 1) pesel = kandydaci[0];
                }
                if (pesel != null) w.Pesel = pesel;
                Match mi = WzImie.Match(utf), mn = WzNazwisko.Match(utf);
                if (!mi.Success) { mi = WzImie.Match(tekst); mn = WzNazwisko.Match(tekst); }
                if (mi.Success && mn.Success) w.Podpisujacy = mi.Groups[1].Value + " " + mn.Groups[1].Value;
                else if (w.Rodzaj == Rodzaj.Zaufany)
                {
                    var mName = Regex.Match(slownikPdf, @"/Name\s*\(([^)]{1,200})\)");
                    w.Podpisujacy = mName.Success ? mName.Groups[1].Value : "(podpis zaufany)";
                }
            }
            if (w.Pesel == null)
                w.Uwagi.Add(w.Rodzaj == Rodzaj.Zaufany
                    ? "Nie udało się odczytać numeru PESEL z danych podpisu zaufanego."
                    : "Certyfikat podpisującego nie zawiera numeru PESEL" + (dc.NumerSeryjnyPodmiotu != "" ? " (identyfikator w certyfikacie: " + dc.NumerSeryjnyPodmiotu + ")." : "."));

            w.Status = status;
            return w;
        }
    }
}
