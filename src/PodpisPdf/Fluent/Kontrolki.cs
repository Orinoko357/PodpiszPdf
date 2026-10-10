using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Drawing.Drawing2D;
using PodpisPdf.Core;

namespace PodpisPdf.Fluent;

/// <summary>Wspólna baza kontrolek rysowanych samodzielnie (podwójne buforowanie, skala DPI).</summary>
internal abstract class FluentKontrolka : Control, IFluentKontrolka
{
    protected FluentKontrolka()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
    }

    protected Paleta P => Motyw.Biezaca;
    protected float Dpi => DeviceDpi / 96f;
    protected int D(float v) => (int)Math.Round(v * Dpi);

    protected bool Najechany { get; private set; }
    protected bool Wcisniety { get; private set; }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); Najechany = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); Najechany = false; Wcisniety = false; Invalidate(); }
    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (e.Button == MouseButtons.Left) { Wcisniety = true; Invalidate(); } }
    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); Wcisniety = false; Invalidate(); }
    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

    protected void TloRodzica(Graphics g)
    {
        using var b = new SolidBrush(Parent?.BackColor ?? P.Tlo);
        g.FillRectangle(b, ClientRectangle);
    }

    protected void RamkaFokusu(Graphics g, RectangleF r, float promien)
    {
        if (Focused && ShowFocusCues)
            Grafika.Obrysuj(g, RectangleF.Inflate(r, D(1.5f), D(1.5f)), promien + D(1.5f), P.Tekst, D(2));
    }
}

/// <summary>Przycisk Fluent: standardowy albo akcentowy, z opcjonalną ikoną (glif Segoe Fluent Icons).</summary>
internal sealed class FluentPrzycisk : Button, IFluentKontrolka, IButtonControl
{
    private bool _najechany, _wcisniety;

    public FluentPrzycisk()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        Font = Motyw.Czcionka(10);
        Cursor = Cursors.Default;
        Padding = new Padding(14, 0, 14, 0);
        MinimumSize = new Size(0, 32);
    }

    [DefaultValue(false)] public bool Akcent { get; set; }
    [DefaultValue(null)] public string? Ikona { get; set; }

    private Paleta P => Motyw.Biezaca;
    private float Dpi => DeviceDpi / 96f;

    public override Size GetPreferredSize(Size proposedSize)
    {
        var tekst = TextRenderer.MeasureText(Text, Font);
        var ikona = Ikona != null ? (int)(22 * Dpi) : 0;
        return new Size(tekst.Width + Padding.Horizontal + ikona, Math.Max((int)(32 * Dpi), tekst.Height + (int)(12 * Dpi)));
    }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _najechany = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _najechany = false; _wcisniety = false; Invalidate(); }
    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); _wcisniety = true; Invalidate(); }
    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); _wcisniety = false; Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Grafika.Wygladzanie(g);
        using (var tlo = new SolidBrush(Parent?.BackColor ?? P.Tlo))
            g.FillRectangle(tlo, ClientRectangle);

        var p = P;
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        var promien = 4 * Dpi;
        Color wypelnienie, tekst, ramka;
        if (Akcent)
        {
            wypelnienie = !Enabled ? p.NeutralnyTlo : _wcisniety ? p.AkcentWcisniety : _najechany ? p.AkcentNajechany : p.Akcent;
            tekst = Enabled ? p.TekstNaAkcencie : p.TekstWylaczony;
            ramka = wypelnienie;
        }
        else
        {
            wypelnienie = _wcisniety ? p.PrzyciskWcisniety : _najechany ? p.PrzyciskNajechany : p.Przycisk;
            tekst = Enabled ? p.Tekst : p.TekstWylaczony;
            ramka = p.RamkaSilna;
        }
        Grafika.Wypelnij(g, r, promien, wypelnienie);
        Grafika.Obrysuj(g, r, promien, ramka);
        if (Focused && ShowFocusCues)
            Grafika.Obrysuj(g, RectangleF.Inflate(r, -2 * Dpi, -2 * Dpi), promien - Dpi, p.Tekst, 1.5f * Dpi);

        var rozmiarTekstu = TextRenderer.MeasureText(g, Text, Font);
        var szerokoscIkony = Ikona != null ? (int)(22 * Dpi) : 0;
        var x = (Width - rozmiarTekstu.Width - szerokoscIkony) / 2;
        if (Ikona != null)
        {
            using var f = Motyw.Ikony(10);
            TextRenderer.DrawText(g, Ikona, f, new Rectangle(x, 0, szerokoscIkony, Height), tekst,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding);
            x += szerokoscIkony;
        }
        TextRenderer.DrawText(g, Text, Font, new Rectangle(x, 0, rozmiarTekstu.Width + 2, Height), tekst,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
    }
}

