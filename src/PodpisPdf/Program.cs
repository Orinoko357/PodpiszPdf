namespace PodpisPdf;

internal static class Program
{
    private const string Tytul = "PodpisPdf";

    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        var autostart = args.Any(a => string.Equals(a, Instalacja.ParametrAutostartu, StringComparison.OrdinalIgnoreCase));
        var plik = args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal));

        var instancja = JednaInstancja.Przejmij() ?? ObsluzDzialajacaKopie(plik, autostart);
        if (instancja == null)
            return;

        using (instancja)
        {
            var okno = new OknoGlowne(plik, autostart);
            instancja.Nasluchuj(okno.PrzyjmijPolecenie);
            Application.Run(okno);
        }
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
            MessageBox.Show(
                $"Uruchomiona jest nowsza wersja PodpisPdf ({dzialajaca}) niż ta, którą otwierasz ({biezaca}).\n\n" +
                "Zostanie użyta uruchomiona, nowsza wersja.",
                Tytul, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            JednaInstancja.Wyslij(polecenie);
            return null;
        }

        var opis = dzialajaca != null ? $"w wersji {dzialajaca}" : "w starszej wersji";
        var odpowiedz = MessageBox.Show(
            $"PodpisPdf {opis} jest już uruchomiony (np. ikona obok zegara).\n\n" +
            $"Aby uruchomić wersję {biezaca} i zaktualizować kopię programu w %APPDATA%\\PodpisPdf, " +
            "działający program trzeba zamknąć.\n\nZamknąć go teraz?",
            Tytul, MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (odpowiedz != DialogResult.Yes)
            return null;

        if (dzialajaca != null && JednaInstancja.Wyslij(JednaInstancja.Zakoncz) == JednaInstancja.Zajety)
        {
            MessageBox.Show(
                "Działający program jest w trakcie pracy (podpisywanie lub otwarte okno).\n" +
                "Dokończ ją i uruchom tę wersję ponownie.",
                Tytul, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return null;
        }

        var instancja = JednaInstancja.Przejmij(TimeSpan.FromSeconds(dzialajaca != null ? 15 : 0));
        if (instancja == null)
        {
            MessageBox.Show(
                "Nie udało się zamknąć działającego programu.\n\n" +
                "Zamknij go ręcznie (ikona PodpisPdf obok zegara → Zakończ) i uruchom tę wersję ponownie.",
                Tytul, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        return instancja;
    }
}
