using System.ComponentModel;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using PodpiszPdf.Core;

namespace PodpiszPdf.Mac;

/// <summary>Dane do wskazania miejsca podpisu i wynik (miejsce albo null po anulowaniu).</summary>
internal sealed class ZadaniePodgladu
{
    // Rozdzielczość podglądu wizualizacji (piksele na punkt przy skali 1) — wystarcza także dla największej skali.
    private const int SkalaPodgladuWizualizacji = 8;

    private ZadaniePodgladu(string plik, byte[] pdf, IReadOnlyList<StronaPdf> strony, ObrazStrony wizualizacja,
        RozmiarWizualizacji rozmiar, PolozenieWizualizacji polozenie, float skala)
    {
        Plik = plik;
        Pdf = pdf;
        Strony = strony;
        Wizualizacja = wizualizacja;
        Rozmiar = rozmiar;
        Polozenie = polozenie;
        Skala = skala;
    }

    public string Plik { get; }
    public byte[] Pdf { get; }
    public IReadOnlyList<StronaPdf> Strony { get; }
    public ObrazStrony Wizualizacja { get; }
    /// <summary>Rozmiar wizualizacji przy skali 1 (wynika z treści).</summary>
    public RozmiarWizualizacji Rozmiar { get; }
    public PolozenieWizualizacji Polozenie { get; }
    public float Skala { get; }

    public TaskCompletionSource<MiejscePodpisu?> Wynik { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Wczytanie dokumentu i wizualizacji (w tle, przed pokazaniem podglądu).</summary>
    public static ZadaniePodgladu Przygotuj(string plik, DaneWizualizacji dane, PolozenieWizualizacji polozenie, float skala)
    {
        var pdf = File.ReadAllBytes(plik);
        var strony = DokumentPdf.Strony(plik);
        if (strony.Count == 0)
            throw new BladPodpisu("Dokument PDF nie ma żadnej strony.");
        var wizualizacja = DokumentPdf.Renderuj(Core.Wizualizacja.PodgladPdf(dane), 0, SkalaPodgladuWizualizacji, bialeTlo: false);
        return new ZadaniePodgladu(plik, pdf, strony, wizualizacja, Core.Wizualizacja.Rozmiar(dane), polozenie, skala);
    }
}

/// <summary>Miniatura strony na pasku po lewej.</summary>
public sealed class MiniaturaStrony(int numer, double szerokosc, double wysokosc) : INotifyPropertyChanged
{
    private Bitmap? _obraz;

    public int Numer { get; } = numer;
    public double Szerokosc { get; } = szerokosc;
    public double Wysokosc { get; } = wysokosc;

