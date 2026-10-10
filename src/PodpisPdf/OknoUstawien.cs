using PodpisPdf.Core;

namespace PodpisPdf;

internal sealed class OknoUstawien : Form
{
    private static readonly (WyborStrony Wartosc, string Opis)[] Strony =
    {
        (WyborStrony.Ostatnia, "ostatnia"),
        (WyborStrony.Pierwsza, "pierwsza"),
        (WyborStrony.Numer, "o numerze:"),
    };

    private static readonly (Naroznik Wartosc, string Opis)[] Narozniki =
    {
        (Naroznik.PrawyDolny, "prawy dolny róg"),
        (Naroznik.LewyDolny, "lewy dolny róg"),
        (Naroznik.PrawyGorny, "prawy górny róg"),
        (Naroznik.LewyGorny, "lewy górny róg"),
    };

    private readonly TextBox _stanowisko = new()
    {
        Dock = DockStyle.Fill,
        Multiline = true,
        AcceptsReturn = true,
        WordWrap = false,
        ScrollBars = ScrollBars.Vertical,
        Height = 52,
    };
    private readonly TextBox _logo = new() { Dock = DockStyle.Fill };
    private readonly TextBox _adresTsa = new() { Dock = DockStyle.Fill };
    private readonly TextBox _loginTsa = new() { Dock = DockStyle.Fill };
    private readonly TextBox _hasloTsa = new() { Dock = DockStyle.Fill, UseSystemPasswordChar = true };
    private readonly ComboBox _strona = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
    private readonly NumericUpDown _numerStrony = new() { Minimum = 1, Maximum = 9999, Width = 70 };
    private readonly ComboBox _naroznik = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
    private readonly NumericUpDown _marginesX = Liczba(0, 500);
    private readonly NumericUpDown _marginesY = Liczba(0, 500);
    private readonly TextBox _przyrostek = new() { Width = 160 };
    private readonly CheckBox _zasobnik = new()
    {
        Text = "Minimalizuj do zasobnika systemowego (ikona obok zegara)",
        AutoSize = true,
    };
    private readonly CheckBox _menuKontekstowe = new()
    {
        Text = $"Pokaż „{MenuKontekstowe.Etykieta}” w menu plików PDF (prawy przycisk myszy)",
        AutoSize = true,
    };
    private readonly CheckBox _poziomLT = new()
    {
        Text = "Dołącz dane OCSP/CRL do pliku (PAdES B-LT) — weryfikacja możliwa także po latach",
        AutoSize = true,
    };

