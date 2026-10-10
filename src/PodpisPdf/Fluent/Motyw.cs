using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace PodpisPdf.Fluent;

/// <summary>Kolory interfejsu (Fluent 2) dla jasnego lub ciemnego motywu.</summary>
internal sealed record Paleta(
    bool Ciemna,
    Color Tlo,              // tło okna
    Color TloWarstwy,       // tło paneli bocznych / paska poleceń
    Color Karta,
    Color KartaNajechana,
    Color Ramka,
    Color RamkaSilna,
    Color Tekst,
    Color TekstDrugi,
    Color TekstWylaczony,
    Color Akcent,
    Color AkcentNajechany,
    Color AkcentWcisniety,
    Color TekstNaAkcencie,
    Color AkcentTlo,        // delikatne tło zaznaczenia / paska informacyjnego
    Color Przycisk,
    Color PrzyciskNajechany,
    Color PrzyciskWcisniety,
    Color PoleTekstowe,
    Color SukcesTlo,
    Color Sukces,
    Color NeutralnyTlo,
    Color Plotno)           // tło pod stronami w podglądzie
{
    public static readonly Paleta Jasny = new(false,
        Tlo: Color.FromArgb(243, 243, 243), TloWarstwy: Color.FromArgb(238, 238, 238),
        Karta: Color.FromArgb(251, 251, 251), KartaNajechana: Color.FromArgb(246, 246, 246),
        Ramka: Color.FromArgb(229, 229, 229), RamkaSilna: Color.FromArgb(205, 205, 205),
        Tekst: Color.FromArgb(26, 26, 26), TekstDrugi: Color.FromArgb(96, 96, 96), TekstWylaczony: Color.FromArgb(160, 160, 160),
        Akcent: Color.FromArgb(15, 108, 189), AkcentNajechany: Color.FromArgb(17, 94, 163), AkcentWcisniety: Color.FromArgb(15, 84, 140),
        TekstNaAkcencie: Color.White, AkcentTlo: Color.FromArgb(235, 243, 252),
        Przycisk: Color.FromArgb(253, 253, 253), PrzyciskNajechany: Color.FromArgb(245, 245, 245), PrzyciskWcisniety: Color.FromArgb(235, 235, 235),
        PoleTekstowe: Color.White,
        SukcesTlo: Color.FromArgb(223, 246, 221), Sukces: Color.FromArgb(16, 124, 16),
        NeutralnyTlo: Color.FromArgb(235, 235, 235), Plotno: Color.FromArgb(230, 230, 230));

    public static readonly Paleta Ciemny = new(true,
        Tlo: Color.FromArgb(32, 32, 32), TloWarstwy: Color.FromArgb(39, 39, 39),
        Karta: Color.FromArgb(43, 43, 43), KartaNajechana: Color.FromArgb(50, 50, 50),
        Ramka: Color.FromArgb(58, 58, 58), RamkaSilna: Color.FromArgb(80, 80, 80),
        Tekst: Color.White, TekstDrugi: Color.FromArgb(197, 197, 197), TekstWylaczony: Color.FromArgb(120, 120, 120),
        Akcent: Color.FromArgb(76, 194, 255), AkcentNajechany: Color.FromArgb(71, 177, 232), AkcentWcisniety: Color.FromArgb(66, 160, 210),
        TekstNaAkcencie: Color.Black, AkcentTlo: Color.FromArgb(36, 48, 61),
        Przycisk: Color.FromArgb(55, 55, 55), PrzyciskNajechany: Color.FromArgb(62, 62, 62), PrzyciskWcisniety: Color.FromArgb(48, 48, 48),
        PoleTekstowe: Color.FromArgb(45, 45, 45),
        SukcesTlo: Color.FromArgb(57, 61, 27), Sukces: Color.FromArgb(108, 203, 95),
        NeutralnyTlo: Color.FromArgb(60, 60, 60), Plotno: Color.FromArgb(28, 28, 28));
}

/// <summary>
/// Motyw programu: automatycznie jasny lub ciemny, zgodnie z ustawieniem Windows
/// („Wybierz tryb aplikacji”), ze zmianą na żywo.
/// </summary>
internal static class Motyw
{
    private static bool? _wymuszonyCiemny;