    public Bitmap? Obraz
    {
        get => _obraz;
        set
        {
            _obraz = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Obraz)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>
/// Podgląd wszystkich stron dokumentu. Kliknięcie umieszcza wizualizację podpisu, przeciągnięcie po stronie
/// rysuje ją w wybranym rozmiarze, a przeciąganie podpisu i 8 uchwytów na ramce pozwalają go przesunąć,
/// powiększyć i pomniejszyć. Proporcje są zawsze stałe (wynikają z treści wizualizacji).
/// </summary>
public sealed partial class StronaPodgladu : UserControl
{
    private const double OdstepStron = 16;
    private const double MarginesBoczny = 24;
    // Największe powiększenie przy dopasowaniu do szerokości (punkty ekranu na punkt PDF).
    private const double MaksSkalaDopasowania = 2;
    // Przesunięcie wskaźnika, od którego kliknięcie staje się rysowaniem.
    private const double ProgPrzeciagania = 5;
    // Połowa obszaru chwytania uchwytu; widoczny uchwyt ma 10 × 10.
    private const double PolowaUchwytu = 8;
    private const double SzerokoscMiniatury = 104;
    // 100% = rzeczywisty rozmiar strony: w macOS punkt ekranu odpowiada punktowi PDF (72 na cal).
    private const double SkalaRzeczywista = 1;
    private static readonly int[] Powiekszenia = { 50, 67, 75, 100, 125, 150, 200, 300 };

    private readonly OknoGlowne _okno;
    private readonly ZadaniePodgladu _zadanie;
    private readonly IReadOnlyList<StronaPdf> _strony;
    private readonly List<Border> _ramkiStron = new();
    private readonly List<Image> _obrazyStron = new();
    private readonly List<Rect> _uklad = new();
    private readonly Dictionary<int, double> _skalaObrazu = new();
    private List<MiniaturaStrony> _miniatury = new();
    private Nakladka _nakladkaMiejsca = null!;
    private Nakladka _nakladkaRysowana = null!;
    private Nakladka _nakladkaPodWskaznikiem = null!;
    private readonly Rectangle[] _uchwyty = new Rectangle[8];
    private readonly Border _etykieta = new();
    private readonly TextBlock _tekstEtykiety = new();

    private double _skala = 1;
    private bool _dopasuj = true;
    private int _powiekszenie = 100;
    private bool _renderowanie;
    private bool _ponownie;
    private bool _ustawianieMiniatury;
    private bool _zakonczono;
    private bool _pierwszyUklad = true;
    private double _gest;

    private MiejscePodpisu? _miejsce;
    private MiejscePodpisu? _podWskaznikiem;
    // Skala podpisu przy kliknięciu: na start 1 (imię i nazwisko 11 pt), potem ostatnio narysowany rozmiar.
    private float _skalaPodpisu;
    private Przeciaganie? _przeciaganie;
    private MiejscePodpisu? _rysowane;
    private bool _pokazPodpowiedz = true;
    private StandardCursorType? _kursor;

    private enum RodzajPrzeciagania { Rysowanie, Przesuwanie, KrawedzPozioma, KrawedzPionowa }

    /// <summary>Rozpoczęte przeciąganie: strona, punkt zaczepienia (PDF) i miejsce wciśnięcia (ekran).</summary>
    private sealed record Przeciaganie(RodzajPrzeciagania Rodzaj, int Indeks, float X, float Y, Point Start);

    public StronaPodgladu() : this(new OknoGlowne(), null!)
    {
    }

    internal StronaPodgladu(OknoGlowne okno, ZadaniePodgladu zadanie)
    {
        _okno = okno;
        _zadanie = zadanie;
        InitializeComponent();
        if (zadanie == null)
        {
            _strony = Array.Empty<StronaPdf>();
            return;
        }
        _strony = zadanie.Strony;
        _skalaPodpisu = Wizualizacja.ZaokraglijSkale(zadanie.Skala);
        Plotno.PointerPressed += PrzyWcisnieciu;
        Plotno.PointerMoved += PrzyRuchu;
        Plotno.PointerReleased += PrzyZwolnieniu;
        Plotno.PointerCaptureLost += (_, _) => PrzerwijPrzeciaganie();
        Plotno.PointerExited += (_, _) => { if (_przeciaganie == null) UstawPodWskaznikiem(null); };
        Przewijanie.AddHandler(PointerWheelChangedEvent, PrzyKolkuMyszy, RoutingStrategies.Tunnel);
        Plotno.AddHandler(Gestures.PointerTouchPadGestureMagnifyEvent, PrzyGescie);
        KeyDown += PrzyKlawiszu;
        Utworz();
        AttachedToVisualTree += (_, _) => Dispatcher.UIThread.Post(() => Focus(), DispatcherPriority.Background);
    }

    private void Utworz()
    {
        foreach (var _ in _strony)
        {
            var obraz = new Image { Stretch = Stretch.Fill };
            var ramka = new Border
            {
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x30, 0, 0, 0)),
                BorderThickness = new Thickness(1),
                Child = obraz,
                BoxShadow = new BoxShadows(new BoxShadow { Blur = 8, OffsetY = 2, Color = Color.FromArgb(0x30, 0, 0, 0) }),
            };
            _obrazyStron.Add(obraz);
            _ramkiStron.Add(ramka);
            Plotno.Children.Add(ramka);
        }

        var akcent = Application.Current?.TryFindResource("SystemAccentColor", ActualThemeVariant, out var kolor) == true && kolor is Color c
            ? c
            : Color.FromRgb(0, 0x7A, 0xFF);
        var pedzelAkcentu = new SolidColorBrush(akcent);
        var wizualizacja = Obrazy.NaObraz(_zadanie.Wizualizacja);
        var tlo = new SolidColorBrush(Color.FromArgb(18, akcent.R, akcent.G, akcent.B));
        _nakladkaMiejsca = new Nakladka(wizualizacja, pedzelAkcentu, tlo, przerywana: false, grubosc: 2);
        _nakladkaRysowana = new Nakladka(wizualizacja, pedzelAkcentu, tlo, przerywana: true, grubosc: 2);
        _nakladkaPodWskaznikiem = new Nakladka(wizualizacja, pedzelAkcentu, tlo, przerywana: true, grubosc: 1);
        Plotno.Children.Add(_nakladkaMiejsca.Element);
        Plotno.Children.Add(_nakladkaPodWskaznikiem.Element);
        Plotno.Children.Add(_nakladkaRysowana.Element);
        for (var i = 0; i < _uchwyty.Length; i++)
        {
            _uchwyty[i] = new Rectangle
            {
                Width = 10, Height = 10, RadiusX = 2, RadiusY = 2,
                Fill = Brushes.White, Stroke = pedzelAkcentu, StrokeThickness = 2,
                IsHitTestVisible = false, IsVisible = false,
            };
            Plotno.Children.Add(_uchwyty[i]);
        }
        _tekstEtykiety.Foreground = Brushes.White;
        _tekstEtykiety.FontWeight = FontWeight.SemiBold;
        _tekstEtykiety.FontSize = 12;
        _etykieta.Child = _tekstEtykiety;
        _etykieta.Background = pedzelAkcentu;
        _etykieta.CornerRadius = new CornerRadius(12);
        _etykieta.Padding = new Thickness(10, 3, 10, 4);
        _etykieta.IsHitTestVisible = false;
        _etykieta.IsVisible = false;
        Plotno.Children.Add(_etykieta);

        _miniatury = _strony.Select((s, i) =>
        {
            var r = RozmiarWidoczny(s);
            return new MiniaturaStrony(i + 1, SzerokoscMiniatury, SzerokoscMiniatury * r.Height / r.Width);
        }).ToList();
        Miniatury.ItemsSource = _miniatury;

        _miejsce = MiejsceDomyslne(_zadanie.Polozenie);
        OdswiezPodpowiedz();
        RenderujMiniatury();
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
            var r = Wizualizacja.ObliczProstokat(strona.Prostokat, p, Rozmiar, _skalaPodpisu);
            return new MiejscePodpisu(numer, r.GetX(), r.GetY(), _skalaPodpisu);
        }
        catch (BladPodpisu)
        {
            // Położenie z ustawień nie mieści się na tej stronie — użytkownik wskaże je kliknięciem.
            var (x, y) = Wizualizacja.DopasujDoStrony(strona.Prostokat, strona.Prawo, strona.Dol, Rozmiar, _skalaPodpisu);
            return new MiejscePodpisu(numer, x, y, _skalaPodpisu);
        }
    }

