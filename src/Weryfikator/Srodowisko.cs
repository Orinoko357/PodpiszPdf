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