internal enum RodzajKarty { Karta, Informacja, Warstwa, Zaznaczenie }

/// <summary>Karta (zaokrąglony panel z obramowaniem) — tło dla grupy kontrolek.</summary>
internal class FluentKarta : Panel, IFluentKontrolka
{
    public FluentKarta()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Padding = new Padding(16);
    }

    [DefaultValue(RodzajKarty.Karta)] public RodzajKarty Rodzaj { get; set; }
    [DefaultValue(true)] public bool Obramowanie { get; set; } = true;
    public float Promien { get; set; } = 8;

    /// <summary>Kolor wypełnienia karty — dzieci bez własnego tła dziedziczą go automatycznie.</summary>
    public override Color BackColor
    {
        get
        {
            var p = Motyw.Biezaca;
            return Rodzaj switch
            {
                RodzajKarty.Informacja or RodzajKarty.Zaznaczenie => p.AkcentTlo,
                RodzajKarty.Warstwa => p.TloWarstwy,
                _ => p.Karta,
            };
        }
        set { }
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var g = e.Graphics;
        Grafika.Wygladzanie(g);
        using (var tlo = new SolidBrush(Parent?.BackColor ?? Motyw.Biezaca.Tlo))
            g.FillRectangle(tlo, ClientRectangle);
        var dpi = DeviceDpi / 96f;
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        Grafika.Wypelnij(g, r, Promien * dpi, BackColor);
        if (Obramowanie)
            Grafika.Obrysuj(g, r, Promien * dpi, Rodzaj == RodzajKarty.Zaznaczenie ? Motyw.Biezaca.Akcent : Motyw.Biezaca.Ramka,
                Rodzaj == RodzajKarty.Zaznaczenie ? 2 * dpi : 1);
    }
}

internal enum StylEtykiety { Zwykly, Drugi, Tytul, Podtytul, Sekcja, Pogrubiony, Link, Ikona, IkonaAkcent }

/// <summary>Etykieta w kolorach motywu; styl wyznacza czcionkę i kolor.</summary>
internal sealed class FluentEtykieta : Label, IFluentKontrolka
{
    private StylEtykiety _styl;

    public FluentEtykieta(string tekst = "", StylEtykiety styl = StylEtykiety.Zwykly)
    {
        Text = tekst;
        AutoSize = true;
        UseMnemonic = false;
        Margin = new Padding(0);
        Styl = styl;
    }

    public StylEtykiety Styl
    {
        get => _styl;
        set
        {
            _styl = value;
            Font = value switch
            {
                StylEtykiety.Tytul => Motyw.Naglowek(20),
                StylEtykiety.Podtytul => Motyw.Naglowek(13),
                StylEtykiety.Sekcja => Motyw.Czcionka(8, FontStyle.Bold),
                StylEtykiety.Pogrubiony => Motyw.Czcionka(10.5f, FontStyle.Bold),
                StylEtykiety.Ikona or StylEtykiety.IkonaAkcent => Motyw.Ikony(12),
                StylEtykiety.Drugi => Motyw.Czcionka(9.5f),
                _ => Motyw.Czcionka(10),
            };
            Cursor = value == StylEtykiety.Link ? Cursors.Hand : Cursors.Default;
        }
    }

    public override Color ForeColor
    {
        get
        {
            var p = Motyw.Biezaca;
            if (!Enabled) return p.TekstWylaczony;
            return _styl switch
            {
                StylEtykiety.Drugi or StylEtykiety.Sekcja => p.TekstDrugi,
                StylEtykiety.Link or StylEtykiety.IkonaAkcent => p.Akcent,
                _ => p.Tekst,
            };
        }
        set { }
    }
}

/// <summary>Przełącznik Wł./Wył. (zamiast pola wyboru).</summary>
internal sealed class FluentPrzelacznik : CheckBox, IFluentKontrolka
{
    private bool _najechany;