    // ---- Zakończenie ----

    private void PrzyAnulowaniu(object? sender, RoutedEventArgs e) => Zakoncz(null);

    private void PrzyPodpisie(object? sender, RoutedEventArgs e)
    {
        if (_miejsce != null)
            Zakoncz(_miejsce);
    }

    private void PrzyKlawiszu(object? sender, KeyEventArgs e)
    {
        var cmd = e.KeyModifiers.HasFlag(KeyModifiers.Meta) || e.KeyModifiers.HasFlag(KeyModifiers.Control);
        switch (e.Key)
        {
            case Key.Escape:
                Zakoncz(null);
                break;
            case Key.Enter when _miejsce != null:
                Zakoncz(_miejsce);
                break;
            case Key.OemPlus or Key.Add when cmd:
                ZmienPowiekszenie(+1);
                break;
            case Key.OemMinus or Key.Subtract when cmd:
                ZmienPowiekszenie(-1);
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    private void Zakoncz(MiejscePodpisu? wynik)
    {
        if (_zakonczono)
            return;
        _zakonczono = true;
        _okno.PokazStroneGlowna();
        _zadanie.Wynik.TrySetResult(wynik);
    }

    // ---- Układ stron i powiększenie ----

    private static Size RozmiarWidoczny(StronaPdf s) =>
        s.Obrot % 180 == 0 ? new Size(s.Szerokosc, s.Wysokosc) : new Size(s.Wysokosc, s.Szerokosc);

    private void PrzyPomniejszeniu(object? sender, RoutedEventArgs e) => ZmienPowiekszenie(-1);

    private void PrzyPowiekszeniu(object? sender, RoutedEventArgs e) => ZmienPowiekszenie(+1);

    private void PrzyDopasowaniu(object? sender, RoutedEventArgs e)
    {
        _dopasuj = true;
        UlozStrony();
    }

    private void PrzyZmianieRozmiaru(object? sender, SizeChangedEventArgs e)
    {
        UlozStrony();
        if (_pierwszyUklad && _uklad.Count > 0)
        {
            _pierwszyUklad = false;
            Dispatcher.UIThread.Post(PrzewinDoMiejsca, DispatcherPriority.Background);
        }
    }

    private void PrzyKolkuMyszy(object? sender, PointerWheelEventArgs e)
    {
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Meta) && !e.KeyModifiers.HasFlag(KeyModifiers.Control))
            return;
        ZmienPowiekszenie(e.Delta.Y > 0 ? +1 : -1);
        e.Handled = true;
    }

    /// <summary>Gest szczypania na gładziku.</summary>
    private void PrzyGescie(object? sender, PointerDeltaEventArgs e)
    {
        _gest += e.Delta.X;
        if (Math.Abs(_gest) < 0.15)
            return;
        ZmienPowiekszenie(_gest > 0 ? +1 : -1);
        _gest = 0;
        e.Handled = true;
    }

    private void ZmienPowiekszenie(int kierunek)
    {
        var biezace = (int)Math.Round(_skala / SkalaRzeczywista * 100);
        var nowe = kierunek > 0
            ? Powiekszenia.FirstOrDefault(p => p > biezace + 1, Powiekszenia[^1])
            : Powiekszenia.LastOrDefault(p => p < biezace - 1, Powiekszenia[0]);
        _dopasuj = false;
        _powiekszenie = nowe;
        var srodek = Przewijanie.Offset.Y + Przewijanie.Viewport.Height / 2;
        var wzgledny = Plotno.Height > 0 ? srodek / Plotno.Height : 0;
        UlozStrony();
        Przewijanie.Offset = new Vector(Przewijanie.Offset.X, Math.Max(0, wzgledny * Plotno.Height - Przewijanie.Viewport.Height / 2));
    }

