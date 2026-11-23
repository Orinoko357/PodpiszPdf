using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Weryfikator.Logika;

namespace Weryfikator
{
    internal sealed class OknoGlowne : Form
    {
        private readonly List<string> _pliki = new List<string>();
        private List<WynikPodpisu> _wyniki = new List<WynikPodpisu>();
        private ListaPesel? _pesele;
        private ListaZaufania? _lista;
        private Task? _pobieranie;
        private CancellationTokenSource? _przerwij;

        private readonly Button _btnPliki = Przycisk("Dodaj pliki…");
        private readonly Button _btnFolder = Przycisk("Dodaj folder…");
        private readonly Button _btnWyczysc = Przycisk("Wyczyść");
        private readonly Button _btnPesel = Przycisk("Lista PESEL (xlsx)…");
        private readonly Button _btnPodglad = Przycisk("Podgląd listy…");
        private readonly Button _btnSprawdz = Przycisk("Sprawdź podpisy");
        private readonly Button _btnRaport = Przycisk("Zapisz raport…");
        private readonly Label _lblPesel = new Label { AutoSize = true, Margin = new Padding(3, 9, 12, 0), Text = "Nie wybrano listy PESEL" };
        private readonly CheckBox _chkOnline = new CheckBox { AutoSize = true, Checked = true, Margin = new Padding(6, 6, 16, 0), Text = "Sprawdzaj unieważnienie certyfikatów (CRL/OCSP)" };
        private readonly LinkLabel _lnkLista = new LinkLabel { AutoSize = true, Margin = new Padding(3, 8, 16, 0), Text = "Lista zaufania UE: wczytywanie…" };
        private readonly LinkLabel _lnkCerty = new LinkLabel { AutoSize = true, Margin = new Padding(3, 8, 3, 0), Text = "Zaufane certyfikaty (podpis osobisty)…" };
        private readonly DataGridView _siatka = new DataGridView();
        private readonly TextBox _szczegoly = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Bottom, Height = 96, BackColor = SystemColors.Window };
        private readonly ToolStripStatusLabel _stan = new ToolStripStatusLabel("Dodaj pliki (można je też przeciągnąć na okno), wybierz listę PESEL i kliknij „Sprawdź podpisy”.") { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
        private readonly ToolStripProgressBar _postep = new ToolStripProgressBar { Visible = false, Width = 180 };

        private static Button Przycisk(string tekst) => new Button { Text = tekst, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(8, 3, 8, 3), Margin = new Padding(3, 3, 3, 3) };

        public OknoGlowne(string[] args, bool start = true)
        {
            var wersja = Assembly.GetExecutingAssembly().GetName().Version;
            Text = $"Weryfikator {wersja?.ToString(3)} — podpisy elektroniczne i numery PESEL";
            Font = new Font("Segoe UI", 9f);
            AutoScaleDimensions = new SizeF(96f, 96f);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(1180, 640);
            MinimumSize = new Size(760, 420);
            StartPosition = FormStartPosition.CenterScreen;
            AllowDrop = true;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch (Exception) { }

            _btnSprawdz.Font = new Font(Font, FontStyle.Bold);
            var gora = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(6, 6, 6, 0), WrapContents = true };
            gora.Controls.AddRange(new Control[] { _btnPliki, _btnFolder, _btnWyczysc, _btnPesel, _btnPodglad, _lblPesel, _btnSprawdz, _btnRaport });
            var opcje = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(6, 0, 6, 6), WrapContents = true };
            opcje.Controls.AddRange(new Control[] { _chkOnline, _lnkLista, _lnkCerty });

