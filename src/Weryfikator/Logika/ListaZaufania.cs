using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Threading.Tasks;
using System.Xml;

namespace Weryfikator.Logika
{
    /// <summary>Usługa zaufania (certyfikat urzędu albo znacznika czasu) z krajowej listy zaufania.</summary>
    internal sealed class WpisListy
    {
        public string Kraj = "";
        public string Dostawca = "";
        /// <summary>„CA/QC” (certyfikaty kwalifikowane) albo „TSA” (znaczniki czasu).</summary>
        public string Typ = "";
        public X509Certificate2 Cert = null!;
        /// <summary>Statusy usługi z datami początku (bieżący i historyczne).</summary>
        public List<KeyValuePair<DateTime, string>> Historia = new List<KeyValuePair<DateTime, string>>();

        private static readonly string[] Dobre = { "granted", "undersupervision", "accredited", "supervisionincessation", "recognisedatnationallevel" };

        public string StatusW(DateTime czasUtc)
        {
            string? s = null; DateTime naj = DateTime.MinValue;
            foreach (var h in Historia)
                if (h.Key <= czasUtc && h.Key >= naj) { naj = h.Key; s = h.Value; }
            return s ?? "";
        }

        public bool AktywnaW(DateTime czasUtc) => Array.IndexOf(Dobre, StatusW(czasUtc)) >= 0;
    }

    /// <summary>Lista zaufania UE: lista list (LOTL) z serwera Komisji Europejskiej i wskazane w niej listy krajowe.</summary>
    internal sealed class ListaZaufania
    {
        public const string AdresLotl = "https://ec.europa.eu/tools/lotl/eu-lotl.xml";

        public DateTime Pobrano;
        public List<WpisListy> Wpisy = new List<WpisListy>();
        public List<string> Ostrzezenia = new List<string>();
        public List<string> Kraje = new List<string>();

        private Dictionary<string, List<WpisListy>>? _poKluczu;
        private Dictionary<string, List<WpisListy>>? _poPodmiocie;

        public static string Klucz(X509Certificate2 c) => c.GetKeyAlgorithm() + ":" + Hex.Z(c.GetPublicKey());

        private void Indeksuj()
        {
            if (_poKluczu != null) return;
            var k = new Dictionary<string, List<WpisListy>>();
            var p = new Dictionary<string, List<WpisListy>>();
            foreach (var w in Wpisy)
            {
                Dodaj(k, Klucz(w.Cert), w);
                Dodaj(p, Hex.Z(w.Cert.SubjectName.RawData), w);
            }
            _poPodmiocie = p; _poKluczu = k;
        }

        private static void Dodaj(Dictionary<string, List<WpisListy>> d, string k, WpisListy w)
        {
            if (!d.TryGetValue(k, out var l)) d[k] = l = new List<WpisListy>();
            l.Add(w);
        }

        /// <summary>Usługi z listy, których certyfikat ma ten sam klucz publiczny i tę samą nazwę podmiotu.</summary>
        public List<WpisListy> Znajdz(X509Certificate2 c)
        {
            Indeksuj();
            var wynik = new List<WpisListy>();
            if (_poKluczu!.TryGetValue(Klucz(c), out var l))
                foreach (var w in l)
                    if (w.Cert.SubjectName.RawData.SequenceEqual(c.SubjectName.RawData)) wynik.Add(w);
            return wynik;
        }

        public IEnumerable<X509Certificate2> PoPodmiocie(X500DistinguishedName nazwa)
        {
            Indeksuj();
            return _poPodmiocie!.TryGetValue(Hex.Z(nazwa.RawData), out var l) ? l.Select(w => w.Cert) : Enumerable.Empty<X509Certificate2>();
        }

        // ---------- pobieranie ----------