    private void UlozStrony()
    {
        if (_strony.Count == 0 || Przewijanie.Bounds.Width <= 0)
            return;
        var szerokoscWidoku = Przewijanie.Bounds.Width;
        var szerokoscPola = Math.Max(200, szerokoscWidoku - 2 * MarginesBoczny);
        var maksSzerokosc = _strony.Max(s => RozmiarWidoczny(s).Width);
        _skala = _dopasuj
            ? Math.Min(MaksSkalaDopasowania, szerokoscPola / maksSzerokosc)
            : _powiekszenie / 100.0 * SkalaRzeczywista;
        Procent.Text = $"{Math.Round(_skala / SkalaRzeczywista * 100)}%";

        _uklad.Clear();
        var y = OdstepStron;
        for (var i = 0; i < _strony.Count; i++)
        {
            var r = RozmiarWidoczny(_strony[i]);
            var w = Math.Round(r.Width * _skala);
            var h = Math.Round(r.Height * _skala);
            var prostokat = new Rect(Math.Max(MarginesBoczny, Math.Round((szerokoscWidoku - w) / 2)), y, w, h);
            _uklad.Add(prostokat);
            var ramka = _ramkiStron[i];
            Canvas.SetLeft(ramka, prostokat.X);
            Canvas.SetTop(ramka, prostokat.Y);
            ramka.Width = w;
            ramka.Height = h;
            y += h + OdstepStron;
        }
        Plotno.Width = Math.Max(szerokoscWidoku, _uklad.Max(r => r.Right) + MarginesBoczny);
        Plotno.Height = y;
        OdswiezNakladki();
        OdswiezInformacje();
        RenderujWidoczne();
    }

    private void PrzewinDoMiejsca()
    {
        if (_miejsce == null || _uklad.Count == 0)
            return;
        var r = ProstokatNaEkranie(_miejsce);
        Przewijanie.Offset = new Vector(0, Math.Max(0, r.Top - Przewijanie.Viewport.Height / 2));
    }

    private void PrzewinDoStrony(int indeks)
    {
        if (indeks < 0 || indeks >= _uklad.Count)
            return;
        Przewijanie.Offset = new Vector(Przewijanie.Offset.X, Math.Max(0, _uklad[indeks].Top - OdstepStron));
    }

    private int BiezacaStrona
    {
        get
        {
            var gora = Przewijanie.Offset.Y + Przewijanie.Viewport.Height / 3;
            var biezaca = _uklad.FindIndex(r => r.Bottom + OdstepStron >= gora);
            return biezaca < 0 ? _strony.Count - 1 : biezaca;
        }
    }

    private void PrzyPrzewinieciu(object? sender, ScrollChangedEventArgs e)
    {
        if (_uklad.Count == 0)
            return;
        var biezaca = BiezacaStrona;
        if (Miniatury.SelectedIndex != biezaca && biezaca < _miniatury.Count)
        {
            _ustawianieMiniatury = true;
            Miniatury.SelectedIndex = biezaca;
            Miniatury.ScrollIntoView(biezaca);
            _ustawianieMiniatury = false;
        }
        OdswiezInformacje();
        RenderujWidoczne();
    }

    private void PrzyWyborzeMiniatury(object? sender, SelectionChangedEventArgs e)
    {
        if (!_ustawianieMiniatury && Miniatury.SelectedIndex >= 0)
            PrzewinDoStrony(Miniatury.SelectedIndex);
    }

    private void OdswiezPodpowiedz()
    {
        TekstPodpowiedzi.Text = "Kliknij stronę, aby umieścić podpis, albo przeciągnij, aby narysować go w innym rozmiarze. " +
                                "Podpis można przesuwać, a uchwyty na ramce zmieniają rozmiar (proporcje są zachowane).";
        Podpowiedz.IsVisible = _pokazPodpowiedz;
    }

    private void OdswiezInformacje()
    {
        if (_uklad.Count == 0)
            return;
        var strona = $"Strona {BiezacaStrona + 1} z {_strony.Count}";
        var podpis = _miejsce == null
            ? "kliknij stronę, aby umieścić podpis"
            : $"podpis na stronie {_miejsce.NumerStrony}, {OpisRozmiaru(_miejsce.Skala)}";
        Informacja.Text = $"{strona}  ·  {podpis}";
        PrzyciskPodpisz.IsEnabled = _miejsce != null;
    }

    /// <summary>Rozmiar podpisu jako wielkość imienia i nazwiska (reszta skaluje się proporcjonalnie).</summary>
    private static string OpisRozmiaru(float skala) =>
        $"imię i nazwisko {Wizualizacja.RozmiarNazwiskaWSkali(skala):0.#} pt";

