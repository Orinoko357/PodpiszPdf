using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using PodpisPdf.Core;
using PodpisPdf.Fluent;

namespace PodpisPdf;

internal sealed class OknoGlowne : Form
{
    // Karta „Dokument”: stan pusty (miejsce na plik) i stan z wybranym plikiem.
    private readonly FluentKarta _kartaDokumentu;
    private readonly Panel _bezPliku;
    private readonly Panel _zPlikiem;
    private readonly PictureBox _miniatura;
    private readonly FluentEtykieta _nazwaPliku;
    private readonly FluentEtykieta _opisPliku;
    private readonly FluentPrzycisk _przyciskWybierz;
    private readonly FluentPrzycisk _przyciskZmien;
    private readonly FluentPrzycisk _przyciskUsun;

    private readonly ListaCertyfikatow _listaCertyfikatow;
    private readonly FluentEtykieta _przyciskOdswiez;
    private readonly FluentEtykieta _informacja;
    private readonly FluentPrzycisk _przyciskUstawienia;
    private readonly FluentPrzycisk _przyciskPodpisz;
    private readonly FluentEtykieta _status;
    private readonly NotifyIcon _zasobnik;
    private readonly ConcurrentQueue<string> _zlecenia = new();

    private Ustawienia _ustawienia;
    private string? _plik;
    private bool _trwaPodpisywanie;
    private bool _pokazanoDymek;
    private int _numerMiniatury;

