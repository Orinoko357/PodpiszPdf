using System.Diagnostics;
using PodpisPdf.Core;
using PodpisPdf.Fluent;

namespace PodpisPdf;

/// <summary>Ustawienia w układzie Ustawień Windows 11: nawigacja po lewej, karty ustawień po prawej.</summary>
internal sealed class OknoUstawien : Form
{
    private static readonly (WyborStrony Wartosc, string Opis)[] Strony =
    {
        (WyborStrony.Ostatnia, "Ostatnia strona"),
        (WyborStrony.Pierwsza, "Pierwsza strona"),
        (WyborStrony.Numer, "Strona o numerze"),
    };

    private static readonly (Naroznik Wartosc, string Opis)[] Narozniki =
    {
        (Naroznik.PrawyDolny, "Prawy dolny róg"),
        (Naroznik.LewyDolny, "Lewy dolny róg"),
        (Naroznik.PrawyGorny, "Prawy górny róg"),
        (Naroznik.LewyGorny, "Lewy górny róg"),
    };

    private readonly FluentPole _stanowisko = new(wieleWierszy: true) { Width = 320, Height = 62 };
    private readonly PictureBox _podgladWizualizacji = new() { SizeMode = PictureBoxSizeMode.Zoom, Size = new Size(320, 94) };
    private readonly FluentEtykieta _opisLogo = new("", StylEtykiety.Drugi) { AutoEllipsis = true, AutoSize = false, Size = new Size(190, 22), TextAlign = ContentAlignment.MiddleLeft };
    private readonly FluentPole _adresTsa = new() { Width = 320 };
    private readonly FluentPole _loginTsa = new() { Width = 320 };
    private readonly FluentPole _hasloTsa = new() { Width = 320 };
    private readonly FluentLista _strona = new() { Width = 170 };
    private readonly NumericUpDown _numerStrony = new() { Minimum = 1, Maximum = 9999, Width = 64, BorderStyle = BorderStyle.FixedSingle };
    private readonly FluentLista _naroznik = new() { Width = 160 };
    private readonly NumericUpDown _marginesX = Liczba(0, 500);
    private readonly NumericUpDown _marginesY = Liczba(0, 500);
    private readonly FluentPole _przyrostek = new() { Width = 200 };
    private readonly FluentPrzelacznik _zasobnik = new();
    private readonly FluentPrzelacznik _autostart = new();
    private readonly FluentPrzelacznik _menuKontekstowe = new();
    private readonly FluentPrzelacznik _poziomLT = new();

    private readonly Panel _tresc;
    private readonly FluentEtykieta _tytul;
    private readonly List<(ElementNawigacji Element, Control Strona)> _strony = new();
    private string? _sciezkaLogo;
    private int _numerPodgladu;