            _siatka.Dock = DockStyle.Fill;
            _siatka.ReadOnly = true;
            _siatka.AllowUserToAddRows = false;
            _siatka.AllowUserToDeleteRows = false;
            _siatka.AllowUserToResizeRows = false;
            _siatka.RowHeadersVisible = false;
            _siatka.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _siatka.MultiSelect = false;
            _siatka.BackgroundColor = SystemColors.Window;
            _siatka.BorderStyle = BorderStyle.None;
            _siatka.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
            _siatka.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            Kolumna("Wynik", 70); Kolumna("Plik", 230); Kolumna("Nr", 32); Kolumna("Ważność podpisu", 115); Kolumna("Rodzaj podpisu", 120);
            Kolumna("Podpisujący", 170); Kolumna("PESEL", 95); Kolumna("Na liście", 65); Kolumna("Czas podpisu", 150);
            var uwagi = Kolumna("Uwagi", 100);
            uwagi.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            uwagi.MinimumWidth = 220 * DeviceDpi / 96;

            var pasek = new StatusStrip();
            pasek.Items.Add(_stan);
            pasek.Items.Add(_postep);

            Controls.Add(_siatka);
            Controls.Add(_szczegoly);
            Controls.Add(opcje);
            Controls.Add(gora);
            Controls.Add(pasek);

            _btnPliki.Click += (s, e) => WybierzPliki();
            _btnFolder.Click += (s, e) => WybierzFolder();
            _btnWyczysc.Click += (s, e) => { _pliki.Clear(); _wyniki.Clear(); Odswiez(); };
            _btnPesel.Click += (s, e) => WybierzListePesel();
            _btnPodglad.Click += (s, e) => PodgladListy();
            _btnPodglad.Enabled = false;
            _btnSprawdz.Click += async (s, e) => await Sprawdz();
            _btnRaport.Click += (s, e) => ZapiszRaport();
            _lnkLista.LinkClicked += (s, e) => PobierzListe(true);
            _lnkCerty.LinkClicked += (s, e) =>
            {
                try { Srodowisko.UtworzFolderCertyfikatow(); Process.Start(Srodowisko.FolderCertyfikatow); }
                catch (Exception ex) { Blad(ex.Message); }
            };
            _siatka.SelectionChanged += (s, e) => PokazSzczegoly();
            _siatka.CellDoubleClick += (s, e) =>
            {
                if (e.RowIndex < 0 || !(_siatka.Rows[e.RowIndex].Tag is string plik)) return;
                try { Process.Start(plik); } catch (Exception ex) { Blad(ex.Message); }
            };
            DragEnter += (s, e) => { if (e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy; };
            DragDrop += (s, e) => { if (e.Data?.GetData(DataFormats.FileDrop) is string[] p) Dodaj(p); };
            if (start) Shown += (s, e) => Start(args);
            Odswiez();
        }

        /// <summary>Zrzut okna z gotowymi wynikami do pliku PNG (kontrola wyglądu w GitHub Actions).</summary>
        public static void Zrzut(string plik, List<WynikPodpisu> wyniki, ListaPesel? pesele, ListaZaufania? lista)
        {
            using var okno = new OknoGlowne(new string[0], false);
            okno._wyniki = wyniki;
            okno._pliki.AddRange(wyniki.Select(w => w.Plik).Distinct());
            okno._pesele = pesele;
            okno._lista = lista;
            if (pesele != null) { okno._lblPesel.Text = $"{Path.GetFileName(pesele.Plik)}: {pesele.Numery.Count} numerów PESEL"; okno._btnPodglad.Enabled = true; }
            okno.Show();
            okno.OpisListy(null);
            okno.Odswiez();
            Application.DoEvents();
            using var bmp = new Bitmap(okno.Width, okno.Height);
            okno.DrawToBitmap(bmp, new Rectangle(Point.Empty, okno.Size));
            bmp.Save(plik, System.Drawing.Imaging.ImageFormat.Png);
            if (pesele == null) return;
            using var podglad = new OknoListy(pesele) { StartPosition = FormStartPosition.Manual, Location = new Point(40, 40) };
            podglad.Show();
            Application.DoEvents();
            using var bmp2 = new Bitmap(podglad.Width, podglad.Height);
            podglad.DrawToBitmap(bmp2, new Rectangle(Point.Empty, podglad.Size));
            bmp2.Save(Path.Combine(Path.GetDirectoryName(plik)!, "lista.png"), System.Drawing.Imaging.ImageFormat.Png);
        }