    private RozmiarWizualizacji Rozmiar => _zadanie.Rozmiar;

    // ---- Obrazy stron (renderowane w tle, tylko widoczne) ----

    private double SkalaEkranu => TopLevel.GetTopLevel(this)?.RenderScaling ?? 2;

    private IEnumerable<int> WidoczneStrony(double zapas)
    {
        var gora = Przewijanie.Offset.Y - zapas;
        var dol = Przewijanie.Offset.Y + Przewijanie.Viewport.Height + zapas;
        for (var i = 0; i < _uklad.Count; i++)
        {
            if (_uklad[i].Bottom >= gora && _uklad[i].Top <= dol)
                yield return i;
        }
    }

    private async void RenderujWidoczne()
    {
        if (_renderowanie)
        {
            _ponownie = true;
            return;
        }
        _renderowanie = true;
        try
        {
            do
            {
                _ponownie = false;
                var skalaPikseli = _skala * SkalaEkranu;
                foreach (var i in WidoczneStrony(Przewijanie.Viewport.Height / 2).ToList())
                {
                    if (_skalaObrazu.TryGetValue(i, out var s) && Math.Abs(s - skalaPikseli) < 0.001)
                        continue;
                    ObrazStrony? obraz;
                    try
                    {
                        var pdf = _zadanie.Pdf;
                        var indeks = i;
                        obraz = await WatekRoboczy.Uruchom(() => DokumentPdf.Renderuj(pdf, indeks, skalaPikseli));
                    }
                    catch (Exception)
                    {
                        obraz = null; // stronę, której nie da się wyrenderować, pokazujemy jako pustą
                    }
                    if (_zakonczono)
                        return;
                    if (obraz != null)
                        _obrazyStron[i].Source = Obrazy.NaObraz(obraz);
                    _skalaObrazu[i] = skalaPikseli;
                    if (_ponownie)
                        break;
                }
                ZwolnijNiewidoczne();
            }
            while (_ponownie && !_zakonczono);
        }
        finally
        {
            _renderowanie = false;
        }
    }

    /// <summary>Ogranicza zużycie pamięci przy długich dokumentach.</summary>
    private void ZwolnijNiewidoczne()
    {
        if (_skalaObrazu.Count <= 8)
            return;
        var widoczne = WidoczneStrony(Przewijanie.Viewport.Height * 2).ToHashSet();
        foreach (var i in _skalaObrazu.Keys.Where(i => !widoczne.Contains(i)).ToList())
        {
            _obrazyStron[i].Source = null;
            _skalaObrazu.Remove(i);
        }
    }

    private async void RenderujMiniatury()
    {
        for (var i = 0; i < _miniatury.Count && !_zakonczono; i++)
        {
            try
            {
                var pdf = _zadanie.Pdf;
                var indeks = i;
                var skala = SzerokoscMiniatury * 2 / RozmiarWidoczny(_strony[i]).Width;
                var obraz = await WatekRoboczy.Uruchom(() => DokumentPdf.Renderuj(pdf, indeks, skala));
                if (!_zakonczono)
                    _miniatury[i].Obraz = Obrazy.NaObraz(obraz);
            }
            catch (Exception)
            {
                // Miniatura jest tylko pomocą.
            }
        }
    }

    // ---- Przeliczanie współrzędnych (punkty ekranu ↔ punkty PDF) ----

    private Rect ProstokatNaEkranie(MiejscePodpisu m)
    {
        var s = _strony[m.NumerStrony - 1];
        var r = _uklad[m.NumerStrony - 1];
        var w = Rozmiar.Szerokosc * m.Skala;
        var h = Rozmiar.Wysokosc * m.Skala;
        var lewo = r.X + (m.X - s.Lewo) * _skala;
        var gora = r.Y + (s.Gora - (m.Y + h)) * _skala;
        return new Rect(lewo, gora, w * _skala, h * _skala);
    }

    private (float X, float Y) PunktPdf(int indeks, Point p)
    {
        var s = _strony[indeks];
        var r = _uklad[indeks];
        return ((float)(s.Lewo + (p.X - r.X) / _skala), (float)(s.Gora - (p.Y - r.Y) / _skala));
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
        var (x, y) = Wizualizacja.DopasujDoStrony(_strony[indeks].Prostokat, srodekX, srodekY, Rozmiar, _skalaPodpisu);
        return new MiejscePodpisu(indeks + 1, x, y, _skalaPodpisu);
    }

