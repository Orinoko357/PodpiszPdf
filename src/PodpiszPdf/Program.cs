using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace PodpiszPdf;

/// <summary>Tryb pracy programu wynikający z parametrów wywołania.</summary>
internal enum TrybUruchomienia { Normalny, Sprawdzenie, Zrzuty }

public static class Program
{
    internal static TrybUruchomienia Tryb { get; private set; }
    internal static string? PlikStartowy { get; private set; }
    internal static bool Autostart { get; private set; }
    internal static string? FolderZrzutow { get; private set; }
    internal static JednaInstancja? Instancja { get; private set; }

    public const string ParametrPoAktualizacji = "--po-aktualizacji";

    /// <summary>Sprzątanie ustawień użytkownika w Windows — uruchamiane przez instalator MSI przy odinstalowaniu.</summary>
    public const string ParametrOdinstalowania = "--odinstaluj";

    /// <summary>Program uruchomiony ponownie po instalatorze aktualizacji (do komunikatu na pasku stanu).</summary>
    internal static bool PoAktualizacji { get; private set; }

    /// <summary>
    /// Kod zakończenia msiexec przekazany po <see cref="ParametrPoAktualizacji"/> (null — starsze wersje
    /// przekazywały sam parametr, tylko po udanej instalacji).
    /// </summary>
    internal static int? KodAktualizacji { get; private set; }

    [STAThread]
    private static int Main(string[] args)
    {
        // Jeden plik exe: biblioteki i zasoby Windows App SDK są wypakowane do AppContext.BaseDirectory.
        // Bez tej zmiennej WinUI szuka ich obok PodpiszPdf.exe albo bierze Windows App Runtime zainstalowany
        // w systemie (inna wersja) — „XAML parsing failed” przy tworzeniu okna.
        Environment.SetEnvironmentVariable("MICROSOFT_WINDOWSAPPRUNTIME_BASE_DIRECTORY", AppContext.BaseDirectory);

        // Serwer COM dla nowego menu kontekstowego Windows 11 — bez interfejsu i bez blokady jednej kopii.
        if (args.Any(a => string.Equals(a, SerwerMenu.ParametrSerwera, StringComparison.OrdinalIgnoreCase)))
            return SerwerMenu.Uruchom();

        WinRT.ComWrappersSupport.InitializeComWrappers();

        // Odinstalowanie programu (akcja instalatora MSI, jako bieżący użytkownik): bez interfejsu.
        if (args.Any(a => string.Equals(a, ParametrOdinstalowania, StringComparison.OrdinalIgnoreCase)))
            return Instalacja.PosprzatajPoOdinstalowaniu();

        // Program nazywał się PodpisPdf (do 3.1.0.x): ustawienia do nowego folderu, stara integracja usuwana.
        Instalacja.PrzeniesZeStarejNazwy();

        // Diagnostyka (GitHub Actions): język interfejsu jak u użytkownika, bez pakietu językowego Windows.
        if (Environment.GetEnvironmentVariable("PODPISZPDF_JEZYK") is { Length: > 0 } jezyk)
            Microsoft.Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = jezyk;

        Autostart = args.Any(a => string.Equals(a, Instalacja.ParametrAutostartu, StringComparison.OrdinalIgnoreCase));
        var indeksAktualizacji = Array.IndexOf(args, ParametrPoAktualizacji);
        PoAktualizacji = indeksAktualizacji >= 0;
        if (PoAktualizacji && indeksAktualizacji + 1 < args.Length && int.TryParse(args[indeksAktualizacji + 1], out var kod))
            KodAktualizacji = kod;
        // Plik: pierwszy parametr, który nie jest przełącznikiem ani kodem po „--po-aktualizacji”.
        PlikStartowy = args.Where((a, i) => !a.StartsWith("--", StringComparison.Ordinal) &&
                                            !(KodAktualizacji != null && i == indeksAktualizacji + 1)).FirstOrDefault();

        // Dwuklik pliku PDF (PodpiszPdf jako aplikacja domyślna): podpisać czy przejrzeć?
        if (args.Contains(DomyslnaAplikacja.ParametrOtwarcia) && PlikStartowy != null && !CzyPodpisac(PlikStartowy))
            return 0;
        if (args.Contains("--sprawdz"))
        {
            Tryb = TrybUruchomienia.Sprawdzenie;
            PlikStartowy = null;
        }
        else if (args.Length >= 2 && args[0] == "--zrzuty")
        {
            Tryb = TrybUruchomienia.Zrzuty;
            FolderZrzutow = args[1];
            PlikStartowy = null;
        }

        if (Tryb == TrybUruchomienia.Normalny)
        {
            try
            {
                // Po aktualizacji poprzednia wersja właśnie się zamyka — czekamy, aż zwolni blokadę.
                Instancja = JednaInstancja.Przejmij(PoAktualizacji ? TimeSpan.FromSeconds(30) : default)
                            ?? ObsluzDzialajacaKopie(PlikStartowy, Autostart);
            }
            catch (UnauthorizedAccessException)
            {
                // Blokadę utworzyła kopia uruchomiona jako administrator — zwykły proces nie może jej otworzyć.
                Win32.Komunikat("PodpiszPdf działa już jako administrator — zamknij go i uruchom ponownie.", Win32.Ostrzezenie);
                return 0;
            }
            if (Instancja == null)
                return 0;
        }

        try
        {
            Application.Start(parametry =>
            {
                var kontekst = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
                SynchronizationContext.SetSynchronizationContext(kontekst);
                _ = new App();
            });
        }
        finally
        {
            Instancja?.Dispose();
        }
        return Environment.ExitCode;
    }