        private DataGridViewColumn Kolumna(string naglowek, int szerokosc)
        {
            var k = _siatka.Columns[_siatka.Columns.Add(naglowek, naglowek)];
            k.Width = szerokosc * DeviceDpi / 96;
            k.SortMode = DataGridViewColumnSortMode.Automatic;
            return k;
        }

        private void Start(string[] args)
        {
            try
            {
                if (File.Exists(Srodowisko.PlikUstawien))
                {
                    var u = File.ReadAllLines(Srodowisko.PlikUstawien);
                    if (u.Length > 0 && File.Exists(u[0].Trim()))
                    {
                        int a = -1, k = -1;
                        if (u.Length > 2) { int.TryParse(u[1], out a); int.TryParse(u[2], out k); }
                        WczytajListePesel(u[0].Trim(), false, a, k);
                    }
                }
            }
            catch (Exception) { }
            Dodaj(args);
            _pobieranie = Task.Run(() => ListaZaufania.Wczytaj(Srodowisko.PlikListy)).ContinueWith(t =>
            {
                _lista = t.Result;
                OpisListy(null);
                if (_lista == null || DateTime.UtcNow - _lista.Pobrano > Srodowisko.WaznoscListy) PobierzListe(false);
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        // ---------- lista zaufania

        private void PobierzListe(bool recznie)
        {
            if (_pobieranie != null && !_pobieranie.IsCompleted) return;
            _lnkLista.Text = "Lista zaufania UE: pobieranie…";
            var poprzednia = _lista;
            _pobieranie = Task.Run(() => Srodowisko.PobierzListe(poprzednia, tekst => BeginInvoke((Action)(() => _lnkLista.Text = tekst))))
                .ContinueWith(t =>
                {
                    if (t.Status == TaskStatus.RanToCompletion)
                    {
                        _lista = t.Result;
                        OpisListy(null);
                        if (recznie && _lista.Ostrzezenia.Count > 0)
                            MessageBox.Show(this, "Listę pobrano, ale z uwagami:\n\n" + string.Join("\n", _lista.Ostrzezenia), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                    else
                    {
                        var blad = t.Exception?.GetBaseException().Message ?? "nieznany błąd";
                        OpisListy(blad);
                        if (recznie) Blad("Nie udało się pobrać listy zaufania UE:\n" + blad);
                    }
                }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        private void OpisListy(string? blad)
        {
            if (_lista == null)
                _lnkLista.Text = blad == null ? "Lista zaufania UE: brak — kliknij, aby pobrać" : "Lista zaufania UE: nie udało się pobrać — kliknij, aby ponowić";
            else
                _lnkLista.Text = $"Lista zaufania UE: {_lista.Pobrano.ToLocalTime():dd.MM.yyyy HH:mm}, {_lista.Kraje.Count} krajów" +
                                 (blad != null ? " (nie udało się odświeżyć)" : _lista.Ostrzezenia.Count > 0 ? " (z uwagami)" : "") + " — odśwież";
            _lnkLista.LinkColor = _lista == null ? Color.Firebrick : SystemColors.HotTrack;
        }

        // ---------- pliki i lista PESEL

        private void WybierzPliki()
        {
            using var d = new OpenFileDialog
            {
                Multiselect = true, Title = "Wybierz podpisane pliki",
                Filter = "Podpisane dokumenty (*.pdf;*.xml;*.xades;*.sig;*.p7s;*.p7m)|*.pdf;*.xml;*.xades;*.sig;*.p7s;*.p7m|Wszystkie pliki (*.*)|*.*"
            };
            if (d.ShowDialog(this) == DialogResult.OK) Dodaj(d.FileNames);
        }

        private void WybierzFolder()
        {
            using var d = new FolderBrowserDialog { Description = "Folder z podpisanymi plikami (razem z podfolderami)", ShowNewFolderButton = false };
            if (d.ShowDialog(this) == DialogResult.OK) Dodaj(new[] { d.SelectedPath });
        }

        private void Dodaj(IEnumerable<string> sciezki)
        {
            bool zmiana = false;
            foreach (var s in sciezki)
            {
                var roz = Path.GetExtension(s).ToLowerInvariant();
                if (File.Exists(s) && (roz == ".xlsx" || roz == ".xlsm")) { WczytajListePesel(s, true); continue; }
                foreach (var p in Srodowisko.PlikiZ(s))
                    if (!_pliki.Contains(p, StringComparer.OrdinalIgnoreCase)) { _pliki.Add(p); zmiana = true; }
            }
            if (!zmiana) return;
            _wyniki.Clear();
            Odswiez();
        }

        private void WybierzListePesel()
        {
            using var d = new OpenFileDialog { Title = "Wybierz listę numerów PESEL", Filter = "Arkusz Excel (*.xlsx)|*.xlsx;*.xlsm|Plik tekstowy lub CSV (*.txt;*.csv)|*.txt;*.csv" };
            if (d.ShowDialog(this) == DialogResult.OK) WczytajListePesel(d.FileName, true);
        }

        private void WczytajListePesel(string plik, bool komunikaty, int arkusz = -1, int kolumna = -1)
        {
            try
            {
                var l = ListaPesel.Wczytaj(plik);
                // Zapamiętany wybór kolumny stosujemy tylko, gdy nadal daje jakieś numery.
                if (arkusz >= 0 || kolumna >= 0)
                {
                    l.Wybierz(arkusz, kolumna);
                    if (l.Numery.Count == 0) l.Wybierz(-1, -1);
                }
                if (l.Numery.Count == 0)
                {
                    if (komunikaty) Blad("W pliku nie znaleziono żadnego numeru PESEL.");
                    return;
                }
                _pesele = l;
                ListaZmieniona();
                if (komunikaty && l.Bledne.Count > 0)
                    MessageBox.Show(this, "Na liście są numery o błędnej sumie kontrolnej (pominięto je — sprawdź, czy to nie literówki):\n\n" +
                                          string.Join("\n", l.Bledne.Take(20)) + (l.Bledne.Count > 20 ? "\n…" : ""), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (Exception e) { if (komunikaty) Blad("Nie można wczytać listy PESEL:\n" + e.Message); }
        }

        private void PodgladListy()
        {
            if (_pesele == null) return;
            using var okno = new OknoListy(_pesele);
            if (okno.ShowDialog(this) != DialogResult.OK) return;
            _pesele.Wybierz(okno.WybranyArkusz, okno.WybranaKolumna);
            if (_pesele.Numery.Count == 0)
                Blad("W wybranej kolumnie nie ma żadnego numeru PESEL — żaden podpisujący nie zostanie uznany za obecnego na liście.");
            ListaZmieniona();
        }

        /// <summary>Opis listy, zapamiętanie wyboru i ponowne porównanie numerów w już sprawdzonych podpisach.</summary>
        private void ListaZmieniona()
        {
            var l = _pesele!;
            _btnPodglad.Enabled = true;
            _lblPesel.Text = $"{Path.GetFileName(l.Plik)}: {l.Numery.Count} numerów PESEL" +
                             (l.WybranaKolumna >= 0 ? $" (kolumna {ListaPesel.NazwaKolumny(l.WybranaKolumna)})" : "");
            try
            {
                Directory.CreateDirectory(Srodowisko.FolderDanych);
                File.WriteAllLines(Srodowisko.PlikUstawien, new[] { l.Plik, l.WybranyArkusz.ToString(), l.WybranaKolumna.ToString() });
            }
            catch (Exception) { }
            if (_wyniki.Count == 0) return;
            foreach (var w in _wyniki)
            {
                w.Uwagi.Remove("Numeru PESEL podpisującego nie ma na liście.");
                w.BezListyPesel = false;
                w.NaLiscie = w.Pesel == null ? (bool?)null : l.Zawiera(w.Pesel);
                if (w.NaLiscie == false) w.Uwagi.Add("Numeru PESEL podpisującego nie ma na liście.");
            }
            Odswiez();
        }

        // ---------- sprawdzanie

        private async Task Sprawdz()
        {
            if (_przerwij != null) { _przerwij.Cancel(); return; }
            if (_pliki.Count == 0) { Blad("Najpierw dodaj pliki do sprawdzenia."); return; }
            if (_pesele == null &&
                MessageBox.Show(this, "Nie wybrano listy numerów PESEL.\nSprawdzić tylko ważność podpisów?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

            _przerwij = new CancellationTokenSource();
            var token = _przerwij.Token;
            Zablokuj(true);
            try
            {
                if (_pobieranie != null && !_pobieranie.IsCompleted)
                {
                    _stan.Text = "Pobieranie listy zaufania UE…";
                    await _pobieranie;
                }
                if (_lista == null)
                {
                    PobierzListe(false);
                    _stan.Text = "Pobieranie listy zaufania UE…";
                    if (_pobieranie != null) await _pobieranie;
                }
                if (_lista == null &&
                    MessageBox.Show(this, "Nie udało się pobrać listy zaufania UE (brak internetu?).\nBez niej nie da się potwierdzić ważności podpisów kwalifikowanych i zaufanych.\n\nSprawdzić mimo to?",
                        Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;

                var u = new Ustawienia { Lista = _lista, Pesele = _pesele, Online = _chkOnline.Checked, CertyfikatyUzytkownika = Srodowisko.CertyfikatyUzytkownika() };
                var pliki = _pliki.ToList();
                var wyniki = new List<WynikPodpisu>();
                _postep.Maximum = pliki.Count; _postep.Value = 0; _postep.Visible = true;
                await Task.Run(() =>
                {
                    for (int i = 0; i < pliki.Count && !token.IsCancellationRequested; i++)
                    {
                        int nr = i;
                        BeginInvoke((Action)(() => { _postep.Value = nr; _stan.Text = $"Sprawdzanie {nr + 1} z {pliki.Count}: {Path.GetFileName(pliki[nr])}"; }));
                        wyniki.AddRange(Sprawdzanie.Plik(pliki[i], u));
                    }
                });
                if (token.IsCancellationRequested)
                    foreach (var p in pliki.Where(p => wyniki.All(w => w.Plik != p)))
                        wyniki.Add(new WynikPodpisu { Plik = p, Status = Status.Blad, Uwagi = { "Nie sprawdzono (przerwano)." } });
                _wyniki = wyniki;
                Odswiez();
            }
            catch (Exception e) { Blad("Błąd podczas sprawdzania:\n" + e.Message); }
            finally
            {
                _postep.Visible = false;
                _przerwij = null;
                Zablokuj(false);
                Podsumuj();
            }
        }

        private void Zablokuj(bool trwa)
        {
            foreach (var b in new[] { _btnPliki, _btnFolder, _btnWyczysc, _btnPesel, _btnRaport }) b.Enabled = !trwa;
            _btnPodglad.Enabled = !trwa && _pesele != null;
            _btnSprawdz.Text = trwa ? "Przerwij" : "Sprawdź podpisy";
            AllowDrop = !trwa;
            if (!trwa) _btnRaport.Enabled = _wyniki.Count > 0;
        }

        private static readonly Color Zielony = Color.FromArgb(223, 240, 216), Zolty = Color.FromArgb(255, 243, 205), Czerwony = Color.FromArgb(248, 215, 218);

        private void Odswiez()
        {
            _siatka.SuspendLayout();
            _siatka.Rows.Clear();
            if (_wyniki.Count == 0)
                foreach (var p in _pliki)
                    _siatka.Rows[_siatka.Rows.Add("", Path.GetFileName(p), "", "do sprawdzenia", "", "", "", "", "", "")].Tag = p;
            foreach (var w in _wyniki)
            {
                int i = _siatka.Rows.Add(w.WPorzadku ? "OK" : "SPRAWDŹ", Path.GetFileName(w.Plik), w.Nr == 0 ? "" : w.Nr.ToString(), w.StatusTekst, w.RodzajTekst,
                    w.Podpisujacy, w.PeselTekst, w.NaLiscieTekst, w.CzasTekst, string.Join(" ", w.Uwagi));
                var wiersz = _siatka.Rows[i];
                wiersz.Tag = w.Plik;
                var kolor = w.WPorzadku ? Zielony
                    : w.Status == Status.Niewazny || w.Status == Status.BrakPodpisu || w.Status == Status.Blad || w.NaLiscie == false ? Czerwony : Zolty;
                wiersz.DefaultCellStyle.BackColor = kolor;
                wiersz.DefaultCellStyle.SelectionBackColor = ControlPaint.Dark(kolor, 0.25f);
                wiersz.DefaultCellStyle.SelectionForeColor = Color.Black;
                wiersz.Cells[0].Style.Font = new Font(Font, FontStyle.Bold);
            }
            _siatka.ResumeLayout();
            _btnRaport.Enabled = _wyniki.Count > 0;
            _btnWyczysc.Enabled = _pliki.Count > 0;
            Podsumuj();
            PokazSzczegoly();
        }

        private void Podsumuj()
        {
            if (_wyniki.Count == 0)
            {
                _stan.Text = _pliki.Count == 0
                    ? "Dodaj pliki (można je też przeciągnąć na okno), wybierz listę PESEL i kliknij „Sprawdź podpisy”."
                    : $"Plików do sprawdzenia: {_pliki.Count}.";
                return;
            }
            int ok = _wyniki.Count(w => w.WPorzadku);
            _stan.Text = $"Plików: {_wyniki.Select(w => w.Plik).Distinct().Count()}, podpisów: {_wyniki.Count(w => w.Nr > 0)}. " +
                         $"W porządku ({(_pesele == null ? "ważny podpis" : "ważny podpis i PESEL na liście")}): {ok}. Do sprawdzenia: {_wyniki.Count - ok} " +
                         $"(nieważnych: {_wyniki.Count(w => w.Status == Status.Niewazny)}, bez podpisu: {_wyniki.Count(w => w.Status == Status.BrakPodpisu)}, " +
                         $"PESEL spoza listy: {_wyniki.Count(w => w.NaLiscie == false)}).";
        }

        private void PokazSzczegoly()
        {
            var wiersz = _siatka.CurrentRow;
            if (wiersz == null || _wyniki.Count == 0) { _szczegoly.Text = ""; return; }
            // Po sortowaniu kolejność wierszy różni się od listy wyników — szukamy po treści wiersza.
            var plik = wiersz.Tag as string;
            var nr = Convert.ToString(wiersz.Cells[2].Value);
            var w = _wyniki.FirstOrDefault(x => x.Plik == plik && (x.Nr == 0 ? "" : x.Nr.ToString()) == nr);
            if (w == null) { _szczegoly.Text = ""; return; }
            _szczegoly.Text = w.Plik + "\r\n" +
                              (w.Nr > 0 ? $"Podpis {w.Nr}: {w.StatusTekst}, rodzaj: {w.RodzajTekst}, podpisujący: {w.Podpisujacy}, PESEL: {w.PeselTekst}, wystawca certyfikatu: {w.Wystawca}\r\n" : "") +
                              string.Join("\r\n", w.Uwagi.Select(x => "• " + x));
        }

        private void ZapiszRaport()
        {
            using var d = new SaveFileDialog { Title = "Zapisz raport", Filter = "Plik CSV dla Excela (*.csv)|*.csv", FileName = $"Weryfikator-raport-{DateTime.Now:yyyy-MM-dd-HHmm}.csv" };
            if (d.ShowDialog(this) != DialogResult.OK) return;
            try { Raport.ZapiszCsv(d.FileName, _wyniki); Process.Start(d.FileName); }
            catch (Exception e) { Blad("Nie udało się zapisać raportu:\n" + e.Message); }
        }

        private void Blad(string tekst) => MessageBox.Show(this, tekst, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }
}