    /// <summary>
    /// Prostokąt od punktu zaczepienia do wskaźnika: proporcje stałe, rozmiar w granicach skali i strony;
    /// kierunek dowolny.
    /// </summary>
    private MiejscePodpisu ProstokatRysowany(int indeks, float ax, float ay, Point wskaznik)
    {
        var s = _strony[indeks];
        var (cx, cy) = PunktPdf(indeks, wskaznik);
        cx = Math.Clamp(cx, s.Lewo, s.Prawo);
        cy = Math.Clamp(cy, s.Dol, s.Gora);
        float dx = cx - ax, dy = cy - ay;
        var proporcje = Rozmiar.Proporcje;
        var bazowa = Rozmiar.Szerokosc;

        var w = Math.Max(Math.Abs(dx), Math.Abs(dy) * proporcje);
        var dostepnaX = dx >= 0 ? s.Prawo - ax : ax - s.Lewo;
        var dostepnaY = (dy >= 0 ? s.Gora - ay : ay - s.Dol) * proporcje;
        w = Math.Min(w, Math.Min(dostepnaX, dostepnaY));
        w = Math.Clamp(w, bazowa * Wizualizacja.MinSkala, bazowa * Wizualizacja.MaksSkala);

        // Rozmiar skokami 0,5 pt imienia i nazwiska (reszta podpisu w tej samej proporcji).
        var skala = Wizualizacja.ZaokraglijSkale(w / bazowa);
        w = bazowa * skala;
        var h = w / proporcje;
        var x = dx >= 0 ? ax : ax - w;
        var y = dy >= 0 ? ay : ay - h;
        // Przy krawędzi strony minimalny rozmiar może nie zmieścić się od punktu zaczepienia — dosuwamy.
        (x, y) = Wizualizacja.DopasujDoStrony(s.Prostokat, x + w / 2, y + h / 2, Rozmiar, skala);
        return new MiejscePodpisu(indeks + 1, x, y, skala);
    }

    /// <summary>
    /// Prostokąt przy przeciąganiu uchwytu na środku boku: zaczepiony na środku przeciwległego boku, rozmiar
    /// z jednego kierunku, proporcje stałe, wyśrodkowany w drugim kierunku.
    /// </summary>
    private MiejscePodpisu ProstokatZBoku(int indeks, float ax, float ay, Point wskaznik, bool poziomo)
    {
        var s = _strony[indeks];
        var (cx, cy) = PunktPdf(indeks, wskaznik);
        cx = Math.Clamp(cx, s.Lewo, s.Prawo);
        cy = Math.Clamp(cy, s.Dol, s.Gora);
        var proporcje = Rozmiar.Proporcje;
        var bazowa = Rozmiar.Szerokosc;
        var w = poziomo ? Math.Abs(cx - ax) : Math.Abs(cy - ay) * proporcje;
        w = Math.Clamp(w, bazowa * Wizualizacja.MinSkala, bazowa * Wizualizacja.MaksSkala);
        var skala = Wizualizacja.ZaokraglijSkale(w / bazowa);
        w = bazowa * skala;
        var h = w / proporcje;
        float x, y;
        if (poziomo)
        {
            x = cx >= ax ? ax : ax - w;
            y = ay - h / 2;
        }
        else
        {
            y = cy >= ay ? ay : ay - h;
            x = ax - w / 2;
        }
        (x, y) = Wizualizacja.DopasujDoStrony(s.Prostokat, x + w / 2, y + h / 2, Rozmiar, skala);
        return new MiejscePodpisu(indeks + 1, x, y, skala);
    }

    /// <summary>
    /// 8 uchwytów na ramce podpisu: w narożnikach (zaczep w przeciwległym narożniku) i na środkach boków
    /// (zaczep na środku przeciwległego boku). Punkty zaczepienia w układzie PDF.
    /// </summary>
    private IEnumerable<(Point Srodek, float AX, float AY, StandardCursorType Kursor, RodzajPrzeciagania Rodzaj)> Uchwyty(MiejscePodpisu m)
    {
        var r = ProstokatNaEkranie(m);
        var w = Rozmiar.Szerokosc * m.Skala;
        var h = Rozmiar.Wysokosc * m.Skala;
        double sx = r.Left + r.Width / 2, sy = r.Top + r.Height / 2;
        const RodzajPrzeciagania Rog = RodzajPrzeciagania.Rysowanie;
        yield return (new Point(r.Left, r.Top), m.X + w, m.Y, StandardCursorType.TopLeftCorner, Rog);
        yield return (new Point(r.Right, r.Top), m.X, m.Y, StandardCursorType.TopRightCorner, Rog);
        yield return (new Point(r.Left, r.Bottom), m.X + w, m.Y + h, StandardCursorType.BottomLeftCorner, Rog);
        yield return (new Point(r.Right, r.Bottom), m.X, m.Y + h, StandardCursorType.BottomRightCorner, Rog);
        yield return (new Point(sx, r.Top), m.X + w / 2, m.Y, StandardCursorType.SizeNorthSouth, RodzajPrzeciagania.KrawedzPionowa);
        yield return (new Point(sx, r.Bottom), m.X + w / 2, m.Y + h, StandardCursorType.SizeNorthSouth, RodzajPrzeciagania.KrawedzPionowa);
        yield return (new Point(r.Left, sy), m.X + w, m.Y + h / 2, StandardCursorType.SizeWestEast, RodzajPrzeciagania.KrawedzPozioma);
        yield return (new Point(r.Right, sy), m.X, m.Y + h / 2, StandardCursorType.SizeWestEast, RodzajPrzeciagania.KrawedzPozioma);
    }

