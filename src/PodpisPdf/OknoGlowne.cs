using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using PodpisPdf.Core;

namespace PodpisPdf;

internal sealed class OknoGlowne : Form
{
    private readonly Label _strefaPliku;
    private readonly Button _przyciskWybierz;
    private readonly ListView _listaCertyfikatow;
    private readonly Button _przyciskOdswiez;
    private readonly Button _przyciskUstawienia;
    private readonly Button _przyciskPodpisz;
    private readonly Label _status;
    private readonly NotifyIcon _zasobnik;
    private readonly ConcurrentQueue<string> _zlecenia = new();

    private Ustawienia _ustawienia;
    private string? _plik;
    private bool _trwaPodpisywanie;
    private bool _pokazanoDymek;

    public OknoGlowne(string? plikStartowy)
    {
        _ustawienia = Ustawienia.Wczytaj();
        _ustawienia.PrzeniesLogoDoFolderuUstawien();

        Text = $"PodpisPdf {Application.ProductVersion.Split('+')[0]} — podpis kwalifikowany PDF";
        Icon = Ustawienia.Ikona();
        Font = SystemFonts.MessageBoxFont ?? Font;
        AutoScaleMode = AutoScaleMode.Font;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(720, 480);
        MinimumSize = new Size(560, 420);
        AllowDrop = true;

        var uklad = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            ColumnCount = 2,
            RowCount = 6,
        };
        uklad.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        uklad.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        uklad.RowStyles.Add(new RowStyle(SizeType.Absolute, 90));  // strefa pliku
        uklad.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // etykieta certyfikatu
        uklad.RowStyles.Add(new RowStyle(SizeType.Percent, 100));  // lista certyfikatów
        uklad.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // przyciski
        uklad.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // status
        Controls.Add(uklad);

