using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using PodpisPdf.Core;

namespace PodpisPdf;

/// <summary>
/// Podgląd wszystkich stron dokumentu. Kliknięcie wskazuje miejsce wizualizacji podpisu (rozmiar jest stały).
/// </summary>
internal sealed class OknoPodgladu : Form
{
    private const int OdstepStron = 16;
    private const int MarginesBoczny = 16;
    private const float MaksSkala = 2f;
    private const int SkalaPodgladuWizualizacji = 4;

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
    private (int Indeks, float X, float Y)? _podMysza;

    private OknoPodgladu(string plik, byte[] pdf, IReadOnlyList<StronaPdf> strony, Bitmap wizualizacja, PolozenieWizualizacji domyslne)
    {
        _pdf = pdf;
        _strony = strony;
        _wizualizacja = wizualizacja;

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
    public static MiejscePodpisu? WskazMiejsce(IWin32Window wlasciciel, string plik, DaneWizualizacji dane, PolozenieWizualizacji domyslne)
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

        using var okno = new OknoPodgladu(plik, pdf, strony, wizualizacja, domyslne);
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
            var r = Wizualizacja.ObliczProstokat(strona.Prostokat, p);
            return new MiejscePodpisu(numer, r.GetX(), r.GetY());
        }
        catch (BladPodpisu)
        {
            // Położenie z ustawień nie mieści się na tej stronie — użytkownik wskaże je kliknięciem.
            var (x, y) = Wizualizacja.DopasujDoStrony(strona.Prostokat, strona.Prawo, strona.Dol);
            return new MiejscePodpisu(numer, x, y);
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
        var r = ProstokatNaEkranie(_miejsce.NumerStrony - 1, _miejsce.X, _miejsce.Y);
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
            ? "Kliknij na stronie, aby wskazać miejsce podpisu."
            : $"Podpis na stronie {_miejsce.NumerStrony}. Kliknij w innym miejscu, aby go przesunąć.";
        _informacja.Text = $"Strona {biezaca} z {_strony.Count}.   {miejsce}";
        _przyciskPodpisz.Enabled = _miejsce != null;
    }

    // ---- Przeliczanie współrzędnych (piksele podglądu ↔ punkty PDF) ----

    private Rectangle ProstokatNaEkranie(int indeks, float x, float y)
    {
        var s = _strony[indeks];
        var r = _uklad[indeks];
        var lewo = r.X + (x - s.Lewo) * _skala;
        var gora = r.Y + (s.Gora - (y + Wizualizacja.WysokoscPt)) * _skala;
        return Rectangle.Round(new RectangleF(lewo, gora, Wizualizacja.SzerokoscPt * _skala, Wizualizacja.WysokoscPt * _skala));
    }

    private (int Indeks, float X, float Y)? MiejscePodKursorem(Point p)
    {
        if (_uklad.Count == 0)
            return null;
        var indeks = _uklad.FindIndex(r => r.Contains(p));
        if (indeks < 0 || _strony[indeks].Obrot != 0)
            return null;
        var s = _strony[indeks];
        var r = _uklad[indeks];
        var srodekX = s.Lewo + (p.X - r.X) / _skala;
        var srodekY = s.Gora - (p.Y - r.Y) / _skala;
        var (x, y) = Wizualizacja.DopasujDoStrony(s.Prostokat, srodekX, srodekY);
        return (indeks, x, y);
    }

    // ---- Obsługa myszy ----

    private void PrzyRuchuMyszy(Point p)
    {
        var nowe = MiejscePodKursorem(p);
        if (nowe == _podMysza)
            return;
        Uniewaznij(_podMysza);
        _podMysza = nowe;
        Uniewaznij(_podMysza);
        var indeks = _uklad.FindIndex(r => r.Contains(p));
        _powierzchnia.Cursor = indeks >= 0 && _strony[indeks].Obrot == 0 ? Cursors.Cross : Cursors.Default;
    }

    private void PrzyKliknieciu(Point p)
    {
        var indeks = _uklad.FindIndex(r => r.Contains(p));
        if (indeks >= 0 && _strony[indeks].Obrot != 0)
        {
            MessageBox.Show(this, "Ta strona jest obrócona — podpisu nie można na niej umieścić. Wskaż miejsce na innej stronie.",
                Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var nowe = MiejscePodKursorem(p);
        if (nowe == null)
            return;
        if (_miejsce != null)
            Uniewaznij((_miejsce.NumerStrony - 1, _miejsce.X, _miejsce.Y));
        _miejsce = new MiejscePodpisu(nowe.Value.Indeks + 1, nowe.Value.X, nowe.Value.Y);
        Uniewaznij(nowe);
        OdswiezInformacje();
    }

    private void PrzyOpuszczeniu()
    {
        Uniewaznij(_podMysza);
        _podMysza = null;
    }

    private void Uniewaznij((int Indeks, float X, float Y)? m)
    {
        if (m is { } v && v.Indeks < _uklad.Count)
        {
            var r = ProstokatNaEkranie(v.Indeks, v.X, v.Y);
            r.Inflate(4, 4);
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
        {
            var r = ProstokatNaEkranie(_miejsce.NumerStrony - 1, _miejsce.X, _miejsce.Y);
            if (r.IntersectsWith(obszar))
            {
                g.DrawImage(_wizualizacja, r);
                using var pioro = new Pen(Color.FromArgb(0, 120, 215), 2);
                g.DrawRectangle(pioro, r);
            }
        }

        if (_podMysza is { } m && (_miejsce == null || m.Indeks != _miejsce.NumerStrony - 1 || m.X != _miejsce.X || m.Y != _miejsce.Y))
        {
            var r = ProstokatNaEkranie(m.Indeks, m.X, m.Y);
            if (r.IntersectsWith(obszar))
            {
                using var atrybuty = new ImageAttributes();
                atrybuty.SetColorMatrix(new ColorMatrix { Matrix33 = 0.45f });
                g.DrawImage(_wizualizacja, r, 0, 0, _wizualizacja.Width, _wizualizacja.Height, GraphicsUnit.Pixel, atrybuty);
                using var pioro = new Pen(Color.FromArgb(0, 120, 215), 1) { DashStyle = DashStyle.Dash };
                g.DrawRectangle(pioro, r);
            }
        }
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

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button == MouseButtons.Left)
                _okno.PrzyKliknieciu(e.Location);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Parent?.Focus(); // kółko myszy przewija panel
        }
    }
}