    private static bool WUchwycie(Point srodek, Point p) =>
        Math.Abs(p.X - srodek.X) <= PolowaUchwytu && Math.Abs(p.Y - srodek.Y) <= PolowaUchwytu;

    private void UstawKursor(StandardCursorType ksztalt)
    {
        if (_kursor == ksztalt)
            return;
        _kursor = ksztalt;
        Plotno.Cursor = new Cursor(ksztalt);
    }

    // ---- Obsługa wskaźnika ----

    private async void PrzyWcisnieciu(object? sender, PointerPressedEventArgs e)
    {
        var punkt = e.GetCurrentPoint(Plotno);
        if (!punkt.Properties.IsLeftButtonPressed || _uklad.Count == 0)
            return;
        var p = punkt.Position;
        e.Handled = true;
        Focus();
        if (_pokazPodpowiedz)
        {
            _pokazPodpowiedz = false;
            OdswiezPodpowiedz();
        }

        if (_miejsce != null)
        {
            foreach (var u in Uchwyty(_miejsce))
            {
                if (WUchwycie(u.Srodek, p))
                {
                    // Zmiana rozmiaru od przeciwległego narożnika albo boku, od razu (bez progu).
                    _przeciaganie = new Przeciaganie(u.Rodzaj, _miejsce.NumerStrony - 1, u.AX, u.AY, new Point(-1000, -1000));
                    e.Pointer.Capture(Plotno);
                    return;
                }
            }
            if (ProstokatNaEkranie(_miejsce).Contains(p))
            {
                var (px, py) = PunktPdf(_miejsce.NumerStrony - 1, p);
                _przeciaganie = new Przeciaganie(RodzajPrzeciagania.Przesuwanie, _miejsce.NumerStrony - 1, px - _miejsce.X, py - _miejsce.Y, p);
                e.Pointer.Capture(Plotno);
                return;
            }
        }

        var indeks = StronaPod(p);
        if (indeks < 0)
            return;
        if (_strony[indeks].Obrot != 0)
        {
            await Dialogi.PokazAsync(_okno, "Strona obrócona",
                "Ta strona jest obrócona — podpisu nie można na niej umieścić. Wskaż miejsce na innej stronie.");
            return;
        }
        var (x, y) = PunktPdf(indeks, p);
        // Kliknięcie umieszcza podpis w bieżącym rozmiarze, przeciągnięcie rysuje go w nowym.
        _przeciaganie = new Przeciaganie(RodzajPrzeciagania.Rysowanie, indeks, x, y, p);
        _rysowane = null;
        e.Pointer.Capture(Plotno);
    }

    private void PrzyRuchu(object? sender, PointerEventArgs e)
    {
        if (_uklad.Count == 0)
            return;
        var p = e.GetPosition(Plotno);
        if (_przeciaganie is { } d)
        {
            if (_rysowane == null && Math.Abs(p.X - d.Start.X) < ProgPrzeciagania && Math.Abs(p.Y - d.Start.Y) < ProgPrzeciagania)
                return;
            _podWskaznikiem = null;
            if (d.Rodzaj == RodzajPrzeciagania.Rysowanie)
            {
                _rysowane = ProstokatRysowany(d.Indeks, d.X, d.Y, p);
            }
            else if (d.Rodzaj is RodzajPrzeciagania.KrawedzPozioma or RodzajPrzeciagania.KrawedzPionowa)
            {
                _rysowane = ProstokatZBoku(d.Indeks, d.X, d.Y, p, d.Rodzaj == RodzajPrzeciagania.KrawedzPozioma);
            }
            else if (_miejsce != null)
            {
                var (px, py) = PunktPdf(d.Indeks, p);
                var w = Rozmiar.Szerokosc * _miejsce.Skala;
                var h = Rozmiar.Wysokosc * _miejsce.Skala;
                var (nx, ny) = Wizualizacja.DopasujDoStrony(_strony[d.Indeks].Prostokat, px - d.X + w / 2, py - d.Y + h / 2, Rozmiar, _miejsce.Skala);
                _rysowane = _miejsce with { X = nx, Y = ny };
            }
            OdswiezNakladki();
            if (_rysowane != null)
                Informacja.Text = $"Rozmiar podpisu: {OpisRozmiaru(_rysowane.Skala)}";
            return;
        }

        // Kursor: uchwyty, przesuwanie, strona.
        if (_miejsce != null)
        {
            foreach (var u in Uchwyty(_miejsce))
            {
                if (WUchwycie(u.Srodek, p))
                {
                    UstawPodWskaznikiem(null);
                    UstawKursor(u.Kursor);
                    return;
                }
            }
            if (ProstokatNaEkranie(_miejsce).Contains(p))
            {
                UstawPodWskaznikiem(null);
                UstawKursor(StandardCursorType.SizeAll);
                return;
            }
        }
        UstawPodWskaznikiem(MiejsceWPunkcie(p));
        var indeks = StronaPod(p);
        UstawKursor(indeks >= 0 && _strony[indeks].Obrot == 0 ? StandardCursorType.Cross : StandardCursorType.Arrow);
    }