    public FluentPrzelacznik()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        Font = Motyw.Czcionka(10);
        AutoSize = false;
        Size = new Size(92, 28);
        Cursor = Cursors.Hand;
    }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _najechany = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _najechany = false; Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var p = Motyw.Biezaca;
        var dpi = DeviceDpi / 96f;
        Grafika.Wygladzanie(g);
        using (var tlo = new SolidBrush(Parent?.BackColor ?? p.Tlo))
            g.FillRectangle(tlo, ClientRectangle);

        float w = 40 * dpi, h = 20 * dpi;
        var r = new RectangleF(Width - w - 2 * dpi, (Height - h) / 2, w, h);
        if (Checked)
        {
            Grafika.Wypelnij(g, r, h / 2, _najechany ? p.AkcentNajechany : p.Akcent);
            var d = 12 * dpi;
            using var b = new SolidBrush(p.TekstNaAkcencie);
            g.FillEllipse(b, r.Right - d - 4 * dpi, r.Y + (h - d) / 2, d, d);
        }
        else
        {
            Grafika.Wypelnij(g, r, h / 2, _najechany ? p.PrzyciskNajechany : p.Karta);
            Grafika.Obrysuj(g, r, h / 2, p.TekstDrugi, dpi);
            var d = 10 * dpi;
            using var b = new SolidBrush(p.TekstDrugi);
            g.FillEllipse(b, r.X + 5 * dpi, r.Y + (h - d) / 2, d, d);
        }
        if (Focused && ShowFocusCues)
            Grafika.Obrysuj(g, RectangleF.Inflate(r, 3 * dpi, 3 * dpi), h / 2 + 3 * dpi, p.Tekst, 1.5f * dpi);

        TextRenderer.DrawText(g, Checked ? "Wł." : "Wył.", Font,
            new Rectangle(0, 0, (int)(r.X - 8 * dpi), Height), Enabled ? p.Tekst : p.TekstWylaczony,
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
    }
}

/// <summary>Pole tekstowe Fluent: zaokrąglona ramka, linia akcentu przy aktywnym polu.</summary>
internal sealed class FluentPole : Panel, IFluentKontrolka
{
    public FluentPole(bool wieleWierszy = false)
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Pole = new TextBox
        {
            BorderStyle = BorderStyle.None,
            Multiline = wieleWierszy,
            AcceptsReturn = wieleWierszy,
            WordWrap = false,
            Font = Motyw.Czcionka(10),
            Dock = DockStyle.Fill,
        };
        Padding = new Padding(10, 7, 10, 6);
        Height = wieleWierszy ? 58 : 34;
        Controls.Add(Pole);
        Pole.GotFocus += (_, _) => Invalidate();
        Pole.LostFocus += (_, _) => Invalidate();
        Cursor = Cursors.IBeam;
        Click += (_, _) => Pole.Focus();
    }

    public TextBox Pole { get; }

    [AllowNull]
    public override string Text { get => Pole.Text; set => Pole.Text = value ?? ""; }

    public override Color BackColor { get => Motyw.Biezaca.PoleTekstowe; set { } }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var g = e.Graphics;
        var p = Motyw.Biezaca;
        var dpi = DeviceDpi / 96f;
        Grafika.Wygladzanie(g);
        using (var tlo = new SolidBrush(Parent?.BackColor ?? p.Tlo))
            g.FillRectangle(tlo, ClientRectangle);
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        Grafika.Wypelnij(g, r, 4 * dpi, p.PoleTekstowe);
        Grafika.Obrysuj(g, r, 4 * dpi, p.Ramka);
        var aktywne = Pole.Focused;
        using var linia = new Pen(aktywne ? p.Akcent : p.RamkaSilna, (aktywne ? 2 : 1) * dpi);
        var y = Height - (aktywne ? 1.5f : 1) * dpi;
        g.DrawLine(linia, 4 * dpi, y, Width - 4 * dpi, y);
    }
}

/// <summary>Przełącznik segmentowy (dwie lub więcej opcji).</summary>
internal sealed class FluentSegment : FluentKontrolka
{
    private int _wybrany;

