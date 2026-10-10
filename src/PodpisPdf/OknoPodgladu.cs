using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using PodpisPdf.Core;
using PodpisPdf.Fluent;

namespace PodpisPdf;

/// <summary>
/// Podgląd wszystkich stron dokumentu. Kliknięcie umieszcza wizualizację podpisu, przeciągnięcie (w trybie
/// rysowania) rysuje ją w wybranym rozmiarze, a uchwyty i przeciąganie podpisu pozwalają go poprawić.
/// Proporcje są zawsze stałe (85 : 25).
/// </summary>
internal sealed class OknoPodgladu : Form
{
    private const int OdstepStron = 16;
    private const int MarginesBoczny = 24;
    private const float MaksSkalaDopasowania = 2f;
    // Rozdzielczość podglądu wizualizacji (piksele na punkt przy skali 1) — wystarcza także dla największej skali.
    private const int SkalaPodgladuWizualizacji = 8;
    // Przesunięcie myszy (piksele), od którego kliknięcie staje się rysowaniem.
    private const int ProgPrzeciagania = 5;
    private const int RozmiarUchwytu = 10;
    private static readonly int[] Powiekszenia = { 50, 67, 75, 100, 125, 150, 200, 300 };

    private readonly byte[] _pdf;
    private readonly IReadOnlyList<StronaPdf> _strony;
    private readonly Bitmap _wizualizacja;
    private readonly Panel _przewijanie;
    private readonly Powierzchnia _powierzchnia;
    private readonly PasekMiniatur _miniatury;
    private readonly FluentEtykieta _informacja;
    private readonly FluentEtykieta _procent;
    private readonly FluentSegment _tryb;
    private readonly FluentPrzycisk _przyciskPodpisz;

    private readonly Dictionary<int, Bitmap> _obrazy = new();
    private readonly List<Rectangle> _uklad = new();
    private float _skala;
    private bool _dopasuj = true;
    private int _powiekszenie = 100;

    private MiejscePodpisu? _miejsce;
    private MiejscePodpisu? _podMysza;
    // Skala podpisu przy kliknięciu (ostatnio narysowany rozmiar).
    private float _skalaPodpisu;
    private Przeciaganie? _przeciaganie;
    private MiejscePodpisu? _rysowane;
    private bool _pokazPodpowiedz = true;

    private enum RodzajPrzeciagania { Klikniecie, Rysowanie, Przesuwanie }

    /// <summary>Rozpoczęte przeciąganie: strona, punkt zaczepienia (PDF) i miejsce wciśnięcia (piksele).</summary>
    private sealed record Przeciaganie(RodzajPrzeciagania Rodzaj, int Indeks, float X, float Y, Point Start);

