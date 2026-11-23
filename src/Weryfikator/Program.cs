using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Weryfikator.Logika;

namespace Weryfikator
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--test-listy") return TestListy(args.Length > 1 ? args[1] : "lista.txt");
            if (args.Length > 0 && args[0] == "--sprawdz") return BezOkna(args);

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new OknoGlowne(args));
            return 0;
        }

        /// <summary>Pobiera listę zaufania UE i zapisuje podsumowanie (test połączenia; używany też w CI).</summary>
        private static int TestListy(string plik)
        {
            var sb = new StringBuilder();
            int kod = 0;
            try
            {
                var lista = Srodowisko.PobierzListe(null, null).GetAwaiter().GetResult();
                sb.AppendLine($"Pobrano {lista.Wpisy.Count} certyfikatów usług z {lista.Kraje.Count} krajów: {string.Join(" ", lista.Kraje)}");
                foreach (var g in lista.Wpisy.GroupBy(w => w.Kraj).OrderBy(g => g.Key))
                    sb.AppendLine($"{g.Key}: urzędy kwalifikowane {g.Count(w => w.Typ == "CA/QC")}, znaczniki czasu {g.Count(w => w.Typ == "TSA")}");
                foreach (var o in lista.Ostrzezenia) sb.AppendLine("OSTRZEŻENIE: " + o);
                if (!lista.Kraje.Contains("PL") || lista.Kraje.Count < 20) kod = 1;
            }
            catch (Exception e) { sb.AppendLine("BŁĄD: " + e); kod = 2; }
            File.WriteAllText(plik, sb.ToString(), Encoding.UTF8);
            return kod;
        }

        /// <summary>
        /// Tryb bez okna: Weryfikator.exe --sprawdz &lt;folder lub pliki…&gt; [--pesel lista.xlsx] [--kolumna B] [--raport raport.csv]
        /// [--lista plik] [--bez-crl] [--zrzut okno.png]. Kod wyjścia: 0 — wszystko w porządku, 1 — są pozycje do sprawdzenia, 2 — błąd.
        /// </summary>
        private static int BezOkna(string[] args)
        {
            string raport = "raport.csv";
            try
            {
                var u = new Ustawienia { CertyfikatyUzytkownika = Srodowisko.CertyfikatyUzytkownika() };
                var pliki = new List<string>();
                string? plikListy = null;
                int kolumna = -1;
                for (int i = 1; i < args.Length; i++)
                {
                    if (args[i] == "--pesel") u.Pesele = ListaPesel.Wczytaj(args[++i]);
                    else if (args[i] == "--kolumna") kolumna = ListaPesel.NumerKolumny(args[++i]);
                    else if (args[i] == "--raport") raport = args[++i];
                    else if (args[i] == "--lista") plikListy = args[++i];
                    else if (args[i] == "--bez-crl") u.Online = false;
                    else if (args[i] == "--zrzut") i++;
                    else pliki.AddRange(Srodowisko.PlikiZ(args[i]));
                }
                if (kolumna >= 0) u.Pesele?.Wybierz(-1, kolumna);
                if (plikListy != null) u.Lista = ListaZaufania.Wczytaj(plikListy) ?? throw new InvalidDataException("Nie można wczytać listy zaufania z pliku " + plikListy);
                else
                {
                    var stara = ListaZaufania.Wczytaj(Srodowisko.PlikListy);
                    if (stara != null && DateTime.UtcNow - stara.Pobrano < Srodowisko.WaznoscListy) u.Lista = stara;
                    else
                        try { u.Lista = Srodowisko.PobierzListe(stara, null).GetAwaiter().GetResult(); }
                        catch (Exception) { u.Lista = stara; }
                }
                var wyniki = pliki.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                    .SelectMany(p => Sprawdzanie.Plik(p, u)).ToList();
                Raport.ZapiszCsv(raport, wyniki);
                int z = Array.IndexOf(args, "--zrzut");
                if (z >= 0)
                {
                    Application.EnableVisualStyles();
                    OknoGlowne.Zrzut(args[z + 1], wyniki, u.Pesele, u.Lista);
                }
                return wyniki.All(w => w.WPorzadku) ? 0 : 1;
            }
            catch (Exception e)
            {
                try { File.WriteAllText(raport + ".blad.txt", e.ToString(), Encoding.UTF8); } catch (Exception) { }
                return 2;
            }
        }
    }
}
