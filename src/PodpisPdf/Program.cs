using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace PodpisPdf;

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

    /// <summary>Program uruchomiony przez nową wersję po aktualizacji (do komunikatu na pasku stanu).</summary>
    internal static bool PoAktualizacji { get; private set; }

    [STAThread]
    private static int Main(string[] args)
    {
        // Serwer COM dla nowego menu kontekstowego Windows 11 — bez interfejsu i bez blokady jednej kopii.
        if (args.Any(a => string.Equals(a, SerwerMenu.ParametrSerwera, StringComparison.OrdinalIgnoreCase)))
            return SerwerMenu.Uruchom();

        WinRT.ComWrappersSupport.InitializeComWrappers();

        Autostart = args.Any(a => string.Equals(a, Instalacja.ParametrAutostartu, StringComparison.OrdinalIgnoreCase));
        PoAktualizacji = args.Contains(ParametrPoAktualizacji);
        PlikStartowy = args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal));

        // Dwuklik pliku PDF (PodpisPdf jako aplikacja domyślna): podpisać czy przejrzeć?
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
            // Po aktualizacji poprzednia wersja właśnie się zamyka — czekamy, aż zwolni blokadę.
            Instancja = JednaInstancja.Przejmij(PoAktualizacji ? TimeSpan.FromSeconds(30) : default)
                        ?? ObsluzDzialajacaKopie(PlikStartowy, Autostart);
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
    /// przy nowszej wersji można zamknąć działającą i przejąć jej miejsce (aktualizacja kopii w %APPDATA%).
    /// </summary>
    private static JednaInstancja? ObsluzDzialajacaKopie(string? plik, bool autostart)
    {
        var biezaca = Instalacja.BiezacaWersja;
        var dzialajaca = Version.TryParse(JednaInstancja.Wyslij(JednaInstancja.Wersja), out var v) ? v : null;
        var polecenie = plik != null ? JednaInstancja.PrefiksPliku + plik : JednaInstancja.Pokaz;

        if (dzialajaca == biezaca)
        {
            if (!autostart)
                JednaInstancja.Wyslij(polecenie);
            return null;
        }
        if (autostart)
            return null; // przy logowaniu do Windows nie zadajemy pytań

        if (dzialajaca != null && dzialajaca > biezaca)
        {
            Win32.Komunikat(
                $"Uruchomiona jest nowsza wersja PodpisPdf ({dzialajaca}) niż ta, którą otwierasz ({biezaca}).\n\n" +
                "Zostanie użyta uruchomiona, nowsza wersja.", Win32.Ostrzezenie);
            JednaInstancja.Wyslij(polecenie);
            return null;
        }

        var opis = dzialajaca != null ? $"w wersji {dzialajaca}" : "w starszej wersji";
        var odpowiedz = Win32.Komunikat(
            $"PodpisPdf {opis} jest już uruchomiony (np. ikona obok zegara).\n\n" +
            $"Aby uruchomić wersję {biezaca} i zaktualizować kopię programu w %APPDATA%\\PodpisPdf, " +
            "działający program trzeba zamknąć.\n\nZamknąć go teraz?", Win32.Ostrzezenie, Win32.TakNie);
        if (odpowiedz != Win32.Tak)
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
                            "Zamknij go ręcznie (ikona PodpisPdf obok zegara → Zakończ) i uruchom tę wersję ponownie.", Win32.Ostrzezenie);
        }
        return instancja;
    }
}