    private OknoPodgladu(string plik, byte[] pdf, IReadOnlyList<StronaPdf> strony, Bitmap wizualizacja,
        PolozenieWizualizacji domyslne, float skalaPodpisu)
    {
        _pdf = pdf;
        _strony = strony;
        _wizualizacja = wizualizacja;
        _skalaPodpisu = Math.Clamp(skalaPodpisu, Wizualizacja.MinSkala, Wizualizacja.MaksSkala);

        SuspendLayout();
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = $"Wskaż miejsce podpisu — {Path.GetFileName(plik)}";
        Icon = Ustawienia.Ikona();
        Font = Motyw.Czcionka(10);
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimizeBox = false;
        KeyPreview = true;
        var ekran = Screen.FromPoint(Cursor.Position).WorkingArea;
        Size = new Size(Math.Min(ekran.Width, (int)(ekran.Height * 0.95)), (int)(ekran.Height * 0.9));
        MinimumSize = new Size(640, 480);

        // ---- pasek poleceń ----
        var pasek = new FluentKarta { Dock = DockStyle.Top, Height = 60, Padding = new Padding(16, 13, 16, 13), Rodzaj = RodzajKarty.Warstwa, Obramowanie = false, Promien = 0 };
        var pomniejsz = new FluentPrzycisk { Text = "", Ikona = "", Size = new Size(40, 34), AccessibleName = "Pomniejsz", Dock = DockStyle.Left };
        _procent = new FluentEtykieta("100%", StylEtykiety.Zwykly) { AutoSize = false, Width = 64, Dock = DockStyle.Left, TextAlign = ContentAlignment.MiddleCenter };
        var powieksz = new FluentPrzycisk { Text = "", Ikona = "", Size = new Size(40, 34), AccessibleName = "Powiększ", Dock = DockStyle.Left };
        var odstep = new Panel { Width = 12, Dock = DockStyle.Left };
        var dopasuj = new FluentPrzycisk { Text = "Dopasuj do szerokości", AutoSize = true, Dock = DockStyle.Left };
        _tryb = new FluentSegment("Kliknij i umieść", "Narysuj rozmiar") { Width = 300, Height = 34, Dock = DockStyle.Right };
        pomniejsz.Click += (_, _) => ZmienPowiekszenie(-1);
        powieksz.Click += (_, _) => ZmienPowiekszenie(+1);
        dopasuj.Click += (_, _) => { _dopasuj = true; UlozStrony(); };
        _tryb.ZmianaWyboru += (_, _) => { _pokazPodpowiedz = true; _powierzchnia?.Invalidate(); OdswiezInformacje(); };
        pasek.Controls.Add(dopasuj);
        pasek.Controls.Add(odstep);
        pasek.Controls.Add(powieksz);
        pasek.Controls.Add(_procent);
        pasek.Controls.Add(pomniejsz);
        pasek.Controls.Add(_tryb);

        // ---- miniatury stron ----
        var bok = new FluentKarta { Dock = DockStyle.Left, Width = 156, Rodzaj = RodzajKarty.Warstwa, Obramowanie = false, Promien = 0, Padding = new Padding(8) };
        _miniatury = new PasekMiniatur(this) { Dock = DockStyle.Fill };
        bok.Controls.Add(_miniatury);

        // ---- strony ----
        _przewijanie = new PanelPlotna { Dock = DockStyle.Fill, AutoScroll = true };
        _powierzchnia = new Powierzchnia(this) { Location = Point.Empty };
        _przewijanie.Controls.Add(_powierzchnia);
        _przewijanie.Resize += (_, _) => UlozStrony();
        _przewijanie.Scroll += (_, _) => PoPrzewinieciu();
        _przewijanie.MouseWheel += (_, e) =>
        {
            if ((ModifierKeys & Keys.Control) != 0)
                ZmienPowiekszenie(e.Delta > 0 ? +1 : -1);
            PoPrzewinieciu();
        };

        // ---- stopka ----
        var stopka = new FluentKarta { Dock = DockStyle.Bottom, Height = 64, Rodzaj = RodzajKarty.Warstwa, Obramowanie = false, Promien = 0, Padding = new Padding(20, 0, 20, 0) };
        var ukladStopki = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1 };
        ukladStopki.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        ukladStopki.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        ukladStopki.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        ukladStopki.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _informacja = new FluentEtykieta("", StylEtykiety.Drugi) { AutoSize = false, Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
        var anuluj = new FluentPrzycisk { Text = "Anuluj", AutoSize = true, MinimumSize = new Size(110, 34), DialogResult = DialogResult.Cancel, Anchor = AnchorStyles.None };
        _przyciskPodpisz = new FluentPrzycisk { Text = "Podpisz", Akcent = true, AutoSize = true, MinimumSize = new Size(130, 34), DialogResult = DialogResult.OK, Anchor = AnchorStyles.None, Margin = new Padding(10, 0, 0, 0) };
        ukladStopki.Controls.Add(_informacja, 0, 0);
        ukladStopki.Controls.Add(anuluj, 1, 0);
        ukladStopki.Controls.Add(_przyciskPodpisz, 2, 0);
        stopka.Controls.Add(ukladStopki);

        Controls.Add(_przewijanie);
        Controls.Add(bok);
        Controls.Add(stopka);
        Controls.Add(pasek);
        AcceptButton = _przyciskPodpisz;
        CancelButton = anuluj;
        ResumeLayout(false);
        PerformLayout();

        Motyw.Zastosuj(this);
        Motyw.Zmieniono += PrzyZmianieMotywu;

        _miejsce = MiejsceDomyslne(domyslne);
        Shown += (_, _) =>
        {
            UlozStrony();
            PrzewinDoMiejsca();
            _przewijanie.Focus();
        };
    }

    /// <summary>Miejsce wybrane przez użytkownika; null po anulowaniu.</summary>
    public MiejscePodpisu? Miejsce => DialogResult == DialogResult.OK ? _miejsce : null;

    /// <summary>Pokazuje podgląd i zwraca wskazane miejsce albo null, gdy użytkownik zrezygnował.</summary>
    public static MiejscePodpisu? WskazMiejsce(IWin32Window wlasciciel, string plik, DaneWizualizacji dane,
        PolozenieWizualizacji domyslne, float skalaPodpisu = 1f)
    {
        using var okno = Utworz(plik, dane, domyslne, skalaPodpisu);
        okno.ShowDialog(wlasciciel);
        return okno.Miejsce;
    }

