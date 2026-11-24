using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Weryfikator.Logika;

namespace Weryfikator
{
    /// <summary>Foldery programu, pamięć podręczna listy zaufania, zaufane certyfikaty użytkownika.</summary>
    internal static class Srodowisko
    {
        public static readonly string FolderDanych =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Weryfikator");
        public static readonly string PlikListy = Path.Combine(FolderDanych, "lista-zaufania.txt");
        public static readonly string FolderCertyfikatow = Path.Combine(FolderDanych, "Zaufane certyfikaty");
        public static readonly string PlikUstawien = Path.Combine(FolderDanych, "ustawienia.txt");
        public static readonly TimeSpan WaznoscListy = TimeSpan.FromHours(12);

        public static HttpClient Http()
        {
            // TLS 1.2 i 1.3 (wartość 12288 — nazwy Tls13 nie ma w starszych zestawach referencyjnych).
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12 | (SecurityProtocolType)12288; }
            catch (NotSupportedException) { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; }
            var h = new HttpClient(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate })
            { Timeout = TimeSpan.FromSeconds(120) };
            h.DefaultRequestHeaders.UserAgent.ParseAdd("Weryfikator/1.0");
            return h;
        }

        public static async Task<ListaZaufania> PobierzListe(ListaZaufania? poprzednia, Action<string>? postep)
        {
            using var http = Http();
            var lista = await ListaZaufania.PobierzAsync(http, poprzednia, postep).ConfigureAwait(false);
            try { lista.Zapisz(PlikListy); } catch (Exception e) { lista.Ostrzezenia.Add("Nie udało się zapisać listy na dysku: " + e.Message); }
            return lista;
        }

        public static void UtworzFolderCertyfikatow()
        {
            Directory.CreateDirectory(FolderCertyfikatow);
            var opis = Path.Combine(FolderCertyfikatow, "CZYTAJ.txt");
            if (!File.Exists(opis))
                File.WriteAllText(opis,
                    "Zaufane certyfikaty główne — Weryfikator\r\n\r\n" +
                    "Podpis osobisty (e-dowód) nie jest podpisem kwalifikowanym, więc jego wystawcy nie ma na liście zaufania UE.\r\n" +
                    "Aby Weryfikator uznawał podpisy osobiste, skopiuj do tego folderu certyfikaty urzędu certyfikacji e-dowodu\r\n" +
                    "(pliki .cer/.crt/.pem — certyfikat główny oraz pośrednie), pobrane ze strony https://www.gov.pl/web/e-dowod\r\n" +
                    "(sekcja o certyfikatach / „Infrastruktura klucza publicznego”).\r\n\r\n" +
                    "Kopiuj tu wyłącznie certyfikaty pobrane z oficjalnego źródła — każdy certyfikat główny z tego folderu\r\n" +
                    "jest traktowany jako zaufany.\r\n");
        }

        /// <summary>Certyfikaty z folderu użytkownika i z folderu „certyfikaty” obok programu.</summary>
        public static List<X509Certificate2> CertyfikatyUzytkownika()
        {
            var l = new List<X509Certificate2>();
            var foldery = new[] { FolderCertyfikatow, Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "certyfikaty") };
            foreach (var f in foldery)
            {
                if (!Directory.Exists(f)) continue;
                foreach (var p in Directory.GetFiles(f))
                {
                    var r = Path.GetExtension(p).ToLowerInvariant();
                    if (r != ".cer" && r != ".crt" && r != ".pem" && r != ".der") continue;
                    try { l.Add(new X509Certificate2(p)); } catch (Exception) { }
                }
            }
            return l;
        }

        /// <summary>Czcionki raportu PDF (zwykła i pogrubiona) z folderu czcionek Windows.</summary>
        public static byte[][] Czcionki()
        {
            var folder = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
            foreach (var para in new[] { new[] { "arial.ttf", "arialbd.ttf" }, new[] { "segoeui.ttf", "segoeuib.ttf" }, new[] { "tahoma.ttf", "tahomabd.ttf" }, new[] { "verdana.ttf", "verdanab.ttf" } })
            {
                string a = Path.Combine(folder, para[0]), b = Path.Combine(folder, para[1]);
                if (File.Exists(a) && File.Exists(b)) return new[] { File.ReadAllBytes(a), File.ReadAllBytes(b) };
            }
            throw new FileNotFoundException("Nie znaleziono czcionki Arial ani Segoe UI w folderze czcionek Windows — zapisz raport jako CSV.");
        }

        /// <summary>Nagłówek raportu: wersja programu, użyta lista PESEL i lista zaufania.</summary>
        public static Raport.Naglowek Naglowek(ListaPesel? pesele, ListaZaufania? lista)
        {
            var n = new Raport.Naglowek { Wersja = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "" };
            if (pesele != null)
                n.ListaPesel = Path.GetFileName(pesele.Plik) + " (" + (pesele.WybranaKolumna >= 0 ? "kolumna " + ListaPesel.NazwaKolumny(pesele.WybranaKolumna) + ", " : "") +
                               "numerów: " + pesele.Numery.Count + ")";
            if (lista != null)
                n.ListaZaufania = (lista.Pobrano.Year > 2000 ? "pobrana " + lista.Pobrano.ToLocalTime().ToString("dd.MM.yyyy HH:mm") + ", " : "") + "krajów: " + lista.Kraje.Count;
            return n;
        }

        public static IEnumerable<string> PlikiZ(string sciezka)
        {
            if (Directory.Exists(sciezka))
            {
                IEnumerable<string> pliki;
                try { pliki = Directory.GetFiles(sciezka, "*", SearchOption.AllDirectories); }
                catch (Exception) { yield break; }
                foreach (var p in pliki)
                    if (Sprawdzanie.Obslugiwany(p)) yield return p;
            }
            else if (File.Exists(sciezka)) yield return sciezka;
        }
    }
}