    private void UstawPodWskaznikiem(MiejscePodpisu? nowe)
    {
        if (nowe == _podWskaznikiem)
            return;
        _podWskaznikiem = nowe;
        OdswiezNakladki();
    }

    private void PrzyZwolnieniu(object? sender, PointerReleasedEventArgs e)
    {
        if (_przeciaganie is not { } d)
            return;
        _przeciaganie = null;
        e.Pointer.Capture(null);

        // Bez przeciągnięcia — zwykłe kliknięcie: podpis w ostatnim rozmiarze, wyśrodkowany w punkcie kliknięcia.
        var nowe = _rysowane ?? (d.Rodzaj == RodzajPrzeciagania.Rysowanie ? MiejsceWPunkcie(d.Start) : null);
        _rysowane = null;
        if (nowe != null)
        {
            _miejsce = nowe;
            _skalaPodpisu = nowe.Skala;
        }
        OdswiezNakladki();
        OdswiezInformacje();
    }

    private void PrzerwijPrzeciaganie()
    {
        if (_przeciaganie == null)
            return;
        _przeciaganie = null;
        _rysowane = null;
        OdswiezNakladki();
        OdswiezInformacje();
    }

    // ---- Nakładki: podpis, uchwyty, etykieta rozmiaru ----

    private void OdswiezNakladki()
    {
        if (_uklad.Count == 0)
            return;
        var rysowanie = _rysowane != null;
        if (_miejsce != null)
            _nakladkaMiejsca.Pokaz(ProstokatNaEkranie(_miejsce), rysowanie ? 0.3 : 1);
        else
            _nakladkaMiejsca.Ukryj();

        if (_rysowane != null)
            _nakladkaRysowana.Pokaz(ProstokatNaEkranie(_rysowane), 0.85);
        else
            _nakladkaRysowana.Ukryj();

        if (!rysowanie && _podWskaznikiem != null && _podWskaznikiem != _miejsce)
            _nakladkaPodWskaznikiem.Pokaz(ProstokatNaEkranie(_podWskaznikiem), 0.45);
        else
            _nakladkaPodWskaznikiem.Ukryj();

        var uchwyty = !rysowanie && _miejsce != null ? Uchwyty(_miejsce).ToList() : null;
        for (var i = 0; i < _uchwyty.Length; i++)
        {
            if (uchwyty == null)
            {
                _uchwyty[i].IsVisible = false;
                continue;
            }
            Canvas.SetLeft(_uchwyty[i], uchwyty[i].Srodek.X - 5);
            Canvas.SetTop(_uchwyty[i], uchwyty[i].Srodek.Y - 5);
            _uchwyty[i].IsVisible = true;
        }

        var opisywane = _rysowane ?? _miejsce;
        if (opisywane == null)
        {
            _etykieta.IsVisible = false;
            return;
        }
        var r = ProstokatNaEkranie(opisywane);
        _tekstEtykiety.Text = OpisRozmiaru(opisywane.Skala);
        _etykieta.IsVisible = true;
        _etykieta.Measure(Size.Infinity);
        Canvas.SetLeft(_etykieta, r.Left + (r.Width - _etykieta.DesiredSize.Width) / 2);
        Canvas.SetTop(_etykieta, r.Bottom + 10);
    }

    /// <summary>Wizualizacja podpisu na stronie: tło w kolorze akcentu, obraz i obramowanie.</summary>
    private sealed class Nakladka
    {
        private readonly Image _obraz;

        public Nakladka(Bitmap wizualizacja, IBrush akcent, IBrush tlo, bool przerywana, double grubosc)
        {
            _obraz = new Image { Source = wizualizacja, Stretch = Stretch.Fill };
            var ramka = new Rectangle { Stroke = akcent, StrokeThickness = grubosc };
            if (przerywana)
                ramka.StrokeDashArray = new AvaloniaList<double> { 4, 2 };
            Element = new Panel { IsHitTestVisible = false, IsVisible = false };
            Element.Children.Add(new Rectangle { Fill = tlo });
            Element.Children.Add(_obraz);
            Element.Children.Add(ramka);
        }

        public Panel Element { get; }

        public void Pokaz(Rect r, double krycie)
        {
            Canvas.SetLeft(Element, r.X);
            Canvas.SetTop(Element, r.Y);
            Element.Width = r.Width;
            Element.Height = r.Height;
            _obraz.Opacity = krycie;
            Element.IsVisible = true;
        }

        public void Ukryj() => Element.IsVisible = false;
    }
}
