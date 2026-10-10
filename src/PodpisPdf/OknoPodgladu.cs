using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using PodpisPdf.Core;

namespace PodpisPdf;

/// <summary>
/// Podgląd wszystkich stron dokumentu. Kliknięcie wskazuje miejsce wizualizacji podpisu, a przeciągnięcie
/// rysuje ją w wybranym rozmiarze — zawsze w stałych proporcjach (85 : 25).
/// </summary>
internal sealed class OknoPodgladu : Form
{
    private const int OdstepStron = 16;
    private const int MarginesBoczny = 16;
    private const float MaksSkala = 2f;
    // Rozdzielczość podglądu wizualizacji (piksele na punkt przy skali 1) — wystarcza także dla największej skali.
    private const int SkalaPodgladuWizualizacji = 8;
    // Przesunięcie myszy (piksele), od którego kliknięcie staje się rysowaniem.
    private const int ProgPrzeciagania = 5;

    private readonly byte[] _pdf;
    private readonly IReadOnlyList<StronaPdf> _strony;
    private readonly Bitmap _wizualizacja;
    private readonly Panel _przewijanie;
    private readonly Powierzchnia _powierzchnia;
    private readonly Label _informacja;
    private readonly Button _przyciskPodpisz;

    private readonly Dictionary<int, Bitmap> _obrazy = new();
    private readonly List<Rectangle> _uklad = new();
    private float _skala;

    private MiejscePodpisu? _miejsce;
    private MiejscePodpisu? _podMysza;
    // Skala podpisu przy kliknięciu (ostatnio narysowany rozmiar).
    private float _skalaPodpisu;
    // Rysowanie: strona i punkt zaczepienia (punkty PDF) oraz miejsce wciśnięcia przycisku (piksele).
    private (int Indeks, float X, float Y, Point Start)? _przeciaganie;
    private MiejscePodpisu? _rysowane;

    private OknoPodgladu(string plik, byte[] pdf, IReadOnlyList<StronaPdf> strony, Bitmap wizualizacja,
        PolozenieWizualizacji domyslne, float skalaPodpisu)
    {
        _pdf = pdf;
        _strony = strony;
        _wizualizacja = wizualizacja;
        _skalaPodpisu = Math.Clamp(skalaPodpisu, Wizualizacja.MinSkala, Wizualizacja.MaksSkala);

        Text = $"Wskaż miejsce podpisu — {Path.GetFileName(plik)}";
        Font = SystemFonts.MessageBoxFont ?? Font;
        AutoScaleMode = AutoScaleMode.Font;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimizeBox = false;
        KeyPreview = true;
        var ekran = Screen.FromPoint(Cursor.Position).WorkingArea;
        Size = new Size(Math.Min(ekran.Width, (int)(ekran.Height * 0.75)), (int)(ekran.Height * 0.9));
        MinimumSize = new Size(480, 400);

        _przewijanie = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Color.FromArgb(0x52, 0x56, 0x59) };
        _powierzchnia = new Powierzchnia(this) { Location = Point.Empty };
        _przewijanie.Controls.Add(_powierzchnia);
        _przewijanie.Resize += (_, _) => UlozStrony();
        _przewijanie.Scroll += (_, _) => OdswiezInformacje();
        _przewijanie.MouseWheel += (_, _) => OdswiezInformacje();