        _strefaPliku = new Label
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.FixedSingle,
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = SystemColors.Window,
            AllowDrop = true,
            Margin = new Padding(0, 0, 8, 8),
            Cursor = Cursors.Hand,
        };
        _strefaPliku.Click += (_, _) => WybierzPlik();
        uklad.Controls.Add(_strefaPliku, 0, 0);

        _przyciskWybierz = new Button { Text = "Wybierz plik…", AutoSize = true, Anchor = AnchorStyles.Top | AnchorStyles.Right, Padding = new Padding(8, 4, 8, 4) };
        _przyciskWybierz.Click += (_, _) => WybierzPlik();
        uklad.Controls.Add(_przyciskWybierz, 1, 0);

        uklad.Controls.Add(new Label
        {
            Text = "Certyfikat do podpisu (★ — certyfikat kwalifikowany):",
            AutoSize = true,
            Margin = new Padding(0, 4, 0, 4),
        }, 0, 1);

        _listaCertyfikatow = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = false,
            HideSelection = false,
            HeaderStyle = ColumnHeaderStyle.Nonclickable,
            Margin = new Padding(0, 0, 8, 8),
        };
        _listaCertyfikatow.Columns.Add("", 28);
        _listaCertyfikatow.Columns.Add("Imię i nazwisko", 220);
        _listaCertyfikatow.Columns.Add("Wystawca", 230);
        _listaCertyfikatow.Columns.Add("Ważny do", 100);
        _listaCertyfikatow.SelectedIndexChanged += (_, _) => OdswiezStan();
        _listaCertyfikatow.DoubleClick += (_, _) => Podpisz();
        uklad.Controls.Add(_listaCertyfikatow, 0, 2);

        _przyciskOdswiez = new Button { Text = "Odśwież listę", AutoSize = true, Anchor = AnchorStyles.Top | AnchorStyles.Right, Padding = new Padding(8, 4, 8, 4) };
        _przyciskOdswiez.Click += (_, _) => WczytajCertyfikaty();
        uklad.Controls.Add(_przyciskOdswiez, 1, 2);

        var przyciski = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            Margin = new Padding(0),
        };
        _przyciskPodpisz = new Button
        {
            Text = "Podpisz",
            AutoSize = true,
            Padding = new Padding(24, 6, 24, 6),
            Font = new Font(Font.FontFamily, Font.Size * 1.15f, FontStyle.Bold),
        };
        _przyciskPodpisz.Click += (_, _) => Podpisz();
        _przyciskUstawienia = new Button { Text = "Ustawienia…", AutoSize = true, Padding = new Padding(8, 6, 8, 6) };
        _przyciskUstawienia.Click += (_, _) => OtworzUstawienia();
        przyciski.Controls.Add(_przyciskPodpisz);
        przyciski.Controls.Add(_przyciskUstawienia);
        uklad.Controls.Add(przyciski, 0, 3);
        uklad.SetColumnSpan(przyciski, 2);

        _status = new Label { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 8, 0, 0), ForeColor = SystemColors.GrayText };
        uklad.Controls.Add(_status, 0, 4);
        uklad.SetColumnSpan(_status, 2);

        AcceptButton = _przyciskPodpisz;

        DragEnter += PrzyPrzeciaganiu;
        DragDrop += PrzyUpuszczeniu;
        _strefaPliku.DragEnter += PrzyPrzeciaganiu;
        _strefaPliku.DragDrop += PrzyUpuszczeniu;

        _zasobnik = UtworzIkoneZasobnika();

        Shown += (_, _) =>
        {
            OdswiezMenuKontekstowe(cicho: true);
            WczytajCertyfikaty();
            if (!string.IsNullOrEmpty(plikStartowy))
                _zlecenia.Enqueue(plikStartowy); // uruchomienie z menu kontekstowego — od razu do podpisu
            PrzetworzZlecenia();
        };

        UstawPlik(null);
    }

    /// <summary>
    /// Wywoływane (w wątku tła), gdy program uruchomiono ponownie, np. z menu kontekstowego pliku PDF.
    /// Pusty tekst oznacza „pokaż okno”.
    /// </summary>
    public void PrzyjmijZInnejKopii(string plik)
    {
        _zlecenia.Enqueue(plik);
        if (IsHandleCreated)
            BeginInvoke(PrzetworzZlecenia);
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

    private CertyfikatInfo? WybranyCertyfikat =>
        _listaCertyfikatow.SelectedItems.Count == 1 ? _listaCertyfikatow.SelectedItems[0].Tag as CertyfikatInfo : null;

    private void WczytajCertyfikaty()
    {
        var poprzedni = WybranyCertyfikat?.Odcisk ?? _ustawienia.OstatniCertyfikat;
        IReadOnlyList<CertyfikatInfo> certyfikaty;
        try
        {
            Cursor = Cursors.WaitCursor;
            certyfikaty = MagazynCertyfikatow.PobierzCertyfikatyDoPodpisu();
        }
        catch (Exception e)
        {
            Blad("Nie udało się odczytać certyfikatów z magazynu Windows.\n\n" + KomunikatyBledow.Opisz(e));
            certyfikaty = Array.Empty<CertyfikatInfo>();
        }
        finally
        {
            Cursor = Cursors.Default;
        }

        _listaCertyfikatow.BeginUpdate();
        _listaCertyfikatow.Items.Clear();
        var pogrubiona = new Font(_listaCertyfikatow.Font, FontStyle.Bold);
        foreach (var c in certyfikaty)
        {
            var pozycja = new ListViewItem(new[]
            {
                c.Kwalifikowany ? "★" : "",
                c.ImieNazwisko,
                c.Wystawca,
                c.WaznyDo.ToString("yyyy-MM-dd"),
            })
            {
                Tag = c,
                ToolTipText = c.Kwalifikowany ? "Certyfikat kwalifikowany" : "Certyfikat niekwalifikowany",
            };
            if (c.Kwalifikowany)
                pozycja.Font = pogrubiona;
            _listaCertyfikatow.Items.Add(pozycja);
        }
        _listaCertyfikatow.ShowItemToolTips = true;

        var doZaznaczenia = _listaCertyfikatow.Items.Cast<ListViewItem>()
            .FirstOrDefault(i => string.Equals(((CertyfikatInfo)i.Tag!).Odcisk, poprzedni, StringComparison.OrdinalIgnoreCase))
            ?? (_listaCertyfikatow.Items.Count > 0 ? _listaCertyfikatow.Items[0] : null);
        if (doZaznaczenia != null)
        {
            doZaznaczenia.Selected = true;
            doZaznaczenia.Focused = true;
            doZaznaczenia.EnsureVisible();
        }
        _listaCertyfikatow.EndUpdate();

        if (certyfikaty.Count == 0)
        {
            UstawStatus("Nie znaleziono certyfikatu do podpisu. Włóż kartę do czytnika (lub zaloguj się w SimplySign Desktop) " +
                        "i kliknij „Odśwież listę”.");
        }
        else
        {
            UstawStatus("");
        }
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
        _strefaPliku.Text = _plik == null
            ? "Przeciągnij tutaj plik PDF\nalbo kliknij, aby go wybrać"
            : $"{Path.GetFileName(_plik)}\n{Path.GetDirectoryName(_plik)}";
        _strefaPliku.Font = _plik == null ? Font : new Font(Font, FontStyle.Bold);
        OdswiezStan();
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
        OdswiezMenuKontekstowe(cicho: false);
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
            opcje.Miejsce = OknoPodgladu.WskazMiejsce(this, wejscie, dane, ustawienia.Polozenie);
            if (opcje.Miejsce == null)
            {
                UstawStatus("Podpisywanie anulowane.");
                return;
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
        _przyciskOdswiez.Enabled = !_trwaPodpisywanie;
        _przyciskUstawienia.Enabled = !_trwaPodpisywanie;
        _listaCertyfikatow.Enabled = !_trwaPodpisywanie;
    }

    private void UstawStatus(string tekst) => _status.Text = tekst;

    private void Blad(string komunikat) =>
        MessageBox.Show(this, komunikat, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
}