    public OknoUstawien(Ustawienia ustawienia)
    {
        Ustawienia = ustawienia;

        SuspendLayout();
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = "Ustawienia";
        Icon = PodpisPdf.Ustawienia.Ikona();
        Font = Motyw.Czcionka(10);
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(960, 680);
        MinimumSize = new Size(820, 560);

        // ---- nawigacja ----
        var nawigacja = new Panel { Dock = DockStyle.Left, Width = 240, Padding = new Padding(12, 16, 12, 12) };

        // ---- prawa strona: tytuł + treść ----
        var prawa = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16, 12, 28, 0) };
        _tytul = new FluentEtykieta("", StylEtykiety.Tytul) { Dock = DockStyle.Top, AutoSize = false, Height = 52 };
        _tresc = new Panel { Dock = DockStyle.Fill };
        prawa.Controls.Add(_tresc);
        prawa.Controls.Add(_tytul);

        // ---- stopka ----
        var stopka = new FluentKarta { Rodzaj = RodzajKarty.Warstwa, Obramowanie = false, Promien = 0, Dock = DockStyle.Bottom, Height = 64, Padding = new Padding(28, 0, 28, 0) };
        var przyciski = new FlowLayoutPanel { Dock = DockStyle.Right, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, WrapContents = false, Padding = new Padding(0, 15, 0, 0) };
        var anuluj = new FluentPrzycisk { Text = "Anuluj", DialogResult = DialogResult.Cancel, AutoSize = true, MinimumSize = new Size(110, 34) };
        var zapisz = new FluentPrzycisk { Text = "Zapisz", Akcent = true, DialogResult = DialogResult.OK, AutoSize = true, MinimumSize = new Size(110, 34), Margin = new Padding(0, 0, 10, 0) };
        przyciski.Controls.Add(anuluj);
        przyciski.Controls.Add(zapisz);
        stopka.Controls.Add(przyciski);

        Controls.Add(prawa);
        Controls.Add(nawigacja);
        Controls.Add(stopka);
        AcceptButton = zapisz;
        CancelButton = anuluj;

        DodajStrone(nawigacja, "Wizualizacja", "", StronaWizualizacji());
        DodajStrone(nawigacja, "Znacznik czasu", "", StronaZnacznikaCzasu());
        DodajStrone(nawigacja, "Program", "", StronaProgramu());
        DodajStrone(nawigacja, "Informacje", "", StronaInformacji());

        ResumeLayout(false);
        PerformLayout();

        WczytajDoPol();
        PokazStrone(0);
        ActiveControl = _strony[0].Element; // bez zaznaczania tekstu w pierwszym polu
        Motyw.Zastosuj(this);
        Motyw.Zmieniono += PrzyZmianieMotywu;

        _stanowisko.Pole.TextChanged += (_, _) => OdswiezPodglad();
        FormClosing += (_, e) =>
        {
            if (DialogResult == DialogResult.OK && !ZapiszZPol())
                e.Cancel = true;
        };
    }

    public Ustawienia Ustawienia { get; }

    /// <summary>Do zrzutów ekranu: pokazuje stronę ustawień o podanym numerze.</summary>
    internal void PokazStrone(int indeks)
    {
        for (var i = 0; i < _strony.Count; i++)
        {
            _strony[i].Element.Wybrany = i == indeks;
            _strony[i].Strona.Visible = i == indeks;
        }
        _tytul.Text = _strony[indeks].Element.Text;
    }

    private void PrzyZmianieMotywu()
    {
        if (IsHandleCreated)
            BeginInvoke(() => { Motyw.Zastosuj(this); OdswiezPodglad(); });
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        Motyw.Zmieniono -= PrzyZmianieMotywu;
        base.OnFormClosed(e);
    }

    private void DodajStrone(Panel nawigacja, string nazwa, string ikona, Control strona)
    {
        var indeks = _strony.Count;
        var element = new ElementNawigacji(nazwa, ikona) { Dock = DockStyle.Top };
        element.Click += (_, _) => PokazStrone(indeks);
        // Dock.Top układa od góry w odwrotnej kolejności dodawania — wstawiamy na koniec listy kontrolek.
        nawigacja.Controls.Add(element);
        nawigacja.Controls.SetChildIndex(element, 0);
        strona.Dock = DockStyle.Fill;
        strona.Visible = false;
        _tresc.Controls.Add(strona);
        _strony.Add((element, strona));
    }

    // ---- Strony ----

    private Control StronaWizualizacji()
    {
        var s = NowaStrona();
        Dodaj(s, Karta("Podgląd wizualizacji", "Tak będzie wyglądał podpis (z Twoim imieniem i nazwiskiem z certyfikatu)", _podgladWizualizacji, wysokosc: 140));
        Dodaj(s, Karta("Stanowisko", "Enter dzieli stanowisko na wiersze; bez tego zawija się samo", _stanowisko, wysokosc: 94));

        var zmien = new FluentPrzycisk { Text = "Zmień…", AutoSize = true };
        zmien.Click += (_, _) => WybierzLogo();
        var usun = new FluentPrzycisk { Text = "Usuń", AutoSize = true, Margin = new Padding(8, 0, 0, 0) };
        usun.Click += (_, _) => { _sciezkaLogo = null; OdswiezLogo(); };
        Dodaj(s, Karta("Logo", "SVG lub PDF (wektorowe — najlepsza jakość), PNG albo JPG. Kopia jest zapisywana razem z ustawieniami.",
            Poziomo(_opisLogo, zmien, usun), wysokosc: 92));

        _strona.Items.AddRange(Strony.Select(x => (object)x.Opis).ToArray());
        _strona.SelectedIndexChanged += (_, _) => _numerStrony.Enabled = Strony[_strona.SelectedIndex].Wartosc == WyborStrony.Numer;
        _naroznik.Items.AddRange(Narozniki.Select(x => (object)x.Opis).ToArray());
        Dodaj(s, Karta("Położenie początkowe", "Gdzie podpis pojawi się na podglądzie — można go tam przesunąć lub narysować",
            Poziomo(_strona, _numerStrony, _naroznik), wysokosc: 84));
        Dodaj(s, Karta("Odległość od krawędzi strony", "W milimetrach, liczona od wybranego narożnika",
            Poziomo(Opis("poziomo"), _marginesX, Opis("pionowo"), _marginesY), wysokosc: 84));
        return s;
    }

    private Control StronaZnacznikaCzasu()
    {
        var s = NowaStrona();
        Dodaj(s, Karta("Adres serwera (TSA)", "Np. http://time.certum.pl — znacznik czasu jest dodawany do każdego podpisu", _adresTsa));
        Dodaj(s, Karta("Login", "Tylko dla płatnych serwerów kwalifikowanych", _loginTsa));
        _hasloTsa.Pole.UseSystemPasswordChar = true;
        Dodaj(s, Karta("Hasło", "Zapisywane w postaci zaszyfrowanej (Windows DPAPI)", _hasloTsa));
        var test = new FluentPrzycisk { Text = "Sprawdź połączenie", AutoSize = true };
        test.Click += async (_, _) => await SprawdzTsa(test);
        Dodaj(s, Karta("Test połączenia", "Pobiera próbny znacznik czasu z serwera", test));
        Dodaj(s, Karta("Dane do weryfikacji długoterminowej (PAdES B-LT)", "Dołącza do pliku dane OCSP/CRL — podpis da się zweryfikować także po latach", _poziomLT));
        return s;
    }

    private Control StronaProgramu()
    {
        var s = NowaStrona();
        Dodaj(s, Karta("Uruchamiaj przy logowaniu do Windows", "Program czeka w zasobniku; kopia programu w %APPDATA%\\PodpisPdf", _autostart));
        Dodaj(s, Karta("Minimalizuj do zasobnika", "Zminimalizowane okno chowa się do ikony obok zegara", _zasobnik));
        Dodaj(s, Karta("Menu kontekstowe plików PDF", $"„{MenuKontekstowe.Etykieta}” pod prawym przyciskiem myszy", _menuKontekstowe));
        Dodaj(s, Karta("Przyrostek nazwy podpisanego pliku", "Np. wniosek.pdf → wniosek_podpisany.pdf; oryginał zostaje bez zmian", _przyrostek));
        return s;
    }

    private Control StronaInformacji()
    {
        var s = NowaStrona();
        Dodaj(s, Karta($"PodpisPdf {Application.ProductVersion.Split('+')[0]}",
            "Kwalifikowany podpis elektroniczny PAdES ze znacznikiem czasu", new Panel { Size = new Size(1, 1) }));
        var folder = new FluentPrzycisk { Text = "Otwórz folder", AutoSize = true };
        folder.Click += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{PodpisPdf.Ustawienia.Folder}\"") { UseShellExecute = true }); }
            catch (Exception) { /* brak eksploratora */ }
        };
        Dodaj(s, Karta("Ustawienia i kopia programu", PodpisPdf.Ustawienia.Folder, folder));
        Dodaj(s, Karta("Licencje", "iText 9 i BouncyCastle (AGPL / MIT), PDFium (BSD), czcionka DejaVu Sans", new Panel { Size = new Size(1, 1) }));
        return s;
    }

    // ---- Budowa kart ----

    private static FlowLayoutPanel NowaStrona()
    {
        var s = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(0, 0, 0, 16) };
        s.Resize += (_, _) =>
        {
            var szer = s.ClientSize.Width - s.Padding.Horizontal - 2;
            s.SuspendLayout();
            foreach (Control k in s.Controls)
                k.Width = szer;
            s.ResumeLayout();
        };
        return s;
    }

    private static void Dodaj(FlowLayoutPanel strona, Control karta) => strona.Controls.Add(karta);

    /// <summary>Karta ustawienia: tytuł i opis po lewej, kontrolka po prawej.</summary>
    private static FluentKarta Karta(string tytul, string opis, Control kontrolka, int wysokosc = 72)
    {
        var karta = new FluentKarta { Height = wysokosc, Margin = new Padding(0, 0, 0, 6), Padding = new Padding(20, 0, 16, 0) };
        var uklad = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        uklad.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        uklad.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        uklad.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var teksty = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Margin = new Padding(0, 0, 16, 0) };
        teksty.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        teksty.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        teksty.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        teksty.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        teksty.Controls.Add(new FluentEtykieta(tytul, StylEtykiety.Zwykly) { Margin = new Padding(0, 0, 0, 2) }, 0, 1);
        // Opis zawija się do szerokości kolumny zamiast być ucinany.
        var etykietaOpisu = new FluentEtykieta(opis, StylEtykiety.Drugi);
        teksty.Controls.Add(etykietaOpisu, 0, 2);
        teksty.Resize += (_, _) => etykietaOpisu.MaximumSize = new Size(Math.Max(50, teksty.Width), 0);
        kontrolka.Anchor = AnchorStyles.Right;
        kontrolka.Margin = new Padding(0);
        uklad.Controls.Add(teksty, 0, 0);
        uklad.Controls.Add(kontrolka, 1, 0);
        karta.Controls.Add(uklad);
        return karta;
    }

    private static Control Poziomo(params Control[] kontrolki)
    {
        var f = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
        foreach (var k in kontrolki)
        {
            k.Anchor = AnchorStyles.Left;
            if (k.Margin == Padding.Empty || k.Margin == new Padding(3))
                k.Margin = new Padding(0, 0, 8, 0);
            f.Controls.Add(k);
        }
        return f;
    }

    private static FluentEtykieta Opis(string tekst) => new(tekst, StylEtykiety.Drugi) { Margin = new Padding(0, 6, 6, 0) };

    // ---- Dane ----

    private void WczytajDoPol()
    {
        var u = Ustawienia;
        var p = u.Polozenie;
        _stanowisko.Text = (u.Stanowisko ?? "").Replace("\n", Environment.NewLine);
        _sciezkaLogo = u.SciezkaLogo;
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
        _autostart.Checked = u.Autostart;
        _menuKontekstowe.Checked = u.MenuKontekstowe;
        OdswiezLogo();
    }

    private void OdswiezLogo()
    {
        if (string.IsNullOrEmpty(_sciezkaLogo))
        {
            _opisLogo.Text = "Brak logo";
        }
        else
        {
            var wektorowe = Path.GetExtension(_sciezkaLogo).ToLowerInvariant() is ".svg" or ".pdf";
            _opisLogo.Text = $"{Path.GetFileName(_sciezkaLogo)}  ·  {(wektorowe ? "wektorowe" : "rastrowe")}";
        }
        OdswiezPodglad();
    }

    /// <summary>Podgląd wizualizacji z bieżącym stanowiskiem i logo (w tle).</summary>
    private async void OdswiezPodglad()
    {
        var numer = ++_numerPodgladu;
        var stanowisko = string.Join("\n", _stanowisko.Text.Replace("\r\n", "\n").Split('\n').Select(w => w.Trim()).Where(w => w.Length > 0));
        var logo = _sciezkaLogo;
        var wysokosc = _podgladWizualizacji.Height;
        try
        {
            await Task.Delay(250); // przy pisaniu odświeżamy dopiero po chwili
            if (numer != _numerPodgladu) return;
            var bitmapa = await Task.Run(() =>
            {
                var dane = new DaneWizualizacji("Imię Nazwisko", stanowisko, logo, DateTimeOffset.Now);
                var skala = wysokosc * 2.0 / Wizualizacja.WysokoscPt;
                return Obrazy.NaBitmape(DokumentPdf.Renderuj(Wizualizacja.PodgladPdf(dane), 0, skala));
            });
            if (numer != _numerPodgladu || IsDisposed)
            {
                bitmapa.Dispose();
                return;
            }
            var stara = _podgladWizualizacji.Image;
            _podgladWizualizacji.Image = bitmapa;
            stara?.Dispose();
        }
        catch (Exception)
        {
            // Podgląd jest tylko pomocą — błąd logo wyjdzie przy zapisie.
        }
    }

    private bool ZapiszZPol()
    {
        var przyrostek = _przyrostek.Text.Trim();
        if (przyrostek.Length == 0 || przyrostek.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            PokazStrone(2);
            Blad("Przyrostek nazwy pliku nie może być pusty ani zawierać znaków \\ / : * ? \" < > |.");
            _przyrostek.Pole.Focus();
            return false;
        }

        var adres = _adresTsa.Text.Trim();
        if (!Uri.TryCreate(adres, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
        {
            PokazStrone(1);
            Blad("Podaj poprawny adres serwera znacznika czasu, np. http://time.certum.pl");
            _adresTsa.Pole.Focus();
            return false;
        }

        var logo = _sciezkaLogo ?? "";
        if (logo.Length > 0 && !File.Exists(logo))
        {
            PokazStrone(0);
            Blad("Nie znaleziono pliku logo. Wybierz plik ponownie albo usuń logo.");
            return false;
        }
        if (logo.Length > 0 && !Wizualizacja.RozszerzeniaLogo.Contains(Path.GetExtension(logo).ToLowerInvariant()))
        {
            PokazStrone(0);
            Blad("Nieobsługiwany format logo. Użyj pliku SVG, PDF, PNG lub JPG.");
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
                PokazStrone(0);
                Blad(KomunikatyBledow.Opisz(e));
                return false;
            }
        }

        string? kopiaLogo;
        try
        {
            kopiaLogo = PodpisPdf.Ustawienia.ZachowajKopieLogo(logo.Length == 0 ? null : logo);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Blad($"Nie udało się zapisać kopii logo w folderze ustawień ({PodpisPdf.Ustawienia.Folder}).\n\n{e.Message}");
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
        u.Autostart = _autostart.Checked;
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
        {
            _sciezkaLogo = dialog.FileName;
            OdswiezLogo();
        }
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
        new() { Minimum = min, Maximum = max, DecimalPlaces = 1, Increment = 1, Width = 70, BorderStyle = BorderStyle.FixedSingle };

    private static void UstawLiczbe(NumericUpDown pole, float wartosc) =>
        pole.Value = Math.Clamp((decimal)wartosc, pole.Minimum, pole.Maximum);

    private void Blad(string komunikat) =>
        MessageBox.Show(this, komunikat, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
}