    public OknoGlowne(string? plikStartowy, bool autostart = false)
    {
        _ustawienia = Ustawienia.Wczytaj();
        _ustawienia.PrzeniesLogoDoFolderuUstawien();

        SuspendLayout();
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = $"PodpisPdf {Application.ProductVersion.Split('+')[0]}";
        Icon = Ustawienia.Ikona();
        Font = Motyw.Czcionka(10);
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(860, 660);
        MinimumSize = new Size(680, 600);
        AllowDrop = true;

        // ---- stopka: stan i przyciski ----
        var stopka = new FluentKarta { Rodzaj = RodzajKarty.Warstwa, Obramowanie = false, Promien = 0, Dock = DockStyle.Bottom, Height = 68, Padding = new Padding(28, 0, 28, 0) };
        var ukladStopki = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1 };
        ukladStopki.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        ukladStopki.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        ukladStopki.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        ukladStopki.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _status = new FluentEtykieta("", StylEtykiety.Drugi) { Anchor = AnchorStyles.Left, AutoEllipsis = true, AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
        _przyciskUstawienia = new FluentPrzycisk { Text = "Ustawienia", Ikona = "\uE713", AutoSize = true, Anchor = AnchorStyles.None, Margin = new Padding(12, 0, 0, 0) };
        _przyciskUstawienia.Click += (_, _) => OtworzUstawienia();
        _przyciskPodpisz = new FluentPrzycisk { Text = "Podpisz…", Akcent = true, AutoSize = true, MinimumSize = new Size(150, 34), Anchor = AnchorStyles.None, Margin = new Padding(10, 0, 0, 0) };
        _przyciskPodpisz.Click += (_, _) => Podpisz();
        ukladStopki.Controls.Add(_status, 0, 0);
        ukladStopki.Controls.Add(_przyciskUstawienia, 1, 0);
        ukladStopki.Controls.Add(_przyciskPodpisz, 2, 0);
        stopka.Controls.Add(ukladStopki);

        // ---- treść ----
        var uklad = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(28, 14, 28, 16) };
        uklad.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        uklad.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // tytuł
        uklad.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // podtytuł
        uklad.RowStyles.Add(new RowStyle(SizeType.Absolute, 118));  // dokument
        uklad.RowStyles.Add(new RowStyle(SizeType.Percent, 100));   // certyfikaty
        uklad.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));   // informacja

        uklad.Controls.Add(new FluentEtykieta("Podpisz dokument", StylEtykiety.Tytul) { Margin = new Padding(0, 0, 0, 2) }, 0, 0);
        uklad.Controls.Add(new FluentEtykieta("Kwalifikowany podpis elektroniczny PAdES ze znacznikiem czasu", StylEtykiety.Drugi) { Margin = new Padding(0, 0, 0, 16) }, 0, 1);

        // karta: dokument
        _kartaDokumentu = new FluentKarta { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 14), Padding = new Padding(20, 14, 20, 14), AllowDrop = true };
        _bezPliku = new Panel { Dock = DockStyle.Fill, Cursor = Cursors.Hand };
        var ikonaPliku = new FluentEtykieta("\uE8E5", StylEtykiety.IkonaAkcent) { Font = Motyw.Ikony(22), Location = new Point(4, 24) };
        var tekstPusty = new FluentEtykieta("Przeciągnij tutaj plik PDF", StylEtykiety.Pogrubiony) { Location = new Point(56, 18) };
        var tekstPusty2 = new FluentEtykieta("albo wybierz go z dysku. Plik można też otworzyć z menu pod prawym przyciskiem myszy.", StylEtykiety.Drugi) { Location = new Point(56, 44) };
        _przyciskWybierz = new FluentPrzycisk { Text = "Wybierz plik…", AutoSize = true, Anchor = AnchorStyles.Top | AnchorStyles.Right };
        _przyciskWybierz.Click += (_, _) => WybierzPlik();
        _bezPliku.Controls.AddRange(new Control[] { ikonaPliku, tekstPusty, tekstPusty2, _przyciskWybierz });
        _bezPliku.Resize += (_, _) => _przyciskWybierz.Location = new Point(_bezPliku.Width - _przyciskWybierz.Width, (_bezPliku.Height - _przyciskWybierz.Height) / 2);
        foreach (Control k in new Control[] { _bezPliku, ikonaPliku, tekstPusty, tekstPusty2 })
            k.Click += (_, _) => WybierzPlik();

        _zPlikiem = new Panel { Dock = DockStyle.Fill, Visible = false };
        var sekcjaDok = new FluentEtykieta("DOKUMENT", StylEtykiety.Sekcja) { Location = new Point(0, 0) };
        _miniatura = new PictureBox { Location = new Point(0, 22), Size = new Size(48, 64), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
        _nazwaPliku = new FluentEtykieta("", StylEtykiety.Pogrubiony) { Location = new Point(64, 32), AutoEllipsis = true, AutoSize = false, Height = 24 };
        _opisPliku = new FluentEtykieta("", StylEtykiety.Drugi) { Location = new Point(64, 58), AutoEllipsis = true, AutoSize = false, Height = 22 };
        _przyciskZmien = new FluentPrzycisk { Text = "Zmień plik…", AutoSize = true };
        _przyciskZmien.Click += (_, _) => WybierzPlik();
        _przyciskUsun = new FluentPrzycisk { Text = "", Ikona = "\uE711", Size = new Size(40, 34), AccessibleName = "Usuń plik" };
        _przyciskUsun.Click += (_, _) => UstawPlik(null);
        _zPlikiem.Controls.AddRange(new Control[] { sekcjaDok, _miniatura, _nazwaPliku, _opisPliku, _przyciskZmien, _przyciskUsun });
        _zPlikiem.Resize += (_, _) => UlozKarteDokumentu();
        _kartaDokumentu.Controls.Add(_bezPliku);
        _kartaDokumentu.Controls.Add(_zPlikiem);
        uklad.Controls.Add(_kartaDokumentu, 0, 2);

        // karta: certyfikat
        var kartaCert = new FluentKarta { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 14), Padding = new Padding(16, 12, 16, 12) };
        var naglowekCert = new Panel { Dock = DockStyle.Top, Height = 30 };
        naglowekCert.Controls.Add(new FluentEtykieta("CERTYFIKAT", StylEtykiety.Sekcja) { Location = new Point(4, 4) });
        _przyciskOdswiez = new FluentEtykieta("Odśwież", StylEtykiety.Link) { Anchor = AnchorStyles.Top | AnchorStyles.Right, TabStop = true };
        _przyciskOdswiez.Click += async (_, _) => { if (_przyciskOdswiez.Enabled) await WczytajCertyfikaty(); };
        naglowekCert.Controls.Add(_przyciskOdswiez);
        naglowekCert.Resize += (_, _) => _przyciskOdswiez.Location = new Point(naglowekCert.Width - _przyciskOdswiez.Width - 4, 2);
        _listaCertyfikatow = new ListaCertyfikatow { Dock = DockStyle.Fill };
        _listaCertyfikatow.ZmianaWyboru += (_, _) => OdswiezStan();
        _listaCertyfikatow.Zatwierdzono += (_, _) => Podpisz();
        kartaCert.Controls.Add(_listaCertyfikatow);
        kartaCert.Controls.Add(naglowekCert);
        uklad.Controls.Add(kartaCert, 0, 3);

        // pasek informacyjny
        var pasek = new FluentKarta { Rodzaj = RodzajKarty.Informacja, Dock = DockStyle.Fill, Padding = new Padding(14, 0, 14, 0), Margin = new Padding(0) };
        var tarcza = new FluentEtykieta("\uEA18", StylEtykiety.IkonaAkcent) { Dock = DockStyle.Left, AutoSize = false, Width = 30, TextAlign = ContentAlignment.MiddleLeft };
        _informacja = new FluentEtykieta("", StylEtykiety.Zwykly) { Dock = DockStyle.Fill, AutoSize = false, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
        pasek.Controls.Add(_informacja);
        pasek.Controls.Add(tarcza);
        uklad.Controls.Add(pasek, 0, 4);

        Controls.Add(uklad);
        Controls.Add(stopka);
        AcceptButton = _przyciskPodpisz;

        DragEnter += PrzyPrzeciaganiu;
        DragDrop += PrzyUpuszczeniu;
        _kartaDokumentu.DragEnter += PrzyPrzeciaganiu;
        _kartaDokumentu.DragDrop += PrzyUpuszczeniu;

        _zasobnik = UtworzIkoneZasobnika();

        // Uruchomienie przy logowaniu do Windows: program startuje od razu w zasobniku, bez pokazywania okna.
        _startWZasobniku = autostart && string.IsNullOrEmpty(plikStartowy);
        if (_startWZasobniku)
            _zasobnik.Visible = true;
        _plikStartowy = plikStartowy;

        ResumeLayout(false);
        PerformLayout();

        Motyw.Zastosuj(this);
        Motyw.Zmieniono += PrzyZmianieMotywu;
        UstawPlik(null);
        OdswiezInformacje();
    }

    /// <summary>Zrzuty ekranu: przykładowy plik i certyfikaty zamiast prawdziwych.</summary>
    internal void PokazDanePrzykladowe(string? plik, IReadOnlyList<CertyfikatInfo> certyfikaty)
    {
        _listaCertyfikatow.Ustaw(certyfikaty, null, "");
        UstawPlik(plik);
        UstawStatus("Gotowy. Program zapamięta wybrany certyfikat.");
    }

    private void PrzyZmianieMotywu()
    {
        if (IsHandleCreated)
            BeginInvoke(() => Motyw.Zastosuj(this));
    }

    /// <summary>Opis tego, co się stanie przy podpisie: format, serwer znacznika czasu, wizualizacja.</summary>
    private void OdswiezInformacje()
    {
        var u = _ustawienia;
        var serwer = Uri.TryCreate(u.AdresTsa, UriKind.Absolute, out var uri) ? uri.Host : u.AdresTsa;
        var skala = u.SkalaPodpisu;
        var rozmiar = $"{Wizualizacja.SzerokoscMm * skala:0} × {Wizualizacja.WysokoscMm * skala:0} mm";
        _informacja.Text = $"PAdES {(u.PoziomLT ? "B-LT" : "B-T")}  ·  znacznik czasu: {serwer}  ·  wizualizacja {rozmiar}" +
                           (string.IsNullOrEmpty(u.SciezkaLogo) ? "" : " z logo");
    }

    private void UlozKarteDokumentu()
    {
        var w = _zPlikiem.Width;
        _przyciskUsun.Location = new Point(w - _przyciskUsun.Width, 34);
        _przyciskZmien.Location = new Point(_przyciskUsun.Left - _przyciskZmien.Width - 8, 34);
        var szer = Math.Max(50, _przyciskZmien.Left - _nazwaPliku.Left - 16);
        _nazwaPliku.Width = szer;
        _opisPliku.Width = szer;
    }

    private readonly bool _startWZasobniku;
    private readonly string? _plikStartowy;
    private bool _zainicjalizowano;

    protected override void SetVisibleCore(bool value)
    {
        if (_startWZasobniku && !_zainicjalizowano && value)
        {
            // Okno nie jest pokazywane, ale uchwyt musi powstać — przez niego przychodzą polecenia
            // z menu kontekstowego i działa ikona w zasobniku.
            if (!IsHandleCreated)
                CreateHandle();
            value = false;
        }
        base.SetVisibleCore(value);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        BeginInvoke(Inicjalizuj);
    }

    /// <summary>
    /// Start programu. Wolniejsze czynności (certyfikaty, kopia programu, biblioteki PDF) wykonują się w tle,
    /// więc okno jest od razu gotowe.
    /// </summary>
    private async void Inicjalizuj()
    {
        if (_zainicjalizowano)
            return;
        _zainicjalizowano = true;
        if (Zrzuty.Aktywne)
            return; // zrzuty ekranu: bez magazynu certyfikatów, rejestru i kopii programu

        var ustawienia = _ustawienia;
        var instalacja = Task.Run(() =>
        {
            var komunikat = OdswiezInstalacje(ustawienia);
            OdswiezMenuKontekstowe(cicho: true);
            return komunikat;
        });

        await WczytajCertyfikaty();

        if (!string.IsNullOrEmpty(_plikStartowy))
            _zlecenia.Enqueue(_plikStartowy); // uruchomienie z menu kontekstowego — od razu do podpisu
        PrzetworzZlecenia();

        var komunikat = await instalacja;
        if (komunikat != null && !_trwaPodpisywanie)
            UstawStatus(komunikat);

        // Przygotowanie bibliotek PDF w tle — pierwszy podgląd i podpis będą szybsze.
        _ = Task.Run(() =>
        {
            Thread.CurrentThread.Priority = ThreadPriority.BelowNormal;
            Rozgrzewka.Wykonaj();
        });
    }

    /// <summary>
    /// Polecenie od kolejnego uruchomienia programu (np. z menu kontekstowego pliku PDF albo nowszej wersji).
    /// Wywoływane w wątku tła; zwraca odpowiedź dla tamtej kopii.
    /// </summary>
    public string PrzyjmijPolecenie(string polecenie)
    {
        if (polecenie == JednaInstancja.Wersja)
            return Instalacja.BiezacaWersja.ToString();

        if (polecenie == JednaInstancja.Zakoncz)
        {
            // Nie przerywamy podpisywania ani otwartego okna (podgląd, ustawienia).
            if (!IsHandleCreated || (bool)Invoke(() => _trwaPodpisywanie || Application.OpenForms.Count > 1))
                return JednaInstancja.Zajety;
            BeginInvoke(Close);
            return JednaInstancja.Ok;
        }

        _zlecenia.Enqueue(polecenie.StartsWith(JednaInstancja.PrefiksPliku, StringComparison.Ordinal)
            ? polecenie[JednaInstancja.PrefiksPliku.Length..]
            : "");
        if (IsHandleCreated)
            BeginInvoke(PrzetworzZlecenia);
        return JednaInstancja.Ok;
    }

    /// <summary>
    /// Kopia programu w %APPDATA%: aktualizacja, gdy uruchomiono nowszą wersję, oraz autostart.
    /// Wywoływane w tle; zwraca komunikat do paska stanu albo null.
    /// </summary>
    private static string? OdswiezInstalacje(Ustawienia ustawienia)
    {
        try
        {
            var wynik = Instalacja.Aktualizuj(instalujGdyBrak: ustawienia.Autostart);
            if (ustawienia.Autostart)
                Instalacja.UstawAutostart(true);
            return wynik == Instalacja.Wynik.Zaktualizowano
                ? $"Zaktualizowano program w {Ustawienia.Folder} do wersji {Instalacja.BiezacaWersja}."
                : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return "Nie udało się zaktualizować kopii programu w AppData: " + e.Message;
        }
    }

    private void PrzetworzZlecenia()
    {
        if (_trwaPodpisywanie || !_zlecenia.TryDequeue(out var plik))
            return;
        Przywroc();
        if (string.IsNullOrEmpty(plik))
        {
            PrzetworzZlecenia();
            return;
        }
        UstawPlik(plik);
        if (_plik != null)
            Podpisz();
        else
            PrzetworzZlecenia();
    }

    // ---- Zasobnik systemowy ----

    private NotifyIcon UtworzIkoneZasobnika()
    {
        var menu = new ContextMenuStrip();
        var otworz = menu.Items.Add("Otwórz PodpisPdf", null, (_, _) => Przywroc());
        otworz.Font = new Font(otworz.Font, FontStyle.Bold);
        menu.Items.Add("Podpisz plik PDF…", null, (_, _) =>
        {
            Przywroc();
            if (_trwaPodpisywanie)
                return;
            WybierzPlik();
            if (_plik != null)
                Podpisz();
        });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Zakończ", null, (_, _) => Close());

        var ikona = new NotifyIcon
        {
            Icon = Icon,
            Text = "PodpisPdf",
            ContextMenuStrip = menu,
            Visible = _ustawienia.MinimalizujDoZasobnika,
        };
        ikona.DoubleClick += (_, _) => Przywroc();
        return ikona;
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (WindowState == FormWindowState.Minimized && _ustawienia.MinimalizujDoZasobnika)
        {
            Hide();
            if (!_pokazanoDymek)
            {
                _pokazanoDymek = true;
                _zasobnik.ShowBalloonTip(3000, "PodpisPdf działa w tle",
                    "Kliknij dwukrotnie ikonę, aby otworzyć okno. Pliki PDF można podpisywać z menu pod prawym przyciskiem myszy.",
                    ToolTipIcon.Info);
            }
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        Motyw.Zmieniono -= PrzyZmianieMotywu;
        _zasobnik.Visible = false;
        _zasobnik.Dispose();
        base.OnFormClosed(e);
    }

    private void Przywroc()
    {
        if (!Visible)
            Show();
        if (WindowState == FormWindowState.Minimized)
            WindowState = FormWindowState.Normal;
        // Okno uruchomione z innego procesu Windows nie zawsze wysuwa na wierzch — krótkie TopMost pomaga.
        TopMost = true;
        TopMost = false;
        Activate();
    }

    private void OdswiezMenuKontekstowe(bool cicho)
    {
        try
        {
            if (_ustawienia.MenuKontekstowe)
            {
                if (!MenuKontekstowe.JestAktualne())
                    MenuKontekstowe.Zarejestruj();
            }
            else
            {
                MenuKontekstowe.Wyrejestruj();
            }
        }
        catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            if (!cicho)
                Blad("Nie udało się zmienić menu kontekstowego plików PDF.\n\n" + e.Message);
        }
    }

    private CertyfikatInfo? WybranyCertyfikat => _listaCertyfikatow.Wybrany;

    private bool _wczytywanieCertyfikatow;

    /// <summary>Lista certyfikatów — odczyt magazynu (i sterowników kart) w tle, bez blokowania okna.</summary>
    private async Task WczytajCertyfikaty()
    {
        if (_wczytywanieCertyfikatow)
            return;
        _wczytywanieCertyfikatow = true;
        var poprzedni = WybranyCertyfikat?.Odcisk ?? _ustawienia.OstatniCertyfikat;
        IReadOnlyList<CertyfikatInfo> certyfikaty;
        UstawStatus("Wczytywanie certyfikatów…");
        OdswiezStan();
        try
        {
            certyfikaty = await Task.Run(MagazynCertyfikatow.PobierzCertyfikatyDoPodpisu);
        }
        catch (Exception e)
        {
            Blad("Nie udało się odczytać certyfikatów z magazynu Windows.\n\n" + KomunikatyBledow.Opisz(e));
            certyfikaty = Array.Empty<CertyfikatInfo>();
        }
        finally
        {
            _wczytywanieCertyfikatow = false;
        }

        _listaCertyfikatow.Ustaw(certyfikaty, poprzedni,
            "Nie znaleziono certyfikatu do podpisu.\nWłóż kartę do czytnika (lub zaloguj się w SimplySign Desktop) i kliknij „Odśwież”.");
        UstawStatus(certyfikaty.Count == 0 ? "Brak certyfikatu do podpisu." : "Gotowy. Program zapamięta wybrany certyfikat.");
        OdswiezStan();
    }

    private void WybierzPlik()
    {
        if (_trwaPodpisywanie)
            return;
        using var dialog = new OpenFileDialog
        {
            Filter = "Dokumenty PDF (*.pdf)|*.pdf",
            Title = "Wybierz dokument do podpisu",
        };
        if (_plik != null)
            dialog.InitialDirectory = Path.GetDirectoryName(_plik);
        if (dialog.ShowDialog(this) == DialogResult.OK)
            UstawPlik(dialog.FileName);
    }

    private void UstawPlik(string? sciezka)
    {
        if (sciezka != null && !File.Exists(sciezka))
        {
            Blad($"Nie znaleziono pliku:\n{sciezka}");
            sciezka = null;
        }
        _plik = sciezka;
        _bezPliku.Visible = _plik == null;
        _zPlikiem.Visible = _plik != null;
        var stara = _miniatura.Image;
        _miniatura.Image = null;
        stara?.Dispose();
        if (_plik != null)
        {
            _nazwaPliku.Text = Path.GetFileName(_plik);
            var rozmiar = Obrazy.Rozmiar(new FileInfo(_plik).Length);
            var folder = Path.GetDirectoryName(_plik);
            _opisPliku.Text = $"{rozmiar}  ·  {folder}";
            UlozKarteDokumentu();
            _ = WczytajMiniature(_plik, rozmiar, folder);
        }
        OdswiezStan();
    }

    /// <summary>Miniatura pierwszej strony i liczba stron — w tle, żeby okno nie czekało.</summary>
    private async Task WczytajMiniature(string plik, string rozmiar, string? folder)
    {
        var numer = ++_numerMiniatury;
        try
        {
            var wysokosc = _miniatura.Height;
            var (bitmapa, strony) = await Task.Run(() =>
            {
                var s = DokumentPdf.Strony(plik);
                var skala = wysokosc * 2.0 / Math.Max(1, s[0].Wysokosc);
                return (Obrazy.NaBitmape(DokumentPdf.Renderuj(File.ReadAllBytes(plik), 0, skala)), s.Count);
            });
            if (numer != _numerMiniatury || _plik != plik)
            {
                bitmapa.Dispose();
                return;
            }
            _miniatura.Image = bitmapa;
            _opisPliku.Text = $"{Obrazy.LiczbaStron(strony)}  ·  {rozmiar}  ·  {folder}";
        }
        catch (Exception)
        {
            // Miniatura jest tylko pomocą — błąd pliku wyjdzie przy podglądzie.
        }
    }

    private static string? PlikZPrzeciagania(DragEventArgs e)
    {
        if (e.Data?.GetData(DataFormats.FileDrop) is string[] { Length: 1 } pliki &&
            string.Equals(Path.GetExtension(pliki[0]), ".pdf", StringComparison.OrdinalIgnoreCase))
            return pliki[0];
        return null;
    }

    private void PrzyPrzeciaganiu(object? sender, DragEventArgs e)
    {
        e.Effect = !_trwaPodpisywanie && PlikZPrzeciagania(e) != null ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private void PrzyUpuszczeniu(object? sender, DragEventArgs e)
    {
        var plik = PlikZPrzeciagania(e);
        if (plik != null && !_trwaPodpisywanie)
            UstawPlik(plik);
    }

    private void OtworzUstawienia()
    {
        using var okno = new OknoUstawien(_ustawienia.Kopia());
        if (okno.ShowDialog(this) != DialogResult.OK)
            return;
        okno.Ustawienia.OstatniCertyfikat = _ustawienia.OstatniCertyfikat;
        _ustawienia = okno.Ustawienia;
        try
        {
            _ustawienia.Zapisz();
        }
        catch (Exception e)
        {
            Blad("Nie udało się zapisać ustawień.\n\n" + KomunikatyBledow.Opisz(e));
        }
        _zasobnik.Visible = _ustawienia.MinimalizujDoZasobnika;
        try
        {
            Instalacja.UstawAutostart(_ustawienia.Autostart);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            Blad("Nie udało się zmienić autostartu.\n\n" + e.Message);
        }
        OdswiezMenuKontekstowe(cicho: false);
        OdswiezInformacje();
    }

    private async void Podpisz()
    {
        if (_trwaPodpisywanie)
            return;
        if (_plik == null)
        {
            WybierzPlik();
            if (_plik == null)
                return;
        }
        var certyfikat = WybranyCertyfikat;
        if (certyfikat == null)
        {
            Blad("Wybierz certyfikat z listy. Jeśli lista jest pusta, włóż kartę i kliknij „Odśwież listę”.");
            return;
        }

        var wejscie = _plik;
        var wyjscie = PodpisywaczPdf.NazwaPlikuWynikowego(wejscie, _ustawienia.Przyrostek);
        if (File.Exists(wyjscie) &&
            MessageBox.Show(this, $"Plik {Path.GetFileName(wyjscie)} już istnieje. Zastąpić go?", Text,
                MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
        {
            return;
        }

        var ustawienia = _ustawienia;
        var oknoNadrzedne = Handle;
        var opcje = ustawienia.UtworzOpcjePodpisu();
        opcje.PrzygotujKlucz = klucz => UstawOknoNadrzedneDlaPinu(klucz, oknoNadrzedne);

        UstawTrwaPodpisywanie(true);
        try
        {
            // Podgląd wszystkich stron: użytkownik klika, gdzie ma być wizualizacja podpisu.
            UstawStatus("Wskaż na podglądzie miejsce podpisu.");
            var dane = new DaneWizualizacji(certyfikat.ImieNazwisko, ustawienia.Stanowisko, ustawienia.SciezkaLogo, DateTimeOffset.Now);
            opcje.Miejsce = OknoPodgladu.WskazMiejsce(this, wejscie, dane, ustawienia.Polozenie, ustawienia.SkalaPodpisu);
            if (opcje.Miejsce == null)
            {
                UstawStatus("Podpisywanie anulowane.");
                return;
            }
            if (Math.Abs(ustawienia.SkalaPodpisu - opcje.Miejsce.Skala) > 0.0001f)
            {
                // Narysowany rozmiar staje się rozmiarem domyślnym przy następnym podpisie.
                ustawienia.SkalaPodpisu = opcje.Miejsce.Skala;
                try { ustawienia.Zapisz(); } catch (Exception) { /* zapamiętanie rozmiaru nie jest krytyczne */ }
                OdswiezInformacje();
            }

            UstawStatus("Podpisywanie… Sterownik karty może poprosić o PIN.");
            var wynik = await Task.Run(() =>
            {
                var tsa = ustawienia.UtworzKlientaTsa();
                return new PodpisywaczPdf(tsa).Podpisz(wejscie, wyjscie, certyfikat.Certyfikat, opcje);
            });

            _ustawienia.OstatniCertyfikat = certyfikat.Odcisk;
            try { _ustawienia.Zapisz(); } catch (Exception) { /* zapamiętanie certyfikatu nie jest krytyczne */ }

            UstawStatus($"Podpisano: {wynik.SciezkaWyniku}");
            var zrodloCzasu = wynik.CzasZSerwera ? "z serwera znacznika czasu" : "z zegara komputera — serwer nie odpowiedział przed podpisem";
            var odpowiedz = MessageBox.Show(this,
                $"Dokument został podpisany.\n\nZapisano jako:\n{wynik.SciezkaWyniku}\n\n" +
                $"Czas na wizualizacji: {wynik.CzasNaWizualizacji.ToLocalTime():yyyy-MM-dd HH:mm:ss} ({zrodloCzasu}).\n" +
                "Wiążący czas podpisu zawiera znacznik czasu.\n\nOtworzyć folder z podpisanym plikiem?",
                Text, MessageBoxButtons.YesNo, MessageBoxIcon.Information);
            if (odpowiedz == DialogResult.Yes)
                PokazWFolderze(wynik.SciezkaWyniku);
        }
        catch (Exception e)
        {
            UstawStatus("Nie podpisano dokumentu.");
            Blad(KomunikatyBledow.Opisz(e));
        }
        finally
        {
            UstawTrwaPodpisywanie(false);
            BeginInvoke(PrzetworzZlecenia); // kolejne pliki z menu kontekstowego
        }
    }

    /// <summary>
    /// Wskazuje sterownikowi karty okno nadrzędne, żeby okno PIN-u pojawiło się nad aplikacją, a nie pod nią.
    /// </summary>
    private static void UstawOknoNadrzedneDlaPinu(AsymmetricAlgorithm klucz, IntPtr okno)
    {
        CngKey? cng = klucz switch
        {
            RSACng rsa => rsa.Key,
            ECDsaCng ec => ec.Key,
            _ => null,
        };
        if (cng == null)
            return;
        try
        {
            cng.SetProperty(new CngProperty("HWND Handle", BitConverter.GetBytes(okno.ToInt64()), CngPropertyOptions.None));
        }
        catch (CryptographicException)
        {
            // Nie każdy dostawca obsługuje tę właściwość — wtedy okno PIN-u i tak się pojawi.
        }
    }

    private static void PokazWFolderze(string sciezka)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{sciezka}\"") { UseShellExecute = true });
        }
        catch (Exception)
        {
            // Brak eksploratora nie jest błędem podpisu.
        }
    }

    private void UstawTrwaPodpisywanie(bool trwa)
    {
        _trwaPodpisywanie = trwa;
        UseWaitCursor = trwa;
        OdswiezStan();
    }

    private void OdswiezStan()
    {
        _przyciskPodpisz.Enabled = !_trwaPodpisywanie && _plik != null && WybranyCertyfikat != null;
        _przyciskWybierz.Enabled = !_trwaPodpisywanie;
        _przyciskZmien.Enabled = !_trwaPodpisywanie;
        _przyciskUsun.Enabled = !_trwaPodpisywanie;
        _przyciskOdswiez.Enabled = !_trwaPodpisywanie && !_wczytywanieCertyfikatow;
        _przyciskUstawienia.Enabled = !_trwaPodpisywanie;
        _listaCertyfikatow.Enabled = !_trwaPodpisywanie;
    }

    private void UstawStatus(string tekst) => _status.Text = tekst;

    private void Blad(string komunikat) =>
        MessageBox.Show(this, komunikat, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
}