    public FluentSegment(params string[] opcje)
    {
        Opcje = opcje;
        Font = Motyw.Czcionka(9.5f);
        Height = 30;
        Width = 300;
        TabStop = true;
        Cursor = Cursors.Hand;
    }

    public string[] Opcje { get; }

    public int Wybrany
    {
        get => _wybrany;
        set
        {
            if (_wybrany == value) return;
            _wybrany = value;
            Invalidate();
            ZmianaWyboru?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? ZmianaWyboru;

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        Focus();
        Wybrany = Math.Clamp(e.X * Opcje.Length / Math.Max(1, Width), 0, Opcje.Length - 1);
    }

    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Left or Keys.Right || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Left) Wybrany = Math.Max(0, Wybrany - 1);
        if (e.KeyCode == Keys.Right) Wybrany = Math.Min(Opcje.Length - 1, Wybrany + 1);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Grafika.Wygladzanie(g);
        TloRodzica(g);
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        Grafika.Wypelnij(g, r, r.Height / 2, P.Tlo);
        Grafika.Obrysuj(g, r, r.Height / 2, P.Ramka);
        var w = r.Width / Opcje.Length;
        for (var i = 0; i < Opcje.Length; i++)
        {
            var komorka = new RectangleF(r.X + i * w + 2 * Dpi, r.Y + 2 * Dpi, w - 4 * Dpi, r.Height - 4 * Dpi);
            var wybrana = i == _wybrany;
            if (wybrana)
                Grafika.Wypelnij(g, komorka, komorka.Height / 2, P.Akcent);
            TextRenderer.DrawText(g, Opcje[i], Font, Rectangle.Round(komorka), wybrana ? P.TekstNaAkcencie : P.Tekst,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
        RamkaFokusu(g, r, r.Height / 2);
    }
}

/// <summary>Kafelek certyfikatu do wyboru (jak przycisk opcji): imię i nazwisko, etykieta kwalifikacji, wystawca i ważność.</summary>
internal sealed class KafelekCertyfikatu : FluentKontrolka
{
    private bool _zaznaczony;

    public KafelekCertyfikatu(CertyfikatInfo certyfikat)
    {
        Certyfikat = certyfikat;
        Font = Motyw.Czcionka(10);
        TabStop = false;
        Cursor = Cursors.Hand;
        AccessibleRole = AccessibleRole.RadioButton;
        AccessibleName = $"{certyfikat.ImieNazwisko}, {(certyfikat.Kwalifikowany ? "kwalifikowany" : "niekwalifikowany")}, {certyfikat.Wystawca}";
    }

    public CertyfikatInfo Certyfikat { get; }

    public bool Zaznaczony
    {
        get => _zaznaczony;
        set { _zaznaczony = value; Invalidate(); }
    }

    public bool PokazFokus { get; set; }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Grafika.Wygladzanie(g);
        TloRodzica(g);
        var p = P;
        var r = new RectangleF(1, 1, Width - 2.5f, Height - 2.5f);
        var tlo = _zaznaczony ? p.AkcentTlo : Najechany ? p.KartaNajechana : p.Tlo;
        Grafika.Wypelnij(g, r, 6 * Dpi, tlo);
        Grafika.Obrysuj(g, r, 6 * Dpi, _zaznaczony ? p.Akcent : p.Ramka, _zaznaczony ? 2 * Dpi : 1);
        if (PokazFokus)
            Grafika.Obrysuj(g, RectangleF.Inflate(r, -3 * Dpi, -3 * Dpi), 4 * Dpi, p.Tekst, 1.5f * Dpi);

        // kółko wyboru
        var cx = 26 * Dpi; var cy = Height / 2f; var rk = 9 * Dpi;
        using (var pi = new Pen(_zaznaczony ? p.Akcent : p.TekstDrugi, 1.6f * Dpi))
            g.DrawEllipse(pi, cx - rk, cy - rk, 2 * rk, 2 * rk);
        if (_zaznaczony)
        {
            using var b = new SolidBrush(p.Akcent);
            g.FillEllipse(b, cx - rk / 2, cy - rk / 2, rk, rk);
        }