        public static async Task<ListaZaufania> PobierzAsync(HttpClient http, ListaZaufania? poprzednia, Action<string>? postep)
        {
            var lista = new ListaZaufania { Pobrano = DateTime.UtcNow };
            postep?.Invoke("Pobieranie listy zaufania UE (LOTL)…");
            var lotlBajty = await http.GetByteArrayAsync(AdresLotl).ConfigureAwait(false);
            var lotl = PodpisXml.Wczytaj(new MemoryStream(lotlBajty));

            var wskazniki = Wskazniki(lotl);
            // LOTL jest pobierana po HTTPS z serwera Komisji; jej podpis musi pochodzić od jednego z certyfikatów,
            // które sama wskazuje jako uprawnione do jej podpisywania.
            var certyLotl = wskazniki.Where(w => w.Mime.IndexOf("xml", StringComparison.OrdinalIgnoreCase) >= 0 && w.Kraj == "EU").SelectMany(w => w.Certy).ToList();
            var bladLotl = SprawdzPodpisListy(lotl, certyLotl);
            if (bladLotl != null) throw new InvalidDataException("Lista zaufania UE (LOTL) ma nieprawidłowy podpis: " + bladLotl);

            var krajowe = wskazniki.Where(w => w.Kraj != "EU" && w.Mime.IndexOf("xml", StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            int gotowe = 0;
            var zadania = krajowe.Select(async w =>
            {
                List<WpisListy>? wpisy = null; string? blad = null;
                try
                {
                    byte[] bajty;
                    try { bajty = await http.GetByteArrayAsync(w.Adres).ConfigureAwait(false); }
                    catch (Exception) { await Task.Delay(1500).ConfigureAwait(false); bajty = await http.GetByteArrayAsync(w.Adres).ConfigureAwait(false); }
                    var doc = PodpisXml.Wczytaj(new MemoryStream(bajty));
                    blad = SprawdzPodpisListy(doc, w.Certy);
                    if (blad == null) wpisy = Uslugi(doc, w.Kraj);
                }
                catch (Exception e) { blad = e.InnerException?.Message ?? e.Message; }
                lock (lista) { gotowe++; postep?.Invoke($"Pobieranie krajowych list zaufania… {gotowe}/{krajowe.Count}"); }
                return new { w.Kraj, wpisy, blad };
            }).ToList();

            foreach (var z in await Task.WhenAll(zadania).ConfigureAwait(false))
            {
                if (z.wpisy != null) { lista.Wpisy.AddRange(z.wpisy); lista.Kraje.Add(z.Kraj); continue; }
                var stare = poprzednia?.Wpisy.Where(x => x.Kraj == z.Kraj).ToList();
                if (stare != null && stare.Count > 0)
                {
                    lista.Wpisy.AddRange(stare); lista.Kraje.Add(z.Kraj);
                    lista.Ostrzezenia.Add($"{z.Kraj}: nie udało się pobrać aktualnej listy ({z.blad}) — użyto poprzednio pobranej.");
                }
                else lista.Ostrzezenia.Add($"{z.Kraj}: lista krajowa pominięta ({z.blad}).");
            }
            if (lista.Wpisy.Count == 0) throw new InvalidDataException("Nie udało się pobrać żadnej krajowej listy zaufania.");
            lista.Kraje = lista.Kraje.Distinct().OrderBy(x => x).ToList();
            return lista;
        }

        internal sealed class Wskaznik
        {
            public string Adres = "", Kraj = "", Mime = "";
            public List<X509Certificate2> Certy = new List<X509Certificate2>();
        }

        internal static List<Wskaznik> Wskazniki(XmlDocument lotl)
        {
            var l = new List<Wskaznik>();
            foreach (XmlElement p in lotl.GetElementsByTagName("OtherTSLPointer", "*"))
            {
                var w = new Wskaznik();
                foreach (XmlElement e in p.GetElementsByTagName("*"))
                {
                    switch (e.LocalName)
                    {
                        case "TSLLocation": w.Adres = e.InnerText.Trim(); break;
                        case "SchemeTerritory": w.Kraj = e.InnerText.Trim().ToUpperInvariant(); break;
                        case "MimeType": w.Mime = e.InnerText.Trim(); break;
                        case "X509Certificate":
                            try { w.Certy.Add(new X509Certificate2(Convert.FromBase64String(e.InnerText))); } catch (Exception) { }
                            break;
                    }
                }
                if (w.Adres.StartsWith("http", StringComparison.OrdinalIgnoreCase)) l.Add(w);
            }
            return l;
        }

        /// <summary>Podpis listy musi obejmować cały dokument i pochodzić od jednego z dozwolonych certyfikatów. Zwraca opis błędu albo null.</summary>
        internal static string? SprawdzPodpisListy(XmlDocument doc, List<X509Certificate2> dozwolone)
        {
            if (dozwolone.Count == 0) return "brak certyfikatów uprawnionych do podpisania listy";
            XmlElement? podpis = null;
            foreach (XmlNode n in doc.DocumentElement!.ChildNodes)
                if (n is XmlElement e && e.LocalName == "Signature" && e.NamespaceURI == PodpisXml.NsDs) podpis = e;
            if (podpis == null) return "lista nie jest podpisana";
            var sx = new SignedXml(doc);
            try { sx.LoadXml(podpis); } catch (Exception e) { return e.Message; }
            bool calosc = false;
            foreach (Reference r in sx.SignedInfo.References) if (r.Uri == "") calosc = true;
            if (!calosc) return "podpis nie obejmuje całej listy";
            var odciski = new HashSet<string>(dozwolone.Select(c => c.Thumbprint));
            var kandydaci = PodpisXml.Certyfikaty(podpis).Where(c => odciski.Contains(c.Thumbprint)).ToList();
            if (kandydaci.Count == 0) return "listę podpisano certyfikatem spoza wskazanych w liście nadrzędnej";
            return PodpisXml.Sprawdz(sx, kandydaci, out var blad) != null ? null : blad ?? "nieprawidłowy podpis";
        }

        internal static List<WpisListy> Uslugi(XmlDocument doc, string kraj)
        {
            var wynik = new List<WpisListy>();
            foreach (XmlElement tsp in doc.GetElementsByTagName("TrustServiceProvider", "*"))
            {
                string dostawca = "";
                var nazwy = tsp.GetElementsByTagName("TSPName", "*");
                if (nazwy.Count > 0) dostawca = nazwy[0]!.ChildNodes.OfType<XmlElement>().FirstOrDefault()?.InnerText.Trim() ?? nazwy[0]!.InnerText.Trim();
                foreach (XmlElement usl in tsp.GetElementsByTagName("TSPService", "*"))
                {
                    XmlElement? info = Dziecko(usl, "ServiceInformation");
                    if (info == null) continue;
                    var typUri = Dziecko(info, "ServiceTypeIdentifier")?.InnerText.Trim() ?? "";
                    string typ;
                    if (typUri.EndsWith("/Svctype/CA/QC", StringComparison.Ordinal)) typ = "CA/QC";
                    else if (typUri.IndexOf("/Svctype/TSA", StringComparison.Ordinal) >= 0) typ = "TSA";
                    else continue;

                    var historia = new List<KeyValuePair<DateTime, string>>();
                    DodajStatus(historia, info);
                    var hist = Dziecko(usl, "ServiceHistory");
                    if (hist != null)
                        foreach (XmlNode h in hist.ChildNodes)
                            if (h is XmlElement he && he.LocalName == "ServiceHistoryInstance") DodajStatus(historia, he);

                    var tozsamosc = Dziecko(info, "ServiceDigitalIdentity");
                    if (tozsamosc == null) continue;
                    foreach (XmlElement c in tozsamosc.GetElementsByTagName("X509Certificate", "*"))
                    {
                        try
                        {
                            wynik.Add(new WpisListy
                            {
                                Kraj = kraj, Dostawca = dostawca, Typ = typ, Historia = historia,
                                Cert = new X509Certificate2(Convert.FromBase64String(c.InnerText))
                            });
                        }
                        catch (Exception) { }
                    }
                }
            }
            return wynik;
        }

        private static void DodajStatus(List<KeyValuePair<DateTime, string>> historia, XmlElement e)
        {
            var status = Dziecko(e, "ServiceStatus")?.InnerText.Trim() ?? "";
            var od = Dziecko(e, "StatusStartingTime")?.InnerText.Trim() ?? "";
            int i = status.LastIndexOf('/');
            if (i >= 0) status = status.Substring(i + 1);
            if (!DateTime.TryParse(od, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var d)) d = DateTime.MinValue;
            historia.Add(new KeyValuePair<DateTime, string>(d, status.ToLowerInvariant()));
        }

        private static XmlElement? Dziecko(XmlElement e, string nazwa)
        {
            foreach (XmlNode n in e.ChildNodes)
                if (n is XmlElement x && x.LocalName == nazwa) return x;
            return null;
        }

        // ---------- pamięć podręczna ----------

        public void Zapisz(string sciezka)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(sciezka)!);
            var sb = new StringBuilder();
            sb.Append("WERYFIKATOR-TL 1\t").Append(Pobrano.Ticks).Append('\n');
            foreach (var w in Wpisy)
            {
                sb.Append(w.Kraj).Append('\t').Append(w.Typ).Append('\t').Append(w.Dostawca.Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' ')).Append('\t');
                sb.Append(string.Join(";", w.Historia.Select(h => h.Key.Ticks + "=" + h.Value))).Append('\t');
                sb.Append(Convert.ToBase64String(w.Cert.RawData)).Append('\n');
            }
            var tmp = sciezka + ".tmp";
            File.WriteAllText(tmp, sb.ToString(), new UTF8Encoding(false));
            if (File.Exists(sciezka)) File.Delete(sciezka);
            File.Move(tmp, sciezka);
        }