    /// <summary>Tworzy okno podglądu (także do zrzutów ekranu w testach).</summary>
    internal static OknoPodgladu Utworz(string plik, DaneWizualizacji dane, PolozenieWizualizacji domyslne, float skalaPodpisu)
    {
        Cursor.Current = Cursors.WaitCursor;
        try
        {
            var pdf = File.ReadAllBytes(plik);
            var strony = DokumentPdf.Strony(plik);
            if (strony.Count == 0)
                throw new BladPodpisu("Dokument PDF nie ma żadnej strony.");
            var wizualizacja = Obrazy.NaBitmape(DokumentPdf.Renderuj(Wizualizacja.PodgladPdf(dane), 0, SkalaPodgladuWizualizacji, bialeTlo: false));
            return new OknoPodgladu(plik, pdf, strony, wizualizacja, domyslne, skalaPodpisu);
        }
        finally
        {
            Cursor.Current = Cursors.Default;
        }
    }

    /// <summary>Do zrzutów ekranu: tryb rysowania.</summary>
    internal void UstawTrybRysowania() => _tryb.Wybrany = 1;

    private void PrzyZmianieMotywu()
    {
        if (IsHandleCreated)
            BeginInvoke(() => Motyw.Zastosuj(this));
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        Motyw.Zmieniono -= PrzyZmianieMotywu;
        base.OnFormClosed(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var b in _obrazy.Values)
                b.Dispose();
            _obrazy.Clear();
            _wizualizacja.Dispose();
        }
        base.Dispose(disposing);
    }

    private MiejscePodpisu? MiejsceDomyslne(PolozenieWizualizacji p)
    {
        var numer = p.Strona switch
        {
            WyborStrony.Pierwsza => 1,
            WyborStrony.Ostatnia => _strony.Count,
            _ => Math.Clamp(p.NumerStrony, 1, _strony.Count),
        };
        var strona = _strony[numer - 1];
        if (strona.Obrot != 0)
            return null;
        try
        {
            var r = Wizualizacja.ObliczProstokat(strona.Prostokat, p, _skalaPodpisu);
            return new MiejscePodpisu(numer, r.GetX(), r.GetY(), _skalaPodpisu);
        }
        catch (BladPodpisu)
        {
            // Położenie z ustawień nie mieści się na tej stronie — użytkownik wskaże je kliknięciem.
            var (x, y) = Wizualizacja.DopasujDoStrony(strona.Prostokat, strona.Prawo, strona.Dol, _skalaPodpisu);
            return new MiejscePodpisu(numer, x, y, _skalaPodpisu);
        }
    }

    // ---- Układ stron i powiększenie ----

    private static SizeF RozmiarWidoczny(StronaPdf s) =>
        s.Obrot % 180 == 0 ? new SizeF(s.Szerokosc, s.Wysokosc) : new SizeF(s.Wysokosc, s.Szerokosc);

    /// <summary>Piksele na punkt PDF przy 100% (rzeczywisty rozmiar strony na ekranie).</summary>
    private float SkalaRzeczywista => DeviceDpi / 72f;

    private void ZmienPowiekszenie(int kierunek)
    {
        var biezace = (int)Math.Round(_skala / SkalaRzeczywista * 100);
        var nowe = kierunek > 0
            ? Powiekszenia.FirstOrDefault(p => p > biezace + 1, Powiekszenia[^1])
            : Powiekszenia.LastOrDefault(p => p < biezace - 1, Powiekszenia[0]);
        _dopasuj = false;
        _powiekszenie = nowe;
        var srodek = -_przewijanie.AutoScrollPosition.Y + _przewijanie.ClientSize.Height / 2f;
        var wzgledny = _powierzchnia.Height > 0 ? srodek / _powierzchnia.Height : 0;
        UlozStrony();
        _przewijanie.AutoScrollPosition = new Point(0, (int)(wzgledny * _powierzchnia.Height - _przewijanie.ClientSize.Height / 2f));
        PoPrzewinieciu();
    }

    private void UlozStrony()
    {
        if (_strony.Count == 0 || _powierzchnia == null)
            return;
        var szerokoscPola = Math.Max(200, _przewijanie.ClientSize.Width - 2 * MarginesBoczny - SystemInformation.VerticalScrollBarWidth);
        var maksSzerokosc = _strony.Max(s => RozmiarWidoczny(s).Width);
        var skala = _dopasuj
            ? Math.Min(MaksSkalaDopasowania * DeviceDpi / 96f, szerokoscPola / maksSzerokosc)
            : _powiekszenie / 100f * SkalaRzeczywista;

        if (Math.Abs(skala - _skala) > 0.001f)
        {
            foreach (var b in _obrazy.Values)
                b.Dispose();
            _obrazy.Clear();
        }
        _skala = skala;
        _procent.Text = $"{Math.Round(_skala / SkalaRzeczywista * 100)}%";

        _uklad.Clear();
        var y = OdstepStron;
        var szerokoscPowierzchni = _przewijanie.ClientSize.Width - SystemInformation.VerticalScrollBarWidth;
        foreach (var s in _strony)
        {
            var r = RozmiarWidoczny(s);
            var w = (int)Math.Round(r.Width * skala);
            var h = (int)Math.Round(r.Height * skala);
            _uklad.Add(new Rectangle(Math.Max(MarginesBoczny, (szerokoscPowierzchni - w) / 2), y, w, h));
            y += h + OdstepStron;
        }
        _powierzchnia.Size = new Size(Math.Max(szerokoscPowierzchni, _uklad.Max(r => r.Right) + MarginesBoczny), y);
        _powierzchnia.Invalidate();
        OdswiezInformacje();
    }

    private void PrzewinDoMiejsca()
    {
        if (_miejsce == null || _uklad.Count == 0)
            return;
        var r = ProstokatNaEkranie(_miejsce);
        var cel = Math.Max(0, r.Top - _przewijanie.ClientSize.Height / 2);
        _przewijanie.AutoScrollPosition = new Point(0, cel);
        PoPrzewinieciu();
    }

    /// <summary>Przewija do strony (kliknięcie miniatury).</summary>
    private void PrzewinDoStrony(int indeks)
    {
        if (indeks < 0 || indeks >= _uklad.Count)
            return;
        _przewijanie.AutoScrollPosition = new Point(0, Math.Max(0, _uklad[indeks].Top - OdstepStron));
        PoPrzewinieciu();
    }

    private int BiezacaStrona
    {
        get
        {
            var gora = -_przewijanie.AutoScrollPosition.Y + _przewijanie.ClientSize.Height / 3;
            var biezaca = _uklad.FindIndex(r => r.Bottom + OdstepStron >= gora);
            return biezaca < 0 ? _strony.Count - 1 : biezaca;
        }
    }

    private void PoPrzewinieciu()
    {
        _miniatury.Biezaca = BiezacaStrona;
        if (_pokazPodpowiedz)
            _powierzchnia.Invalidate(); // podpowiedź jest przyklejona do górnej krawędzi widoku
        OdswiezInformacje();
    }

    private void OdswiezInformacje()
    {
        var strona = $"Strona {BiezacaStrona + 1} z {_strony.Count}";
        var podpis = _miejsce == null
            ? "kliknij stronę, aby umieścić podpis"
            : $"podpis na stronie {_miejsce.NumerStrony}, {OpisRozmiaru(_miejsce.Skala)}";
        _informacja.Text = $"{strona}  ·  {podpis}";
        _przyciskPodpisz.Enabled = _miejsce != null;
    }

    private static string OpisRozmiaru(float skala) =>
        $"{Wizualizacja.SzerokoscMm * skala:0} × {Wizualizacja.WysokoscMm * skala:0} mm";

    // ---- Przeliczanie współrzędnych (piksele podglądu ↔ punkty PDF) ----

    private Rectangle ProstokatNaEkranie(MiejscePodpisu m)
    {
        var s = _strony[m.NumerStrony - 1];
        var r = _uklad[m.NumerStrony - 1];
        var w = Wizualizacja.SzerokoscPt * m.Skala;
        var h = Wizualizacja.WysokoscPt * m.Skala;
        var lewo = r.X + (m.X - s.Lewo) * _skala;
        var gora = r.Y + (s.Gora - (m.Y + h)) * _skala;
        return Rectangle.Round(new RectangleF(lewo, gora, w * _skala, h * _skala));
    }

    private (float X, float Y) PunktPdf(int indeks, Point p)
    {
        var s = _strony[indeks];
        var r = _uklad[indeks];
        return (s.Lewo + (p.X - r.X) / _skala, s.Gora - (p.Y - r.Y) / _skala);
    }

    private int StronaPod(Point p) => _uklad.FindIndex(r => r.Contains(p));

    /// <summary>Podpis w bieżącej skali, wyśrodkowany w punkcie.</summary>
    private MiejscePodpisu? MiejsceWPunkcie(Point p)
    {
        if (_uklad.Count == 0)
            return null;
        var indeks = StronaPod(p);
        if (indeks < 0 || _strony[indeks].Obrot != 0)
            return null;
        var (srodekX, srodekY) = PunktPdf(indeks, p);
        var (x, y) = Wizualizacja.DopasujDoStrony(_strony[indeks].Prostokat, srodekX, srodekY, _skalaPodpisu);
        return new MiejscePodpisu(indeks + 1, x, y, _skalaPodpisu);
    }

    /// <summary>
    /// Prostokąt od punktu zaczepienia do kursora: proporcje stałe, rozmiar w granicach skali i strony;
    /// kierunek dowolny.
    /// </summary>
    private MiejscePodpisu ProstokatRysowany(int indeks, float ax, float ay, Point kursor)
    {
        var s = _strony[indeks];
        var (cx, cy) = PunktPdf(indeks, kursor);
        cx = Math.Clamp(cx, s.Lewo, s.Prawo);
        cy = Math.Clamp(cy, s.Dol, s.Gora);
        float dx = cx - ax, dy = cy - ay;
        var proporcje = Wizualizacja.Proporcje;
        var bazowa = Wizualizacja.SzerokoscPt;

        var w = Math.Max(Math.Abs(dx), Math.Abs(dy) * proporcje);
        var dostepnaX = dx >= 0 ? s.Prawo - ax : ax - s.Lewo;
        var dostepnaY = (dy >= 0 ? s.Gora - ay : ay - s.Dol) * proporcje;
        w = Math.Min(w, Math.Min(dostepnaX, dostepnaY));
        w = Math.Clamp(w, bazowa * Wizualizacja.MinSkala, bazowa * Wizualizacja.MaksSkala);
        var h = w / proporcje;

        var x = dx >= 0 ? ax : ax - w;
        var y = dy >= 0 ? ay : ay - h;
        var skala = w / bazowa;
        // Przy krawędzi strony minimalny rozmiar może nie zmieścić się od punktu zaczepienia — dosuwamy.
        (x, y) = Wizualizacja.DopasujDoStrony(s.Prostokat, x + w / 2, y + h / 2, skala);
        return new MiejscePodpisu(indeks + 1, x, y, skala);
    }

    /// <summary>Uchwyty w narożnikach podpisu (piksele) i przeciwległe narożniki (punkty PDF) jako zaczepienie.</summary>
    private IEnumerable<(Rectangle Uchwyt, float AX, float AY, Cursor Kursor)> Uchwyty(MiejscePodpisu m)
    {
        var r = ProstokatNaEkranie(m);
        var d = (int)(RozmiarUchwytu * DeviceDpi / 96f);
        var w = Wizualizacja.SzerokoscPt * m.Skala;
        var h = Wizualizacja.WysokoscPt * m.Skala;
        Rectangle U(int x, int y) => new(x - d, y - d, 2 * d, 2 * d);
        yield return (U(r.Left, r.Top), m.X + w, m.Y, Cursors.SizeNWSE);         // lewy górny → zaczep prawy dolny
        yield return (U(r.Right, r.Top), m.X, m.Y, Cursors.SizeNESW);            // prawy górny → lewy dolny
        yield return (U(r.Left, r.Bottom), m.X + w, m.Y + h, Cursors.SizeNESW);  // lewy dolny → prawy górny
        yield return (U(r.Right, r.Bottom), m.X, m.Y + h, Cursors.SizeNWSE);     // prawy dolny → lewy górny
    }

    // ---- Obsługa myszy ----

    private void PrzyWcisnieciu(Point p)
    {
        if (_pokazPodpowiedz)
        {
            _pokazPodpowiedz = false;
            _powierzchnia.Invalidate();
        }
        if (_miejsce != null)
        {
            foreach (var u in Uchwyty(_miejsce))
            {
                if (u.Uchwyt.Contains(p))
                {
                    // Zmiana rozmiaru: rysowanie od przeciwległego narożnika, od razu (bez progu).
                    _przeciaganie = new Przeciaganie(RodzajPrzeciagania.Rysowanie, _miejsce.NumerStrony - 1, u.AX, u.AY, new Point(-1000, -1000));
                    return;
                }
            }
            if (ProstokatNaEkranie(_miejsce).Contains(p))
            {
                var (px, py) = PunktPdf(_miejsce.NumerStrony - 1, p);
                _przeciaganie = new Przeciaganie(RodzajPrzeciagania.Przesuwanie, _miejsce.NumerStrony - 1, px - _miejsce.X, py - _miejsce.Y, p);
                return;
            }
        }

        var indeks = StronaPod(p);
        if (indeks < 0)
            return;
        if (_strony[indeks].Obrot != 0)
        {
            MessageBox.Show(this, "Ta strona jest obrócona — podpisu nie można na niej umieścić. Wskaż miejsce na innej stronie.",
                Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var (x, y) = PunktPdf(indeks, p);
        _przeciaganie = new Przeciaganie(_tryb.Wybrany == 1 ? RodzajPrzeciagania.Rysowanie : RodzajPrzeciagania.Klikniecie, indeks, x, y, p);
        _rysowane = null;
    }

    private void PrzyRuchuMyszy(Point p)
    {
        if (_przeciaganie is { } d)
        {
            if (d.Rodzaj == RodzajPrzeciagania.Klikniecie)
                return;
            if (_rysowane == null && Math.Abs(p.X - d.Start.X) < ProgPrzeciagania && Math.Abs(p.Y - d.Start.Y) < ProgPrzeciagania)
                return;
            UstawPodMysza(null);
            if (_rysowane == null)
                Uniewaznij(_miejsce); // dotychczasowy podpis przygasa na czas zmiany
            Uniewaznij(_rysowane);
            if (d.Rodzaj == RodzajPrzeciagania.Rysowanie)
            {
                _rysowane = ProstokatRysowany(d.Indeks, d.X, d.Y, p);
            }
            else if (_miejsce != null)
            {
                var (px, py) = PunktPdf(d.Indeks, p);
                var w = Wizualizacja.SzerokoscPt * _miejsce.Skala;
                var h = Wizualizacja.WysokoscPt * _miejsce.Skala;
                var (nx, ny) = Wizualizacja.DopasujDoStrony(_strony[d.Indeks].Prostokat, px - d.X + w / 2, py - d.Y + h / 2, _miejsce.Skala);
                _rysowane = _miejsce with { X = nx, Y = ny };
            }
            Uniewaznij(_rysowane);
            if (_rysowane != null)
                _informacja.Text = $"Rozmiar podpisu: {OpisRozmiaru(_rysowane.Skala)}";
            return;
        }

        // Kursor: uchwyty, przesuwanie, strona.
        if (_miejsce != null)
        {
            var uchwyt = Uchwyty(_miejsce).FirstOrDefault(u => u.Uchwyt.Contains(p));
            if (uchwyt.Kursor != null)
            {
                UstawPodMysza(null);
                _powierzchnia.Cursor = uchwyt.Kursor;
                return;
            }
            if (ProstokatNaEkranie(_miejsce).Contains(p))
            {
                UstawPodMysza(null);
                _powierzchnia.Cursor = Cursors.SizeAll;
                return;
            }
        }
        UstawPodMysza(MiejsceWPunkcie(p));
        var indeks = StronaPod(p);
        _powierzchnia.Cursor = indeks >= 0 && _strony[indeks].Obrot == 0 ? Cursors.Cross : Cursors.Default;
    }

    private void UstawPodMysza(MiejscePodpisu? nowe)
    {
        if (nowe == _podMysza)
            return;
        Uniewaznij(_podMysza);
        _podMysza = nowe;
        Uniewaznij(_podMysza);
    }

    private void PrzyZwolnieniu(Point p)
    {
        if (_przeciaganie is not { } d)
            return;
        _przeciaganie = null;

        // Bez przeciągnięcia — zwykłe kliknięcie: podpis w ostatnim rozmiarze, wyśrodkowany w punkcie kliknięcia.
        var nowe = _rysowane ?? (d.Rodzaj == RodzajPrzeciagania.Przesuwanie ? null : MiejsceWPunkcie(d.Start));
        _rysowane = null;
        Uniewaznij(_miejsce);
        if (nowe == null)
            return;
        _miejsce = nowe;
        _skalaPodpisu = nowe.Skala;
        Uniewaznij(_miejsce);
        OdswiezInformacje();
    }

    private void PrzyOpuszczeniu() => UstawPodMysza(null);

    private void Uniewaznij(MiejscePodpisu? m)
    {
        if (m != null && m.NumerStrony - 1 < _uklad.Count)
        {
            var r = ProstokatNaEkranie(m);
            var d = (int)(RozmiarUchwytu * DeviceDpi / 96f) + 4;
            r.Inflate(d, d);
            r.Height += (int)(36 * DeviceDpi / 96f); // etykieta z rozmiarem pod prostokątem
            _powierzchnia.Invalidate(r);
        }
    }

    // ---- Rysowanie ----

    private void Rysuj(Graphics g, Rectangle obszar)
    {
        if (_uklad.Count == 0)
            return;
        var p = Motyw.Biezaca;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        var widoczne = new HashSet<int>();
        for (var i = 0; i < _uklad.Count; i++)
        {
            var r = _uklad[i];
            if (!r.IntersectsWith(obszar))
                continue;
            widoczne.Add(i);
            using (var cien = new SolidBrush(Color.FromArgb(p.Ciemna ? 120 : 40, 0, 0, 0)))
                g.FillRectangle(cien, r.X + 2, r.Y + 3, r.Width, r.Height);
            var obraz = ObrazStrony(i);
            if (obraz != null)
                g.DrawImage(obraz, r);
            else
                g.FillRectangle(Brushes.White, r);
        }
        ZwolnijNiewidoczne(widoczne);

        if (_miejsce != null)
        {
            var wygaszony = _rysowane != null;
            RysujPodpis(g, obszar, _miejsce, wygaszony ? 0.3f : 1f, DashStyle.Solid, 2);
            if (!wygaszony)
            {
                RysujUchwyty(g, _miejsce);
                RysujEtykieteRozmiaru(g, _miejsce);
            }
        }
        if (_rysowane != null)
        {
            RysujPodpis(g, obszar, _rysowane, 0.85f, DashStyle.Dash, 2);
            RysujEtykieteRozmiaru(g, _rysowane);
        }
        else if (_podMysza != null && _podMysza != _miejsce)
        {
            RysujPodpis(g, obszar, _podMysza, 0.45f, DashStyle.Dash, 1);
        }

        if (_pokazPodpowiedz)
            RysujPodpowiedz(g);
    }

    private void RysujPodpis(Graphics g, Rectangle obszar, MiejscePodpisu m, float przezroczystosc, DashStyle linia, float grubosc)
    {
        var r = ProstokatNaEkranie(m);
        if (!r.IntersectsWith(obszar))
            return;
        using (var nakladka = new SolidBrush(Color.FromArgb(18, Paleta.Jasny.Akcent)))
            g.FillRectangle(nakladka, r);
        using (var atrybuty = new ImageAttributes())
        {
            atrybuty.SetColorMatrix(new ColorMatrix { Matrix33 = przezroczystosc });
            g.DrawImage(_wizualizacja, r, 0, 0, _wizualizacja.Width, _wizualizacja.Height, GraphicsUnit.Pixel, atrybuty);
        }
        using var pioro = new Pen(Paleta.Jasny.Akcent, grubosc * DeviceDpi / 96f) { DashStyle = linia };
        g.DrawRectangle(pioro, r);
    }

    private void RysujUchwyty(Graphics g, MiejscePodpisu m)
    {
        var dpi = DeviceDpi / 96f;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        foreach (var u in Uchwyty(m))
        {
            var r = RectangleF.Inflate(u.Uchwyt, -RozmiarUchwytu * dpi / 2, -RozmiarUchwytu * dpi / 2);
            Grafika.Wypelnij(g, r, 2 * dpi, Color.White);
            Grafika.Obrysuj(g, r, 2 * dpi, Paleta.Jasny.Akcent, 2 * dpi);
        }
    }

    private void RysujEtykieteRozmiaru(Graphics g, MiejscePodpisu m)
    {
        var dpi = DeviceDpi / 96f;
        var r = ProstokatNaEkranie(m);
        var tekst = OpisRozmiaru(m.Skala);
        using var f = Motyw.Czcionka(9, FontStyle.Bold);
        var rozmiar = TextRenderer.MeasureText(g, tekst, f);
        var pig = new RectangleF(r.Left + (r.Width - rozmiar.Width - 16 * dpi) / 2, r.Bottom + 10 * dpi, rozmiar.Width + 16 * dpi, 24 * dpi);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        Grafika.Wypelnij(g, pig, pig.Height / 2, Paleta.Jasny.Akcent);
        TextRenderer.DrawText(g, tekst, f, Rectangle.Round(pig), Color.White,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    /// <summary>Podpowiedź u góry widoku (znika po pierwszym kliknięciu).</summary>
    private void RysujPodpowiedz(Graphics g)
    {
        var dpi = DeviceDpi / 96f;
        var tekst = _tryb.Wybrany == 1
            ? "Przeciągnij na stronie, aby narysować podpis — proporcje są zachowane"
            : "Kliknij stronę, aby umieścić podpis. Uchwyty w narożnikach zmieniają rozmiar.";
        using var f = Motyw.Czcionka(10);
        var rozmiar = TextRenderer.MeasureText(g, tekst, f);
        var gora = -_przewijanie.AutoScrollPosition.Y + 14 * dpi;
        var szer = rozmiar.Width + 36 * dpi;
        var r = new RectangleF((_powierzchnia.Width - szer) / 2, gora, szer, 36 * dpi);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        Grafika.Wypelnij(g, r, r.Height / 2, Color.FromArgb(235, 32, 32, 32));
        TextRenderer.DrawText(g, tekst, f, Rectangle.Round(r), Color.White,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    private Bitmap? ObrazStrony(int indeks)
    {
        if (_obrazy.TryGetValue(indeks, out var b))
            return b;
        try
        {
            b = Obrazy.NaBitmape(DokumentPdf.Renderuj(_pdf, indeks, _skala));
        }
        catch (Exception)
        {
            return null; // Strona, której nie da się wyrenderować, pokazujemy jako pustą.
        }
        _obrazy[indeks] = b;
        return b;
    }

    /// <summary>Ogranicza zużycie pamięci przy długich dokumentach.</summary>
    private void ZwolnijNiewidoczne(HashSet<int> widoczne)
    {
        if (_obrazy.Count <= 8 || widoczne.Count == 0)
            return;
        var min = widoczne.Min() - 2;
        var max = widoczne.Max() + 2;
        foreach (var i in _obrazy.Keys.Where(i => i < min || i > max).ToList())
        {
            _obrazy[i].Dispose();
            _obrazy.Remove(i);
        }
    }

    /// <summary>Tło obszaru stron w kolorze motywu.</summary>
    private sealed class PanelPlotna : Panel, IFluentKontrolka
    {
        public override Color BackColor { get => Motyw.Biezaca.Plotno; set { } }
    }

    /// <summary>Powierzchnia z narysowanymi stronami (wewnątrz przewijanego panelu).</summary>
    private sealed class Powierzchnia : Control, IFluentKontrolka
    {
        private readonly OknoPodgladu _okno;

        public Powierzchnia(OknoPodgladu okno)
        {
            _okno = okno;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        public override Color BackColor { get => Motyw.Biezaca.Plotno; set { } }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            _okno.Rysuj(e.Graphics, e.ClipRectangle);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            _okno.PrzyRuchuMyszy(e.Location);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _okno.PrzyOpuszczeniu();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Parent?.Focus(); // kółko myszy przewija panel
            if (e.Button == MouseButtons.Left)
                _okno.PrzyWcisnieciu(e.Location);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left)
                _okno.PrzyZwolnieniu(e.Location);
        }
    }

    /// <summary>Miniatury stron po lewej; kliknięcie przewija do strony.</summary>
    private sealed class PasekMiniatur : ScrollableControl, IFluentKontrolka
    {
        private const int Szerokosc = 104;
        private const int Odstep = 14;
        private readonly OknoPodgladu _okno;
        private readonly Dictionary<int, Bitmap> _obrazy = new();
        private readonly List<Rectangle> _uklad = new();
        private int _biezaca;

        public PasekMiniatur(OknoPodgladu okno)
        {
            _okno = okno;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            AutoScroll = true;
            Cursor = Cursors.Hand;
        }

        public override Color BackColor { get => Parent?.BackColor ?? Motyw.Biezaca.TloWarstwy; set { } }

        public int Biezaca
        {
            get => _biezaca;
            set
            {
                if (_biezaca == value) return;
                _biezaca = value;
                Invalidate();
                if (value >= 0 && value < _uklad.Count)
                {
                    var r = _uklad[value];
                    var widok = new Rectangle(0, -AutoScrollPosition.Y, ClientSize.Width, ClientSize.Height);
                    if (!widok.Contains(r))
                        AutoScrollPosition = new Point(0, Math.Max(0, r.Top - 8));
                }
            }
        }

        private float Dpi => DeviceDpi / 96f;

        private void Uloz()
        {
            _uklad.Clear();
            var y = (int)(8 * Dpi);
            var w = (int)(Szerokosc * Dpi);
            foreach (var s in _okno._strony)
            {
                var r = RozmiarWidoczny(s);
                var h = (int)(w * r.Height / r.Width);
                _uklad.Add(new Rectangle((ClientSize.Width - w) / 2, y, w, h));
                y += h + (int)((Odstep + 18) * Dpi);
            }
            AutoScrollMinSize = new Size(0, y);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Uloz();
            Invalidate();
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            var p = new Point(e.X, e.Y - AutoScrollPosition.Y);
            var indeks = _uklad.FindIndex(r => Rectangle.Inflate(r, 6, 6).Contains(p));
            if (indeks >= 0)
                _okno.PrzewinDoStrony(indeks);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var p = Motyw.Biezaca;
            using (var tlo = new SolidBrush(BackColor))
                g.FillRectangle(tlo, ClientRectangle);
            if (_uklad.Count == 0) Uloz();
            g.TranslateTransform(0, AutoScrollPosition.Y);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var widok = new Rectangle(0, -AutoScrollPosition.Y, ClientSize.Width, ClientSize.Height);
            using var f = Motyw.Czcionka(9);
            for (var i = 0; i < _uklad.Count; i++)
            {
                var r = _uklad[i];
                if (!Rectangle.Inflate(r, 0, 30).IntersectsWith(widok))
                    continue;
                var wybrana = i == _biezaca;
                if (wybrana)
                    Grafika.Obrysuj(g, RectangleF.Inflate(r, 5 * Dpi, 5 * Dpi), 6 * Dpi, p.Akcent, 2 * Dpi);
                var obraz = Obraz(i, r.Width);
                if (obraz != null) g.DrawImage(obraz, r); else g.FillRectangle(Brushes.White, r);
                using (var ramka = new Pen(p.Ramka))
                    g.DrawRectangle(ramka, r);
                var y = (int)(r.Bottom + 6 * Dpi);
                TextRenderer.DrawText(g, (i + 1).ToString(), f, new Rectangle(r.X, y, r.Width, (int)(16 * Dpi)),
                    wybrana ? p.Akcent : p.TekstDrugi, TextFormatFlags.HorizontalCenter);
            }
        }

        private Bitmap? Obraz(int indeks, int szerokosc)
        {
            if (_obrazy.TryGetValue(indeks, out var b))
                return b;
            try
            {
                var s = RozmiarWidoczny(_okno._strony[indeks]);
                b = Obrazy.NaBitmape(DokumentPdf.Renderuj(_okno._pdf, indeks, szerokosc / s.Width));
                _obrazy[indeks] = b;
                return b;
            }
            catch (Exception)
            {
                return null;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                foreach (var b in _obrazy.Values) b.Dispose();
                _obrazy.Clear();
            }
            base.Dispose(disposing);
        }
    }
}