        var x = (int)(50 * Dpi);
        using var pogrubiona = Motyw.Czcionka(10.5f, FontStyle.Bold);
        using var drobna = Motyw.Czcionka(9.5f);
        using var pigulkaCz = Motyw.Czcionka(8.5f, FontStyle.Bold);
        var c = Certyfikat;
        var y1 = (int)(Height / 2f - 20 * Dpi);
        var nazwa = TextRenderer.MeasureText(g, c.ImieNazwisko, pogrubiona);
        TextRenderer.DrawText(g, c.ImieNazwisko, pogrubiona, new Point(x, y1), p.Tekst, TextFormatFlags.NoPadding);

        var etykieta = c.Kwalifikowany ? "Kwalifikowany" : "Niekwalifikowany";
        var we = TextRenderer.MeasureText(g, etykieta, pigulkaCz, Size.Empty, TextFormatFlags.NoPadding);
        var pig = new RectangleF(x + nazwa.Width + 10 * Dpi, y1 + 1 * Dpi, we.Width + 16 * Dpi, nazwa.Height - 1 * Dpi);
        Grafika.Wypelnij(g, pig, pig.Height / 2, c.Kwalifikowany ? p.SukcesTlo : p.NeutralnyTlo);
        TextRenderer.DrawText(g, etykieta, pigulkaCz, Rectangle.Round(pig), c.Kwalifikowany ? p.Sukces : p.TekstDrugi,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

        var opis = $"{c.Wystawca}  ·  ważny do {c.WaznyDo:dd.MM.yyyy}";
        TextRenderer.DrawText(g, opis, drobna, new Rectangle(x, (int)(Height / 2f + 3 * Dpi), Width - x - (int)(12 * Dpi), (int)(22 * Dpi)),
            p.TekstDrugi, TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
    }
}

/// <summary>Lista kafelków certyfikatów z wyborem jednego (klawiatura: strzałki, Enter).</summary>
internal sealed class ListaCertyfikatow : Panel, IFluentKontrolka
{
    private readonly List<KafelekCertyfikatu> _kafelki = new();
    private readonly FluentEtykieta _pusta;
    private int _wybrany = -1;

    public ListaCertyfikatow()
    {
        SetStyle(ControlStyles.Selectable, true);
        TabStop = true;
        AutoScroll = true;
        DoubleBuffered = true;
        _pusta = new FluentEtykieta("", StylEtykiety.Drugi) { AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Visible = false };
        Controls.Add(_pusta);
    }

    public override Color BackColor { get => Parent?.BackColor ?? Motyw.Biezaca.Karta; set { } }

    public CertyfikatInfo? Wybrany => _wybrany >= 0 && _wybrany < _kafelki.Count ? _kafelki[_wybrany].Certyfikat : null;

    public event EventHandler? ZmianaWyboru;
    public event EventHandler? Zatwierdzono;

    public void Ustaw(IReadOnlyList<CertyfikatInfo> certyfikaty, string? odciskDoZaznaczenia, string komunikatGdyPusta)
    {
        SuspendLayout();
        foreach (var k in _kafelki)
        {
            Controls.Remove(k);
            k.Dispose();
        }
        _kafelki.Clear();
        foreach (var c in certyfikaty)
        {
            var k = new KafelekCertyfikatu(c);
            k.Click += (_, _) => { Focus(); Wybierz(_kafelki.IndexOf(k)); };
            k.DoubleClick += (_, _) => Zatwierdzono?.Invoke(this, EventArgs.Empty);
            _kafelki.Add(k);
            Controls.Add(k);
        }
        _pusta.Text = komunikatGdyPusta;
        _pusta.Visible = _kafelki.Count == 0;
        _wybrany = -1;
        var indeks = _kafelki.FindIndex(k => string.Equals(k.Certyfikat.Odcisk, odciskDoZaznaczenia, StringComparison.OrdinalIgnoreCase));
        Uloz();
        ResumeLayout();
        Wybierz(indeks >= 0 ? indeks : _kafelki.Count > 0 ? 0 : -1);
    }

    private void Wybierz(int indeks)
    {
        if (indeks == _wybrany) return;
        _wybrany = indeks;
        for (var i = 0; i < _kafelki.Count; i++)
            _kafelki[i].Zaznaczony = i == indeks;
        if (indeks >= 0)
            ScrollControlIntoView(_kafelki[indeks]);
        ZmianaWyboru?.Invoke(this, EventArgs.Empty);
    }

