using Avalonia;
using Avalonia.Controls;

namespace PodpiszPdf.Mac;

/// <summary>Jedno okno programu: strona główna, podgląd (wskazanie miejsca podpisu) i ustawienia.</summary>
public sealed partial class OknoGlowne : Window
{
    private PixelRect? _rozmiarPrzedPodgladem;

    public OknoGlowne() : this(new Ustawienia())
    {
    }

    internal OknoGlowne(Ustawienia ustawienia, bool trybTestowy = false)
    {
        Ustawienia = ustawienia;
        TrybTestowy = trybTestowy;
        InitializeComponent();
        StronaGlowna = new StronaGlowna(this);
        Tresc.Content = StronaGlowna;
        Opened += (_, _) =>
        {
            if (!TrybTestowy)
                StronaGlowna.Inicjalizuj();
        };
        Closing += (_, e) =>
        {
            // Przerwanie podpisu w trakcie mogłoby zostawić kartę zalogowaną i plik tymczasowy.
            if (StronaGlowna.EtapKrytyczny)
                e.Cancel = true;
        };
    }

    internal Ustawienia Ustawienia { get; set; }

    /// <summary>Zrzuty ekranu: przykładowe dane, bez zapisu ustawień i bez sieci.</summary>
    internal bool TrybTestowy { get; }

    internal StronaGlowna StronaGlowna { get; }

    internal void PokazStroneGlowna()
    {
        Tresc.Content = StronaGlowna;
        Title = "PodpiszPdf";
        PrzywrocRozmiarPoPodgladzie();
        StronaGlowna.PoPowrocie();
    }

    internal void PokazPodglad(StronaPodgladu strona, string plik)
    {
        Title = $"Wskaż miejsce podpisu — {Path.GetFileName(plik)}";
        PowiekszDoPodgladu();
        Tresc.Content = strona;
    }

    internal void OtworzUstawienia(int sekcja = 0)
    {
        if (!ReferenceEquals(Tresc.Content, StronaGlowna) || StronaGlowna.Zajeta)
            return;
        Title = "Ustawienia — PodpiszPdf";
        Tresc.Content = new StronaUstawien(this, Ustawienia.Kopia(), sekcja);
    }

    internal void OtworzPlik(string sciezka)
    {
        Activate();
        StronaGlowna.OtworzZFindera(sciezka);
    }

    internal void ZapiszUstawienia()
    {
        if (!TrybTestowy)
            Ustawienia.Zapisz();
    }

    private void PowiekszDoPodgladu()
    {
        if (TrybTestowy || Screens.ScreenFromWindow(this) is not { } ekran)
            return;
        _rozmiarPrzedPodgladem = new PixelRect(Position, PixelSize.FromSize(ClientSize, RenderScaling));
        var obszar = ekran.WorkingArea;
        var szerokosc = (int)(obszar.Width * 0.8);
        var wysokosc = (int)(obszar.Height * 0.9);
        if (szerokosc <= ClientSize.Width * RenderScaling && wysokosc <= ClientSize.Height * RenderScaling)
            return;
        Width = Math.Max(Width, szerokosc / RenderScaling);
        Height = Math.Max(Height, wysokosc / RenderScaling);
        Position = new PixelPoint(obszar.X + (obszar.Width - (int)(Width * RenderScaling)) / 2, obszar.Y + (obszar.Height - (int)(Height * RenderScaling)) / 2);
    }

    private void PrzywrocRozmiarPoPodgladzie()
    {
        if (_rozmiarPrzedPodgladem is not { } r || WindowState != WindowState.Normal)
            return;
        _rozmiarPrzedPodgladem = null;
        Width = r.Width / RenderScaling;
        Height = r.Height / RenderScaling;
        Position = r.Position;
    }

    private void PrzyOtwarciu(object? sender, EventArgs e) => StronaGlowna.WybierzPlikZMenu();

    private void PrzyPodpisie(object? sender, EventArgs e) => StronaGlowna.PodpiszZMenu();
}