        _informacja = new Label { AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 8, 0, 0) };
        _przyciskPodpisz = new Button
        {
            Text = "Podpisz",
            AutoSize = true,
            Padding = new Padding(20, 4, 20, 4),
            Font = new Font(Font, FontStyle.Bold),
            DialogResult = DialogResult.OK,
        };
        var anuluj = new Button { Text = "Anuluj", AutoSize = true, Padding = new Padding(12, 4, 12, 4), DialogResult = DialogResult.Cancel };
        var dol = new TableLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, ColumnCount = 3, Padding = new Padding(10) };
        dol.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        dol.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        dol.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        dol.Controls.Add(_informacja, 0, 0);
        dol.Controls.Add(anuluj, 1, 0);
        dol.Controls.Add(_przyciskPodpisz, 2, 0);

        Controls.Add(_przewijanie);
        Controls.Add(dol);
        AcceptButton = _przyciskPodpisz;
        CancelButton = anuluj;

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
        byte[] pdf;
        IReadOnlyList<StronaPdf> strony;
        Bitmap wizualizacja;
        Cursor.Current = Cursors.WaitCursor;
        try
        {
            pdf = File.ReadAllBytes(plik);
            strony = DokumentPdf.Strony(plik);
            if (strony.Count == 0)
                throw new BladPodpisu("Dokument PDF nie ma żadnej strony.");
            wizualizacja = NaBitmape(DokumentPdf.Renderuj(Wizualizacja.PodgladPdf(dane), 0, SkalaPodgladuWizualizacji, bialeTlo: false));
        }
        finally
        {
            Cursor.Current = Cursors.Default;
        }

        using var okno = new OknoPodgladu(plik, pdf, strony, wizualizacja, domyslne, skalaPodpisu);
        okno.ShowDialog(wlasciciel);
        return okno.Miejsce;
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

    // ---- Układ stron ----

    private static SizeF RozmiarWidoczny(StronaPdf s) =>
        s.Obrot % 180 == 0 ? new SizeF(s.Szerokosc, s.Wysokosc) : new SizeF(s.Wysokosc, s.Szerokosc);

    private void UlozStrony()
    {
        if (_strony.Count == 0)
            return;
        var szerokoscPola = Math.Max(200, _przewijanie.ClientSize.Width - 2 * MarginesBoczny - SystemInformation.VerticalScrollBarWidth);
        var maksSzerokosc = _strony.Max(s => RozmiarWidoczny(s).Width);
        var skala = Math.Min(MaksSkala, szerokoscPola / maksSzerokosc);

        if (Math.Abs(skala - _skala) > 0.001f)
        {
            foreach (var b in _obrazy.Values)
                b.Dispose();
            _obrazy.Clear();
        }
        _skala = skala;

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
        OdswiezInformacje();
    }

    private void OdswiezInformacje()
    {
        var gora = -_przewijanie.AutoScrollPosition.Y + _przewijanie.ClientSize.Height / 3;
        var biezaca = _uklad.FindIndex(r => r.Bottom + OdstepStron >= gora) + 1;
        if (biezaca <= 0)
            biezaca = _strony.Count;
        var miejsce = _miejsce == null
            ? "Kliknij na stronie, aby wskazać miejsce podpisu, albo przeciągnij, aby go narysować."
            : $"Podpis na stronie {_miejsce.NumerStrony}, {OpisRozmiaru(_miejsce.Skala)}. " +
              "Kliknij, aby przesunąć, albo przeciągnij, aby narysować w innym rozmiarze.";
        _informacja.Text = $"Strona {biezaca} z {_strony.Count}.   {miejsce}";
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

    /// <summary>Punkt PDF pod kursorem na stronie o danym indeksie (bez ograniczania do strony).</summary>
    private (float X, float Y) PunktPdf(int indeks, Point p)
    {
        var s = _strony[indeks];
        var r = _uklad[indeks];
        return (s.Lewo + (p.X - r.X) / _skala, s.Gora - (p.Y - r.Y) / _skala);
    }

    private int StronaPod(Point p) => _uklad.FindIndex(r => r.Contains(p));

    /// <summary>Podpis w bieżącej skali, wyśrodkowany pod kursorem.</summary>
    private MiejscePodpisu? MiejscePodKursorem(Point p)
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
    /// Prostokąt rysowany od punktu zaczepienia do kursora: proporcje stałe, rozmiar w granicach skali
    /// i strony; kierunek rysowania dowolny.
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

    // ---- Obsługa myszy ----

    private void PrzyWcisnieciu(Point p)
    {
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
        _przeciaganie = (indeks, x, y, p);
        _rysowane = null;
    }

    private void PrzyRuchuMyszy(Point p)
    {
        if (_przeciaganie is { } d)
        {
            if (_rysowane == null && Math.Abs(p.X - d.Start.X) < ProgPrzeciagania && Math.Abs(p.Y - d.Start.Y) < ProgPrzeciagania)
                return;
            Uniewaznij(_podMysza);
            _podMysza = null;
            if (_rysowane == null)
                Uniewaznij(_miejsce); // dotychczasowy podpis przygasa na czas rysowania
            Uniewaznij(_rysowane);
            _rysowane = ProstokatRysowany(d.Indeks, d.X, d.Y, p);
            Uniewaznij(_rysowane);
            _informacja.Text = $"Rozmiar podpisu: {OpisRozmiaru(_rysowane.Skala)}";
            return;
        }

        var nowe = MiejscePodKursorem(p);
        if (nowe == _podMysza)
            return;
        Uniewaznij(_podMysza);
        _podMysza = nowe;
        Uniewaznij(_podMysza);
        var indeks = StronaPod(p);
        _powierzchnia.Cursor = indeks >= 0 && _strony[indeks].Obrot == 0 ? Cursors.Cross : Cursors.Default;
    }

    private void PrzyZwolnieniu(Point p)
    {
        if (_przeciaganie is not { } d)
            return;
        _przeciaganie = null;

        // Bez przeciągnięcia — zwykłe kliknięcie: podpis w ostatnim rozmiarze, wyśrodkowany w punkcie kliknięcia.
        var nowe = _rysowane ?? MiejscePodKursorem(d.Start);
        _rysowane = null;
        if (nowe == null)
            return;
        Uniewaznij(_miejsce);
        _miejsce = nowe;
        _skalaPodpisu = nowe.Skala;
        Uniewaznij(_miejsce);
        OdswiezInformacje();
    }

    private void PrzyOpuszczeniu()
    {
        Uniewaznij(_podMysza);
        _podMysza = null;
    }

    private void Uniewaznij(MiejscePodpisu? m)
    {
        if (m != null && m.NumerStrony - 1 < _uklad.Count)
        {
            var r = ProstokatNaEkranie(m);
            r.Inflate(4, 4);
            r.Height += 24; // etykieta z rozmiarem pod prostokątem
            _powierzchnia.Invalidate(r);
        }
    }

    // ---- Rysowanie ----

    private void Rysuj(Graphics g, Rectangle obszar)
    {
        if (_uklad.Count == 0)
            return;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        var widoczne = new HashSet<int>();
        for (var i = 0; i < _uklad.Count; i++)
        {
            var r = _uklad[i];
            if (!r.IntersectsWith(obszar))
                continue;
            widoczne.Add(i);
            using (var cien = new SolidBrush(Color.FromArgb(70, 0, 0, 0)))
                g.FillRectangle(cien, r.X + 3, r.Y + 3, r.Width, r.Height);
            var obraz = ObrazStrony(i);
            if (obraz != null)
                g.DrawImage(obraz, r);
            else
                g.FillRectangle(Brushes.White, r);
        }
        ZwolnijNiewidoczne(widoczne);

        if (_miejsce != null)
            RysujPodpis(g, obszar, _miejsce, przezroczystosc: _rysowane == null ? 1f : 0.3f, linia: DashStyle.Solid, grubosc: 2);

        if (_rysowane != null)
        {
            // W trakcie rysowania: nowy rozmiar na żywo, z podpisem rozmiaru w milimetrach.
            RysujPodpis(g, obszar, _rysowane, przezroczystosc: 0.85f, linia: DashStyle.Dash, grubosc: 2);
            var r = ProstokatNaEkranie(_rysowane);
            var opis = OpisRozmiaru(_rysowane.Skala);
            var rozmiar = g.MeasureString(opis, Font);
            var tlo = new RectangleF(r.Left, r.Bottom + 3, rozmiar.Width + 6, rozmiar.Height + 2);
            using var pedzel = new SolidBrush(Color.FromArgb(0, 120, 215));
            g.FillRectangle(pedzel, tlo);
            g.DrawString(opis, Font, Brushes.White, tlo.X + 3, tlo.Y + 1);
            return;
        }

        if (_podMysza != null && _podMysza != _miejsce)
            RysujPodpis(g, obszar, _podMysza, przezroczystosc: 0.45f, linia: DashStyle.Dash, grubosc: 1);
    }

    private void RysujPodpis(Graphics g, Rectangle obszar, MiejscePodpisu m, float przezroczystosc, DashStyle linia, float grubosc)
    {
        var r = ProstokatNaEkranie(m);
        if (!r.IntersectsWith(obszar))
            return;
        using (var atrybuty = new ImageAttributes())
        {
            atrybuty.SetColorMatrix(new ColorMatrix { Matrix33 = przezroczystosc });
            g.DrawImage(_wizualizacja, r, 0, 0, _wizualizacja.Width, _wizualizacja.Height, GraphicsUnit.Pixel, atrybuty);
        }
        using var pioro = new Pen(Color.FromArgb(0, 120, 215), grubosc) { DashStyle = linia };
        g.DrawRectangle(pioro, r);
    }

    private Bitmap? ObrazStrony(int indeks)
    {
        if (_obrazy.TryGetValue(indeks, out var b))
            return b;
        try
        {
            b = NaBitmape(DokumentPdf.Renderuj(_pdf, indeks, _skala));
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

    private static Bitmap NaBitmape(ObrazStrony obraz)
    {
        var b = new Bitmap(obraz.Szerokosc, obraz.Wysokosc, PixelFormat.Format32bppArgb);
        var dane = b.LockBits(new Rectangle(0, 0, b.Width, b.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            var wiersz = obraz.Szerokosc * 4;
            for (var y = 0; y < obraz.Wysokosc; y++)
                Marshal.Copy(obraz.Bgra, y * wiersz, dane.Scan0 + y * dane.Stride, wiersz);
        }
        finally
        {
            b.UnlockBits(dane);
        }
        return b;
    }

    /// <summary>Powierzchnia z narysowanymi stronami (wewnątrz przewijanego panelu).</summary>
    private sealed class Powierzchnia : Control
    {
        private readonly OknoPodgladu _okno;

        public Powierzchnia(OknoPodgladu okno)
        {
            _okno = okno;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Color.FromArgb(0x52, 0x56, 0x59);
        }

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
}