        public static ListaZaufania? Wczytaj(string sciezka)
        {
            try
            {
                if (!File.Exists(sciezka)) return null;
                var wiersze = File.ReadAllLines(sciezka, Encoding.UTF8);
                if (wiersze.Length < 2 || !wiersze[0].StartsWith("WERYFIKATOR-TL 1\t")) return null;
                var l = new ListaZaufania { Pobrano = new DateTime(long.Parse(wiersze[0].Split('\t')[1]), DateTimeKind.Utc) };
                for (int i = 1; i < wiersze.Length; i++)
                {
                    var p = wiersze[i].Split('\t');
                    if (p.Length != 5) continue;
                    var w = new WpisListy { Kraj = p[0], Typ = p[1], Dostawca = p[2], Cert = new X509Certificate2(Convert.FromBase64String(p[4])) };
                    foreach (var h in p[3].Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        int r = h.IndexOf('=');
                        w.Historia.Add(new KeyValuePair<DateTime, string>(new DateTime(long.Parse(h.Substring(0, r)), DateTimeKind.Utc), h.Substring(r + 1)));
                    }
                    l.Wpisy.Add(w);
                }
                l.Kraje = l.Wpisy.Select(x => x.Kraj).Distinct().OrderBy(x => x).ToList();
                return l.Wpisy.Count > 0 ? l : null;
            }
            catch (Exception) { return null; }
        }
    }
}