    static Motyw()
    {
        Biezaca = Wykryj() ? Paleta.Ciemny : Paleta.Jasny;
        try
        {
            SystemEvents.UserPreferenceChanged += (_, e) =>
            {
                if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color or UserPreferenceCategory.VisualStyle)
                    Odswiez();
            };
        }
        catch (Exception)
        {
            // Bez powiadomień motyw zmieni się dopiero po ponownym uruchomieniu.
        }
    }

    public static Paleta Biezaca { get; private set; }

    public static event Action? Zmieniono;

    /// <summary>Wymusza motyw (zrzuty ekranu w testach); null — zgodnie z Windows.</summary>
    public static void Wymus(bool? ciemny)
    {
        _wymuszonyCiemny = ciemny;
        Odswiez();
    }

    private static void Odswiez()
    {
        var nowa = (_wymuszonyCiemny ?? Wykryj()) ? Paleta.Ciemny : Paleta.Jasny;
        if (nowa == Biezaca)
            return;
        Biezaca = nowa;
        Zmieniono?.Invoke();
    }

    /// <summary>Czy w Windows wybrano ciemny tryb aplikacji.</summary>
    private static bool Wykryj()
    {
        try
        {
            using var klucz = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return klucz?.GetValue("AppsUseLightTheme") is int wartosc && wartosc == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    // ---- Czcionki ----

    private static readonly string RodzinaTekstu = Dostepna("Segoe UI Variable Text", "Segoe UI");
    private static readonly string RodzinaNaglowka = Dostepna("Segoe UI Variable Display", "Segoe UI Semibold", "Segoe UI");
    private static readonly string RodzinaIkon = Dostepna("Segoe Fluent Icons", "Segoe MDL2 Assets");

    public static Font Czcionka(float punkty, FontStyle styl = FontStyle.Regular) => new(RodzinaTekstu, punkty, styl);
    public static Font Naglowek(float punkty) => new(RodzinaNaglowka, punkty, RodzinaNaglowka == "Segoe UI Semibold" ? FontStyle.Regular : FontStyle.Bold);
    public static Font Ikony(float punkty) => new(RodzinaIkon, punkty);

    private static string Dostepna(params string[] rodziny)
    {
        using var zainstalowane = new System.Drawing.Text.InstalledFontCollection();
        var nazwy = zainstalowane.Families.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return rodziny.FirstOrDefault(nazwy.Contains) ?? rodziny[^1];
    }

    // ---- Okna ----

    /// <summary>
    /// Wygląd okna: kolory, ciemny pasek tytułu, kolor paska tytułu jak tło okna, zaokrąglone narożniki
    /// (Windows 11) oraz ciemne paski przewijania i pola standardowych kontrolek.
    /// </summary>
    public static void Zastosuj(Form okno)
    {
        var p = Biezaca;
        okno.BackColor = p.Tlo;
        okno.ForeColor = p.Tekst;
        if (okno.IsHandleCreated)
            UstawRamke(okno.Handle, p);
        else
            okno.HandleCreated += (_, _) => UstawRamke(okno.Handle, Biezaca);
        ZastosujDoKontrolek(okno.Controls, p);
        okno.Invalidate(true);
    }

    private static void ZastosujDoKontrolek(Control.ControlCollection kontrolki, Paleta p)
    {
        foreach (Control k in kontrolki)
        {
            switch (k)
            {
                case IFluentKontrolka:
                    k.Invalidate();
                    break;
                case TextBox or NumericUpDown or ComboBox:
                    k.BackColor = p.PoleTekstowe;
                    k.ForeColor = p.Tekst;
                    MotywSystemowy(k, p);
                    break;
                case Label l:
                    l.ForeColor = l.Tag as string == "drugi" ? p.TekstDrugi : p.Tekst;
                    break;
                case ScrollableControl s when s.AutoScroll:
                    MotywSystemowy(k, p);
                    break;
            }
            if (k.HasChildren)
                ZastosujDoKontrolek(k.Controls, p);
        }
    }

    /// <summary>Ciemne paski przewijania i listy rozwijane standardowych kontrolek Windows.</summary>
    private static void MotywSystemowy(Control k, Paleta p)
    {
        void Ustaw() => SetWindowTheme(k.Handle, p.Ciemna ? "DarkMode_Explorer" : "Explorer", null);
        if (k.IsHandleCreated)
            Ustaw();
        else
            k.HandleCreated += (_, _) => SetWindowTheme(k.Handle, Biezaca.Ciemna ? "DarkMode_Explorer" : "Explorer", null);
    }

    private static void UstawRamke(IntPtr okno, Paleta p)
    {
        const int DwmwaUseImmersiveDarkMode = 20, DwmwaWindowCornerPreference = 33, DwmwaBorderColor = 34, DwmwaCaptionColor = 35, DwmwaTextColor = 36;
        try
        {
            var ciemny = p.Ciemna ? 1 : 0;
            DwmSetWindowAttribute(okno, DwmwaUseImmersiveDarkMode, ref ciemny, sizeof(int));
            var narozniki = 2; // DWMWCP_ROUND
            DwmSetWindowAttribute(okno, DwmwaWindowCornerPreference, ref narozniki, sizeof(int));
            var tlo = ColorTranslator.ToWin32(p.Tlo);
            DwmSetWindowAttribute(okno, DwmwaCaptionColor, ref tlo, sizeof(int));
            var ramka = ColorTranslator.ToWin32(p.Ciemna ? Color.FromArgb(60, 60, 60) : Color.FromArgb(210, 210, 210));
            DwmSetWindowAttribute(okno, DwmwaBorderColor, ref ramka, sizeof(int));
            var tekst = ColorTranslator.ToWin32(p.Tekst);
            DwmSetWindowAttribute(okno, DwmwaTextColor, ref tekst, sizeof(int));
        }
        catch (Exception)
        {
            // Starsze Windows nie znają tych atrybutów — okno zostaje w wyglądzie systemowym.
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int atrybut, ref int wartosc, int rozmiar);

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hwnd, string? nazwa, string? lista);
}

/// <summary>Kontrolka rysowana samodzielnie według bieżącego motywu.</summary>
internal interface IFluentKontrolka;

internal static class Grafika
{
    public static GraphicsPath Zaokraglony(RectangleF r, float promien)
    {
        var sciezka = new GraphicsPath();
        var d = Math.Min(promien * 2, Math.Min(r.Width, r.Height));
        if (d <= 0.5f)
        {
            sciezka.AddRectangle(r);
            return sciezka;
        }
        sciezka.AddArc(r.X, r.Y, d, d, 180, 90);
        sciezka.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        sciezka.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        sciezka.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        sciezka.CloseFigure();
        return sciezka;
    }

    public static void Wypelnij(Graphics g, RectangleF r, float promien, Color kolor)
    {
        using var s = Zaokraglony(r, promien);
        using var b = new SolidBrush(kolor);
        g.FillPath(b, s);
    }

    public static void Obrysuj(Graphics g, RectangleF r, float promien, Color kolor, float grubosc = 1)
    {
        using var s = Zaokraglony(r, promien);
        using var pi = new Pen(kolor, grubosc);
        g.DrawPath(pi, s);
    }

    public static void Wygladzanie(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
    }
}