    /// <summary>
    /// Wybór po otwarciu pliku PDF: zgodnie z ustawieniem albo w oknie „Podpisz / Przeglądaj”.
    /// Zwraca true, gdy plik ma trafić do podpisu.
    /// </summary>
    private static bool CzyPodpisac(string plik)
    {
        var ustawienia = Ustawienia.Wczytaj();
        var wybor = ustawienia.PoOtwarciuPdf switch
        {
            PoOtwarciuPdf.Podpisz => Win32.WyborOtwarcia.Podpisz,
            PoOtwarciuPdf.Przegladaj => Win32.WyborOtwarcia.Przegladaj,
            _ => Win32.WyborOtwarcia.Anuluj,
        };
        if (ustawienia.PoOtwarciuPdf == PoOtwarciuPdf.Pytaj)
        {
            (wybor, var zapamietaj) = Win32.ZapytajOOtwarcie(plik, Przegladarka.Nazwa(ustawienia));
            if (zapamietaj)
            {
                ustawienia.PoOtwarciuPdf = wybor == Win32.WyborOtwarcia.Podpisz ? PoOtwarciuPdf.Podpisz : PoOtwarciuPdf.Przegladaj;
                try { ustawienia.Zapisz(); } catch (Exception) { /* zapamiętanie wyboru nie jest krytyczne */ }
            }
        }
        if (wybor == Win32.WyborOtwarcia.Przegladaj)
            Przegladarka.Otworz(plik, ustawienia);
        return wybor == Win32.WyborOtwarcia.Podpisz;
    }

    /// <summary>
    /// Program już działa. Ta sama wersja — przekazujemy jej plik. Inna wersja — ostrzeżenie;
    /// przy nowszej wersji można zamknąć działającą i przejąć jej miejsce (np. po instalacji nowszej wersji).
    /// </summary>
    private static JednaInstancja? ObsluzDzialajacaKopie(string? plik, bool autostart)
    {
        var biezaca = Instalacja.BiezacaWersja;
        if (autostart)
            return null; // przy logowaniu do Windows nie zadajemy pytań
        var odpowiedz = JednaInstancja.Wyslij(JednaInstancja.Wersja);
        var koniec = Environment.TickCount64 + 20000;
        while (odpowiedz == null && Environment.TickCount64 < koniec)
        {
            // Działająca kopia może dopiero startować (np. kilka plików PDF otwartych naraz) — potok
            // pojawia się po zbudowaniu okna. Gdy w tym czasie się zakończy, przejmujemy jej miejsce.
            if (JednaInstancja.Przejmij() is { } wolna)
                return wolna;
            Thread.Sleep(500);
            odpowiedz = JednaInstancja.Wyslij(JednaInstancja.Wersja, 1000);
        }
        var dzialajaca = Version.TryParse(odpowiedz, out var v) ? v : null;
        var polecenie = plik != null ? JednaInstancja.PrefiksPliku + plik : JednaInstancja.Pokaz;

        if (dzialajaca == biezaca)
        {
            JednaInstancja.WyslijZPonowieniem(polecenie, TimeSpan.FromSeconds(20));
            return null;
        }

        if (dzialajaca != null && dzialajaca > biezaca)
        {
            Win32.Komunikat(
                $"Uruchomiona jest nowsza wersja PodpiszPdf ({dzialajaca}) niż ta, którą otwierasz ({biezaca}).\n\n" +
                "Zostanie użyta uruchomiona, nowsza wersja.", Win32.Ostrzezenie);
            JednaInstancja.Wyslij(polecenie);
            return null;
        }

        var opis = dzialajaca != null ? $"w wersji {dzialajaca}" : "w starszej wersji";
        var pytanie = Win32.Komunikat(
            $"PodpiszPdf {opis} jest już uruchomiony (np. ikona obok zegara).\n\n" +
            $"Aby uruchomić wersję {biezaca}, " +
            "działający program trzeba zamknąć.\n\nZamknąć go teraz?", Win32.Ostrzezenie, Win32.TakNie);
        if (pytanie != Win32.Tak)
            return null;

        if (dzialajaca != null && JednaInstancja.Wyslij(JednaInstancja.Zakoncz) == JednaInstancja.Zajety)
        {
            Win32.Komunikat("Działający program jest w trakcie pracy (podpisywanie lub otwarte okno).\n" +
                            "Dokończ ją i uruchom tę wersję ponownie.", Win32.Ostrzezenie);
            return null;
        }

        var instancja = JednaInstancja.Przejmij(TimeSpan.FromSeconds(dzialajaca != null ? 15 : 0));
        if (instancja == null)
        {
            Win32.Komunikat("Nie udało się zamknąć działającego programu.\n\n" +
                            "Zamknij go ręcznie (ikona PodpiszPdf obok zegara → Zakończ) i uruchom tę wersję ponownie.", Win32.Ostrzezenie);
        }
        return instancja;
    }
}