    private void Uloz()
    {
        var dpi = DeviceDpi / 96f;
        var y = AutoScrollPosition.Y;
        var h = (int)(64 * dpi);
        var odstep = (int)(8 * dpi);
        var w = ClientSize.Width;
        foreach (var k in _kafelki)
        {
            k.SetBounds(0, y, w, h);
            y += h + odstep;
        }
    }

    protected override void OnResize(EventArgs eventargs)
    {
        base.OnResize(eventargs);
        Uloz();
    }

    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Up or Keys.Down || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_kafelki.Count == 0) return;
        if (e.KeyCode == Keys.Down) Wybierz(Math.Min(_kafelki.Count - 1, _wybrany + 1));
        if (e.KeyCode == Keys.Up) Wybierz(Math.Max(0, _wybrany - 1));
    }

    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); OdswiezFokus(true); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); OdswiezFokus(false); }

    private void OdswiezFokus(bool fokus)
    {
        for (var i = 0; i < _kafelki.Count; i++)
        {
            _kafelki[i].PokazFokus = fokus && i == _wybrany && ShowFocusCues;
            _kafelki[i].Invalidate();
        }
    }
}

/// <summary>Lista rozwijana w kolorach motywu.</summary>
internal sealed class FluentLista : ComboBox, IFluentKontrolka
{
    public FluentLista()
    {
        DropDownStyle = ComboBoxStyle.DropDownList;
        FlatStyle = FlatStyle.Flat;
        DrawMode = DrawMode.OwnerDrawFixed;
        Font = Motyw.Czcionka(10);
        ItemHeight = 24;
    }

    public override Color BackColor { get => Motyw.Biezaca.PoleTekstowe; set { } }
    public override Color ForeColor { get => Motyw.Biezaca.Tekst; set { } }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        var p = Motyw.Biezaca;
        var zaznaczony = (e.State & DrawItemState.Selected) != 0 && (e.State & DrawItemState.ComboBoxEdit) == 0;
        using (var tlo = new SolidBrush(zaznaczony ? p.AkcentTlo : p.PoleTekstowe))
            e.Graphics.FillRectangle(tlo, e.Bounds);
        if (e.Index >= 0)
            TextRenderer.DrawText(e.Graphics, GetItemText(Items[e.Index]), Font, Rectangle.Inflate(e.Bounds, -6, 0), p.Tekst,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
    }
}

/// <summary>Pozycja nawigacji po lewej (jak w Ustawieniach Windows 11).</summary>
internal sealed class ElementNawigacji : FluentKontrolka
{
    private bool _wybrany;

    public ElementNawigacji(string tekst, string ikona)
    {
        Text = tekst;
        Ikona = ikona;
        Font = Motyw.Czcionka(10.5f);
        Height = 40;
        Cursor = Cursors.Hand;
        TabStop = true;
        AccessibleRole = AccessibleRole.PageTab;
        AccessibleName = tekst;
    }

    public string Ikona { get; }

    public bool Wybrany
    {
        get => _wybrany;
        set { _wybrany = value; Invalidate(); }
    }

    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Enter or Keys.Space || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode is Keys.Enter or Keys.Space)
            OnClick(EventArgs.Empty);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Grafika.Wygladzanie(g);
        TloRodzica(g);
        var r = new RectangleF(2 * Dpi, 2 * Dpi, Width - 4 * Dpi, Height - 4 * Dpi);
        if (_wybrany || Najechany)
            Grafika.Wypelnij(g, r, 5 * Dpi, _wybrany ? P.Karta : P.KartaNajechana);
        if (_wybrany)
            Grafika.Wypelnij(g, new RectangleF(r.X, r.Y + r.Height / 2 - 8 * Dpi, 3 * Dpi, 16 * Dpi), 1.5f * Dpi, P.Akcent);
        RamkaFokusu(g, RectangleF.Inflate(r, -1.5f * Dpi, -1.5f * Dpi), 5 * Dpi);
        using var fi = Motyw.Ikony(11);
        TextRenderer.DrawText(g, Ikona, fi, new Rectangle((int)(16 * Dpi), 0, (int)(24 * Dpi), Height), P.Tekst,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding);
        TextRenderer.DrawText(g, Text, Font, new Rectangle((int)(48 * Dpi), 0, Width - (int)(52 * Dpi), Height), P.Tekst,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
    }
}