    public OknoUstawien(Ustawienia ustawienia)
    {
        Ustawienia = ustawienia;

        Text = "Ustawienia";
        Font = SystemFonts.MessageBoxFont ?? Font;
        AutoScaleMode = AutoScaleMode.Font;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        var uklad = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            Padding = new Padding(12),
            Dock = DockStyle.Fill,
        };
        uklad.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        uklad.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 480));
        Controls.Add(uklad);

        Naglowek(uklad, "Wizualizacja podpisu");
        Wiersz(uklad, "Stanowisko (opcjonalnie):", _stanowisko);
        Wiersz(uklad, "", Etykieta("Enter = nowy wiersz na wizualizacji (bez tego zawija się samo)"));

        var przyciskLogo = new Button { Text = "Wybierz…", AutoSize = true };
        przyciskLogo.Click += (_, _) => WybierzLogo();
        var przyciskBezLogo = new Button { Text = "Usuń", AutoSize = true };
        przyciskBezLogo.Click += (_, _) => _logo.Text = "";
        Wiersz(uklad, "Logo (opcjonalnie):", Poziomo(true, _logo, przyciskLogo, przyciskBezLogo));

        _strona.Items.AddRange(Strony.Select(s => (object)s.Opis).ToArray());
        _strona.SelectedIndexChanged += (_, _) => _numerStrony.Enabled = Strony[_strona.SelectedIndex].Wartosc == WyborStrony.Numer;
        Wiersz(uklad, "", Etykieta("Najlepiej logo wektorowe (SVG lub PDF) — ostre przy każdym powiększeniu. " +
            "PNG/JPG są osadzane w oryginalnej rozdzielczości. Puste marginesy są pomijane, " +
            "a kopia logo jest zapisywana razem z ustawieniami programu."));

        Naglowek(uklad, $"Położenie początkowe na podglądzie (rozmiar stały: {Wizualizacja.SzerokoscMm:0} × {Wizualizacja.WysokoscMm:0} mm)");
        Wiersz(uklad, "Strona:", Poziomo(false, _strona, _numerStrony));

        _naroznik.Items.AddRange(Narozniki.Select(n => (object)n.Opis).ToArray());
        Wiersz(uklad, "Położenie:", Poziomo(false, _naroznik));
        Wiersz(uklad, "Odległość od krawędzi [mm]:",
            Poziomo(false, Etykieta("w poziomie"), _marginesX, Etykieta("w pionie"), _marginesY));

        Naglowek(uklad, "Serwer znacznika czasu (TSA)");
        Wiersz(uklad, "Adres:", _adresTsa);
        Wiersz(uklad, "Login (opcjonalnie):", _loginTsa);
        Wiersz(uklad, "Hasło (opcjonalnie):", _hasloTsa);
        var przyciskTest = new Button { Text = "Sprawdź połączenie", AutoSize = true };
        przyciskTest.Click += async (_, _) => await SprawdzTsa(przyciskTest);
        Wiersz(uklad, "", Poziomo(false, przyciskTest));
        Wiersz(uklad, "", _poziomLT);

        Naglowek(uklad, "Program");
        Wiersz(uklad, "", _zasobnik);
        Wiersz(uklad, "", _menuKontekstowe);

        Naglowek(uklad, "Plik wynikowy");
        Wiersz(uklad, "Przyrostek nazwy:", Poziomo(false, _przyrostek, Etykieta("np. wniosek.pdf → wniosek_podpisany.pdf")));

        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true, MinimumSize = new Size(90, 0) };
        var anuluj = new Button { Text = "Anuluj", DialogResult = DialogResult.Cancel, AutoSize = true, MinimumSize = new Size(90, 0) };
        var przyciski = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 12, 0, 0),
        };
        przyciski.Controls.Add(anuluj);
        przyciski.Controls.Add(ok);
        uklad.Controls.Add(przyciski);
        uklad.SetColumnSpan(przyciski, 2);
        AcceptButton = ok;
        CancelButton = anuluj;

        WczytajDoPol();
        FormClosing += (_, e) =>
        {
            if (DialogResult == DialogResult.OK && !ZapiszZPol())
                e.Cancel = true;
        };
    }

    public Ustawienia Ustawienia { get; }

    private void WczytajDoPol()
    {
        var u = Ustawienia;
        var p = u.Polozenie;
        _stanowisko.Text = (u.Stanowisko ?? "").Replace("\n", Environment.NewLine);
        _logo.Text = u.SciezkaLogo ?? "";
        _adresTsa.Text = u.AdresTsa;
        _loginTsa.Text = u.LoginTsa ?? "";
        _hasloTsa.Text = u.HasloTsa ?? "";
        _strona.SelectedIndex = Math.Max(0, Array.FindIndex(Strony, s => s.Wartosc == p.Strona));
        _numerStrony.Value = Math.Clamp(p.NumerStrony, (int)_numerStrony.Minimum, (int)_numerStrony.Maximum);
        _numerStrony.Enabled = p.Strona == WyborStrony.Numer;
        _naroznik.SelectedIndex = Math.Max(0, Array.FindIndex(Narozniki, n => n.Wartosc == p.Naroznik));
        UstawLiczbe(_marginesX, p.MarginesPoziomyMm);
        UstawLiczbe(_marginesY, p.MarginesPionowyMm);
        _przyrostek.Text = u.Przyrostek;
        _poziomLT.Checked = u.PoziomLT;
        _zasobnik.Checked = u.MinimalizujDoZasobnika;
        _menuKontekstowe.Checked = u.MenuKontekstowe;
    }

    private bool ZapiszZPol()
    {
        var przyrostek = _przyrostek.Text.Trim();
        if (przyrostek.Length == 0 || przyrostek.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            Blad("Przyrostek nazwy pliku nie może być pusty ani zawierać znaków \\ / : * ? \" < > |.");
            _przyrostek.Focus();
            return false;
        }

        var adres = _adresTsa.Text.Trim();
        if (!Uri.TryCreate(adres, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
        {
            Blad("Podaj poprawny adres serwera znacznika czasu, np. http://time.certum.pl");
            _adresTsa.Focus();
            return false;
        }

        var logo = _logo.Text.Trim();
        if (logo.Length > 0 && !File.Exists(logo))
        {
            Blad("Nie znaleziono pliku logo. Wybierz plik ponownie albo usuń logo.");
            _logo.Focus();
            return false;
        }
        if (logo.Length > 0 && !Wizualizacja.RozszerzeniaLogo.Contains(Path.GetExtension(logo).ToLowerInvariant()))
        {
            Blad("Nieobsługiwany format logo. Użyj pliku SVG, PDF, PNG lub JPG.");
            _logo.Focus();
            return false;
        }

        // Sprawdzenie, czy logo da się odczytać — lepiej teraz niż przy podpisie.
        if (logo.Length > 0)
        {
            try
            {
                Wizualizacja.PodgladPdf(new DaneWizualizacji("Test", null, logo, DateTimeOffset.Now));
            }
            catch (Exception e)
            {
                Blad(KomunikatyBledow.Opisz(e));
                _logo.Focus();
                return false;
            }
        }

        string? kopiaLogo;
        try
        {
            kopiaLogo = Ustawienia.ZachowajKopieLogo(logo.Length == 0 ? null : logo);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Blad($"Nie udało się zapisać kopii logo w folderze ustawień ({Ustawienia.Folder}).\n\n{e.Message}");
            return false;
        }

        var u = Ustawienia;
        var wiersze = _stanowisko.Text.Replace("\r\n", "\n").Split('\n').Select(w => w.Trim()).Where(w => w.Length > 0);
        var stanowisko = string.Join("\n", wiersze);
        u.Stanowisko = stanowisko.Length == 0 ? null : stanowisko;
        u.SciezkaLogo = kopiaLogo;
        u.AdresTsa = adres;
        u.LoginTsa = string.IsNullOrWhiteSpace(_loginTsa.Text) ? null : _loginTsa.Text.Trim();
        u.HasloTsa = _hasloTsa.Text;
        u.Polozenie = new PolozenieWizualizacji
        {
            Strona = Strony[_strona.SelectedIndex].Wartosc,
            NumerStrony = (int)_numerStrony.Value,
            Naroznik = Narozniki[_naroznik.SelectedIndex].Wartosc,
            MarginesPoziomyMm = (float)_marginesX.Value,
            MarginesPionowyMm = (float)_marginesY.Value,
        };
        u.Przyrostek = przyrostek;
        u.PoziomLT = _poziomLT.Checked;
        u.MinimalizujDoZasobnika = _zasobnik.Checked;
        u.MenuKontekstowe = _menuKontekstowe.Checked;
        return true;
    }

    private void WybierzLogo()
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "Logo (*.svg;*.pdf;*.png;*.jpg;*.jpeg)|*.svg;*.pdf;*.png;*.jpg;*.jpeg|" +
                     "Wektorowe (*.svg;*.pdf)|*.svg;*.pdf|Obrazy (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg",
            Title = "Wybierz logo",
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
            _logo.Text = dialog.FileName;
    }

    private async Task SprawdzTsa(Button przycisk)
    {
        var adres = _adresTsa.Text.Trim();
        var login = string.IsNullOrWhiteSpace(_loginTsa.Text) ? null : _loginTsa.Text.Trim();
        var haslo = _hasloTsa.Text;
        przycisk.Enabled = false;
        UseWaitCursor = true;
        try
        {
            var klient = KlientZnacznikaCzasu.DlaAdresu(adres, login, haslo);
            // Pobranie pełnego znacznika (a nie tylko czasu), żeby zobaczyć ewentualny błąd serwera.
            var czas = await Task.Run(() =>
            {
                var skrot = klient.GetMessageDigest().Digest(Guid.NewGuid().ToByteArray());
                klient.GetTimeStampToken(skrot);
                return klient.PobierzCzas();
            });
            MessageBox.Show(this,
                "Połączenie z serwerem znacznika czasu działa." +
                (czas != null ? $"\nCzas serwera: {czas.Value.ToLocalTime():yyyy-MM-dd HH:mm:ss}" : ""),
                Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception e)
        {
            Blad(KomunikatyBledow.Opisz(e));
        }
        finally
        {
            UseWaitCursor = false;
            przycisk.Enabled = true;
        }
    }

    private static NumericUpDown Liczba(int min, int max) =>
        new() { Minimum = min, Maximum = max, DecimalPlaces = 1, Increment = 1, Width = 70 };

    private static void UstawLiczbe(NumericUpDown pole, float wartosc) =>
        pole.Value = Math.Clamp((decimal)wartosc, pole.Minimum, pole.Maximum);

    private static Label Etykieta(string tekst) =>
        new() { Text = tekst, AutoSize = true, MaximumSize = new Size(480, 0), Anchor = AnchorStyles.Left, Margin = new Padding(3, 6, 3, 0) };

    private static Control Poziomo(bool rozciagnijPierwszy, params Control[] kontrolki)
    {
        if (rozciagnijPierwszy)
        {
            var t = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = kontrolki.Length, Margin = new Padding(0) };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (var i = 1; i < kontrolki.Length; i++)
                t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            for (var i = 0; i < kontrolki.Length; i++)
                t.Controls.Add(kontrolki[i], i, 0);
            return t;
        }

        var f = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0), Dock = DockStyle.Fill };
        f.Controls.AddRange(kontrolki);
        return f;
    }

    private static void Naglowek(TableLayoutPanel uklad, string tekst)
    {
        var l = new Label
        {
            Text = tekst,
            AutoSize = true,
            Font = new Font(uklad.Font, FontStyle.Bold),
            Margin = new Padding(0, uklad.Controls.Count == 0 ? 0 : 14, 0, 4),
        };
        uklad.Controls.Add(l);
        uklad.SetColumnSpan(l, 2);
    }

    private static void Wiersz(TableLayoutPanel uklad, string etykieta, Control kontrolka)
    {
        uklad.Controls.Add(new Label { Text = etykieta, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 8, 0) });
        uklad.Controls.Add(kontrolka);
    }

    private void Blad(string komunikat) =>
        MessageBox.Show(this, komunikat, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
}
