using System.ComponentModel;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
using PodpisPdf.Core;
using Windows.Foundation;
using Windows.System;
using Windows.UI;

namespace PodpisPdf;

/// <summary>Dane do wskazania miejsca podpisu i wynik (miejsce albo null po anulowaniu).</summary>
internal sealed class ZadaniePodgladu
{
    // Rozdzielczość podglądu wizualizacji (piksele na punkt przy skali 1) — wystarcza także dla największej skali.
    private const int SkalaPodgladuWizualizacji = 8;

    private ZadaniePodgladu(string plik, byte[] pdf, IReadOnlyList<StronaPdf> strony, ObrazStrony wizualizacja,
        PolozenieWizualizacji polozenie, float skala)
    {
        Plik = plik;
        Pdf = pdf;
        Strony = strony;
        Wizualizacja = wizualizacja;
        Polozenie = polozenie;
        Skala = skala;
    }

    public string Plik { get; }
    public byte[] Pdf { get; }
    public IReadOnlyList<StronaPdf> Strony { get; }
    public ObrazStrony Wizualizacja { get; }
    public PolozenieWizualizacji Polozenie { get; }
    public float Skala { get; }

    /// <summary>Do zrzutów ekranu: od razu tryb rysowania.</summary>
    public bool TrybRysowania { get; init; }

    public TaskCompletionSource<MiejscePodpisu?> Wynik { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Wczytanie dokumentu i wizualizacji (w tle, przed pokazaniem podglądu).</summary>
    public static ZadaniePodgladu Przygotuj(string plik, DaneWizualizacji dane, PolozenieWizualizacji polozenie, float skala, bool trybRysowania = false)
    {
        var pdf = File.ReadAllBytes(plik);
        var strony = DokumentPdf.Strony(plik);
        if (strony.Count == 0)
            throw new BladPodpisu("Dokument PDF nie ma żadnej strony.");
        var wizualizacja = DokumentPdf.Renderuj(Core.Wizualizacja.PodgladPdf(dane), 0, SkalaPodgladuWizualizacji, bialeTlo: false);
        return new ZadaniePodgladu(plik, pdf, strony, wizualizacja, polozenie, skala) { TrybRysowania = trybRysowania };
    }
}

/// <summary>Miniatura strony na pasku po lewej.</summary>
public sealed partial class MiniaturaStrony(int numer, double szerokosc, double wysokosc) : INotifyPropertyChanged
{
    private ImageSource? _obraz;

    public int Numer { get; } = numer;
    public double Szerokosc { get; } = szerokosc;
    public double Wysokosc { get; } = wysokosc;

    public ImageSource? Obraz
    {
        get => _obraz;
        set
        {
            _obraz = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Obraz)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public override string ToString() => $"Strona {Numer}";
}

/// <summary>Powierzchnia ze stronami — z możliwością zmiany kursora (krzyżyk, uchwyty).</summary>
internal sealed partial class PowierzchniaPodgladu : Grid
{
    private InputSystemCursorShape? _kursor;

    public void UstawKursor(InputSystemCursorShape ksztalt)
    {
        if (_kursor == ksztalt)
            return;
        _kursor = ksztalt;
        ProtectedCursor = InputSystemCursor.Create(ksztalt);
    }
}

/// <summary>
/// Podgląd wszystkich stron dokumentu. Kliknięcie umieszcza wizualizację podpisu, przeciągnięcie (w trybie
/// rysowania) rysuje ją w wybranym rozmiarze, a uchwyty i przeciąganie podpisu pozwalają go poprawić.
/// Proporcje są zawsze stałe (85 : 25).
/// </summary>
public sealed partial class StronaPodgladu : Page
{
    private const double OdstepStron = 16;
    private const double MarginesBoczny = 24;
    // Największe powiększenie przy dopasowaniu do szerokości (DIP na punkt PDF).
    private const double MaksSkalaDopasowania = 2;
    // Przesunięcie wskaźnika (DIP), od którego kliknięcie staje się rysowaniem.
    private const double ProgPrzeciagania = 5;
    // Połowa obszaru chwytania uchwytu (DIP); widoczny uchwyt ma 10 × 10.
    private const double PolowaUchwytu = 8;
    private const double SzerokoscMiniatury = 104;
    private static readonly int[] Powiekszenia = { 50, 67, 75, 100, 125, 150, 200, 300 };

    private ZadaniePodgladu _zadanie = null!;
    private IReadOnlyList<StronaPdf> _strony = Array.Empty<StronaPdf>();
    private readonly List<Border> _ramkiStron = new();
    private readonly List<Image> _obrazyStron = new();
    private readonly List<Rect> _uklad = new();
    private readonly Dictionary<int, double> _skalaObrazu = new();
    private List<MiniaturaStrony> _miniatury = new();
    private readonly PowierzchniaPodgladu _powierzchnia = new();
    private readonly Canvas _plotno = new();
    private Nakladka _nakladkaMiejsca = null!;
    private Nakladka _nakladkaRysowana = null!;
    private Nakladka _nakladkaPodWskaznikiem = null!;
    private readonly Rectangle[] _uchwyty = new Rectangle[4];
    private readonly Border _etykieta = new();
    private readonly TextBlock _tekstEtykiety = new();

    private double _skala = 1;
    private bool _dopasuj = true;
    private int _powiekszenie = 100;
    private bool _renderowanie;
    private bool _ponownie;
    private bool _ustawianieMiniatury;
    private bool _zakonczono;

    private MiejscePodpisu? _miejsce;
    private MiejscePodpisu? _podWskaznikiem;
    // Skala podpisu przy kliknięciu (ostatnio narysowany rozmiar).
    private float _skalaPodpisu;
    private Przeciaganie? _przeciaganie;
    private MiejscePodpisu? _rysowane;
    private bool _pokazPodpowiedz = true;

    private enum RodzajPrzeciagania { Klikniecie, Rysowanie, Przesuwanie }

    /// <summary>Rozpoczęte przeciąganie: strona, punkt zaczepienia (PDF) i miejsce wciśnięcia (DIP).</summary>
    private sealed record Przeciaganie(RodzajPrzeciagania Rodzaj, int Indeks, float X, float Y, Point Start);

    public StronaPodgladu()
    {
        InitializeComponent();
        _powierzchnia.Background = new SolidColorBrush(Colors.Transparent);
        _powierzchnia.HorizontalAlignment = HorizontalAlignment.Left;
        _powierzchnia.VerticalAlignment = VerticalAlignment.Top;
        _powierzchnia.Children.Add(_plotno);
        _powierzchnia.PointerPressed += PrzyWcisnieciu;
        _powierzchnia.PointerMoved += PrzyRuchu;
        _powierzchnia.PointerReleased += PrzyZwolnieniu;
        _powierzchnia.PointerCaptureLost += (_, _) => PrzerwijPrzeciaganie();
        _powierzchnia.PointerExited += (_, _) => { if (_przeciaganie == null) UstawPodWskaznikiem(null); };
        _powierzchnia.PointerWheelChanged += PrzyKolkuMyszy;
        Przewijanie.Content = _powierzchnia;
        Loaded += PoZaladowaniu;
    }

    private static OknoGlowne Okno => App.Okno!;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _zadanie = (ZadaniePodgladu)e.Parameter;
        _strony = _zadanie.Strony;
        _skalaPodpisu = Math.Clamp(_zadanie.Skala, Wizualizacja.MinSkala, Wizualizacja.MaksSkala);
        Okno.UstawPodtytul($"Wskaż miejsce podpisu — {System.IO.Path.GetFileName(_zadanie.Plik)}");
        Okno.PowiekszDoPodgladu();
        Utworz();
        if (_zadanie.TrybRysowania)
            Tryb.SelectedIndex = 1;
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        Okno.UstawPodtytul(null);
        Okno.PrzywrocRozmiarPoPodgladzie();
        if (!_zakonczono)
        {
            _zakonczono = true;
            _zadanie.Wynik.TrySetResult(null);
        }
    }

    private void Utworz()
    {
        foreach (var _ in _strony)
        {
            var obraz = new Image { Stretch = Stretch.Fill };
            var ramka = new Border
            {
                Background = new SolidColorBrush(Colors.White),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x30, 0, 0, 0)),
                BorderThickness = new Thickness(1),
                Child = obraz,
            };
            _obrazyStron.Add(obraz);
            _ramkiStron.Add(ramka);
            _plotno.Children.Add(ramka);
        }

        var akcent = Application.Current.Resources.TryGetValue("SystemAccentColor", out var kolor) && kolor is Color c
            ? c
            : Color.FromArgb(255, 0, 95, 184);
        var pedzelAkcentu = new SolidColorBrush(akcent);
        var wizualizacja = Obrazy.NaObraz(_zadanie.Wizualizacja);
        var tlo = new SolidColorBrush(Color.FromArgb(18, akcent.R, akcent.G, akcent.B));
        _nakladkaMiejsca = new Nakladka(wizualizacja, pedzelAkcentu, tlo, przerywana: false, grubosc: 2);
        _nakladkaRysowana = new Nakladka(wizualizacja, pedzelAkcentu, tlo, przerywana: true, grubosc: 2);
        _nakladkaPodWskaznikiem = new Nakladka(wizualizacja, pedzelAkcentu, tlo, przerywana: true, grubosc: 1);
        _plotno.Children.Add(_nakladkaMiejsca.Element);
        _plotno.Children.Add(_nakladkaPodWskaznikiem.Element);
        _plotno.Children.Add(_nakladkaRysowana.Element);
        for (var i = 0; i < _uchwyty.Length; i++)
        {
            _uchwyty[i] = new Rectangle
            {
                Width = 10, Height = 10, RadiusX = 2, RadiusY = 2,
                Fill = new SolidColorBrush(Colors.White), Stroke = pedzelAkcentu, StrokeThickness = 2,
                IsHitTestVisible = false, Visibility = Visibility.Collapsed,
            };
            _plotno.Children.Add(_uchwyty[i]);
        }
        _tekstEtykiety.Foreground = new SolidColorBrush(Colors.White);
        _tekstEtykiety.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        _tekstEtykiety.FontSize = 12;
        _etykieta.Child = _tekstEtykiety;
        _etykieta.Background = pedzelAkcentu;
        _etykieta.CornerRadius = new CornerRadius(12);
        _etykieta.Padding = new Thickness(10, 3, 10, 4);
        _etykieta.IsHitTestVisible = false;
        _etykieta.Visibility = Visibility.Collapsed;
        _plotno.Children.Add(_etykieta);

        _miniatury = _strony.Select((s, i) =>
        {
            var r = RozmiarWidoczny(s);
            return new MiniaturaStrony(i + 1, SzerokoscMiniatury, SzerokoscMiniatury * r.Height / r.Width);
        }).ToList();
        Miniatury.ItemsSource = _miniatury;

        _miejsce = MiejsceDomyslne(_zadanie.Polozenie);
        OdswiezPodpowiedz();
    }

    private void PoZaladowaniu(object sender, RoutedEventArgs e)
    {
        UlozStrony();
        PrzewinDoMiejsca();
        RenderujMiniatury();
        Przewijanie.Focus(FocusState.Programmatic);
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

    // ---- Zakończenie ----

    private void PrzyAnulowaniu(object sender, RoutedEventArgs e) => Zakoncz(null);

    private void PrzyPodpisie(object sender, RoutedEventArgs e)
    {
        if (_miejsce != null)
            Zakoncz(_miejsce);
    }

    private void Zakoncz(MiejscePodpisu? wynik)
    {
        if (_zakonczono)
            return;
        _zakonczono = true;
        if (Frame.CanGoBack)
            Frame.GoBack();
        _zadanie.Wynik.TrySetResult(wynik);
    }

    // ---- Układ stron i powiększenie ----

    private static Size RozmiarWidoczny(StronaPdf s) =>
        s.Obrot % 180 == 0 ? new Size(s.Szerokosc, s.Wysokosc) : new Size(s.Wysokosc, s.Szerokosc);

    /// <summary>DIP na punkt PDF przy 100% (rzeczywisty rozmiar strony na ekranie).</summary>
    private const double SkalaRzeczywista = 96.0 / 72.0;

    private void PrzyPomniejszeniu(object sender, RoutedEventArgs e) => ZmienPowiekszenie(-1);

    private void PrzyPowiekszeniu(object sender, RoutedEventArgs e) => ZmienPowiekszenie(+1);

    private void PrzyDopasowaniu(object sender, RoutedEventArgs e)
    {
        _dopasuj = true;
        UlozStrony();
    }

    private void PrzyZmianieRozmiaru(object sender, SizeChangedEventArgs e) => UlozStrony();

    private void PrzyKolkuMyszy(object sender, PointerRoutedEventArgs e)
    {
        if (!e.KeyModifiers.HasFlag(VirtualKeyModifiers.Control))
            return;
        ZmienPowiekszenie(e.GetCurrentPoint(_plotno).Properties.MouseWheelDelta > 0 ? +1 : -1);
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
        var srodek = Przewijanie.VerticalOffset + Przewijanie.ViewportHeight / 2;
        var wzgledny = _powierzchnia.Height > 0 ? srodek / _powierzchnia.Height : 0;
        UlozStrony();
        Przewijanie.UpdateLayout();
        Przewijanie.ChangeView(null, Math.Max(0, wzgledny * _powierzchnia.Height - Przewijanie.ViewportHeight / 2), null, true);
    }

    private void UlozStrony()
    {
        if (_strony.Count == 0 || _ramkiStron.Count == 0 || Przewijanie.ActualWidth <= 0)
            return;
        var szerokoscWidoku = Przewijanie.ActualWidth;
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
        var szerokosc = Math.Max(szerokoscWidoku, _uklad.Max(r => r.Right) + MarginesBoczny);
        _plotno.Width = _powierzchnia.Width = szerokosc;
        _plotno.Height = _powierzchnia.Height = y;
        OdswiezNakladki();
        OdswiezInformacje();
        RenderujWidoczne();
    }

    private void PrzewinDoMiejsca()
    {
        if (_miejsce == null || _uklad.Count == 0)
            return;
        var r = ProstokatNaEkranie(_miejsce);
        Przewijanie.UpdateLayout();
        Przewijanie.ChangeView(null, Math.Max(0, r.Top - Przewijanie.ViewportHeight / 2), null, true);
    }

    private void PrzewinDoStrony(int indeks)
    {
        if (indeks < 0 || indeks >= _uklad.Count)
            return;
        Przewijanie.ChangeView(null, Math.Max(0, _uklad[indeks].Top - OdstepStron), null, false);
    }

    private int BiezacaStrona
    {
        get
        {
            var gora = Przewijanie.VerticalOffset + Przewijanie.ViewportHeight / 3;
            var biezaca = _uklad.FindIndex(r => r.Bottom + OdstepStron >= gora);
            return biezaca < 0 ? _strony.Count - 1 : biezaca;
        }
    }

    private void PrzyPrzewinieciu(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        if (_uklad.Count == 0)
            return;
        var biezaca = BiezacaStrona;
        if (Miniatury.SelectedIndex != biezaca && biezaca < _miniatury.Count)
        {
            _ustawianieMiniatury = true;
            Miniatury.SelectedIndex = biezaca;
            Miniatury.ScrollIntoView(_miniatury[biezaca]);
            _ustawianieMiniatury = false;
        }
        OdswiezInformacje();
        RenderujWidoczne();
    }

    private void PrzyWyborzeMiniatury(object sender, SelectionChangedEventArgs e)
    {
        if (!_ustawianieMiniatury && Miniatury.SelectedIndex >= 0)
            PrzewinDoStrony(Miniatury.SelectedIndex);
    }

    private void PrzyZmianieTrybu(object sender, SelectionChangedEventArgs e)
    {
        _pokazPodpowiedz = true;
        OdswiezPodpowiedz();
        OdswiezInformacje();
    }

    private bool TrybRysowania => Tryb.SelectedIndex == 1;

    private void OdswiezPodpowiedz()
    {
        TekstPodpowiedzi.Text = TrybRysowania
            ? "Przeciągnij na stronie, aby narysować podpis — proporcje są zachowane"
            : "Kliknij stronę, aby umieścić podpis. Uchwyty w narożnikach zmieniają rozmiar.";
        Podpowiedz.Visibility = _pokazPodpowiedz ? Visibility.Visible : Visibility.Collapsed;
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

    private static string OpisRozmiaru(float skala) =>
        $"{Wizualizacja.SzerokoscMm * skala:0} × {Wizualizacja.WysokoscMm * skala:0} mm";

    // ---- Obrazy stron (renderowane w tle, tylko widoczne) ----

    private IEnumerable<int> WidoczneStrony(double zapas)
    {
        var gora = Przewijanie.VerticalOffset - zapas;
        var dol = Przewijanie.VerticalOffset + Przewijanie.ViewportHeight + zapas;
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
                var skalaPikseli = _skala * (XamlRoot?.RasterizationScale ?? 1);
                foreach (var i in WidoczneStrony(Przewijanie.ViewportHeight / 2).ToList())
                {
                    if (_skalaObrazu.TryGetValue(i, out var s) && Math.Abs(s - skalaPikseli) < 0.001)
                        continue;
                    ObrazStrony? obraz;
                    try
                    {
                        var pdf = _zadanie.Pdf;
                        var indeks = i;
                        obraz = await Task.Run(() => DokumentPdf.Renderuj(pdf, indeks, skalaPikseli));
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
        var widoczne = WidoczneStrony(Przewijanie.ViewportHeight * 2).ToHashSet();
        foreach (var i in _skalaObrazu.Keys.Where(i => !widoczne.Contains(i)).ToList())
        {
            _obrazyStron[i].Source = null;
            _skalaObrazu.Remove(i);
        }
    }

    private async void RenderujMiniatury()
    {
        var rozdzielczosc = XamlRoot?.RasterizationScale ?? 1;
        for (var i = 0; i < _miniatury.Count && !_zakonczono; i++)
        {
            try
            {
                var pdf = _zadanie.Pdf;
                var indeks = i;
                var skala = SzerokoscMiniatury * rozdzielczosc / RozmiarWidoczny(_strony[i]).Width;
                var obraz = await Task.Run(() => DokumentPdf.Renderuj(pdf, indeks, skala));
                if (!_zakonczono)
                    _miniatury[i].Obraz = Obrazy.NaObraz(obraz);
            }
            catch (Exception)
            {
                // Miniatura jest tylko pomocą.
            }
        }
    }

    // ---- Przeliczanie współrzędnych (DIP podglądu ↔ punkty PDF) ----

    private Rect ProstokatNaEkranie(MiejscePodpisu m)
    {
        var s = _strony[m.NumerStrony - 1];
        var r = _uklad[m.NumerStrony - 1];
        var w = Wizualizacja.SzerokoscPt * m.Skala;
        var h = Wizualizacja.WysokoscPt * m.Skala;
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
        var (x, y) = Wizualizacja.DopasujDoStrony(_strony[indeks].Prostokat, srodekX, srodekY, _skalaPodpisu);
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

    /// <summary>Uchwyty w narożnikach podpisu (DIP) i przeciwległe narożniki (punkty PDF) jako zaczepienie.</summary>
    private IEnumerable<(Point Srodek, float AX, float AY, InputSystemCursorShape Kursor)> Uchwyty(MiejscePodpisu m)
    {
        var r = ProstokatNaEkranie(m);
        var w = Wizualizacja.SzerokoscPt * m.Skala;
        var h = Wizualizacja.WysokoscPt * m.Skala;
        yield return (new Point(r.Left, r.Top), m.X + w, m.Y, InputSystemCursorShape.SizeNorthwestSoutheast);      // lewy górny → zaczep prawy dolny
        yield return (new Point(r.Right, r.Top), m.X, m.Y, InputSystemCursorShape.SizeNortheastSouthwest);         // prawy górny → lewy dolny
        yield return (new Point(r.Left, r.Bottom), m.X + w, m.Y + h, InputSystemCursorShape.SizeNortheastSouthwest); // lewy dolny → prawy górny
        yield return (new Point(r.Right, r.Bottom), m.X, m.Y + h, InputSystemCursorShape.SizeNorthwestSoutheast);  // prawy dolny → lewy górny
    }

    private static bool WUchwycie(Point srodek, Point p) =>
        Math.Abs(p.X - srodek.X) <= PolowaUchwytu && Math.Abs(p.Y - srodek.Y) <= PolowaUchwytu;

    // ---- Obsługa wskaźnika ----

    private async void PrzyWcisnieciu(object sender, PointerRoutedEventArgs e)
    {
        var punkt = e.GetCurrentPoint(_plotno);
        if (!punkt.Properties.IsLeftButtonPressed || _uklad.Count == 0)
            return;
        var p = punkt.Position;
        e.Handled = true;
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
                    // Zmiana rozmiaru: rysowanie od przeciwległego narożnika, od razu (bez progu).
                    _przeciaganie = new Przeciaganie(RodzajPrzeciagania.Rysowanie, _miejsce.NumerStrony - 1, u.AX, u.AY, new Point(-1000, -1000));
                    _powierzchnia.CapturePointer(e.Pointer);
                    return;
                }
            }
            if (ProstokatNaEkranie(_miejsce).Contains(p))
            {
                var (px, py) = PunktPdf(_miejsce.NumerStrony - 1, p);
                _przeciaganie = new Przeciaganie(RodzajPrzeciagania.Przesuwanie, _miejsce.NumerStrony - 1, px - _miejsce.X, py - _miejsce.Y, p);
                _powierzchnia.CapturePointer(e.Pointer);
                return;
            }
        }

        var indeks = StronaPod(p);
        if (indeks < 0)
            return;
        if (_strony[indeks].Obrot != 0)
        {
            await Dialogi.PokazAsync(XamlRoot, "Strona obrócona",
                "Ta strona jest obrócona — podpisu nie można na niej umieścić. Wskaż miejsce na innej stronie.");
            return;
        }
        var (x, y) = PunktPdf(indeks, p);
        _przeciaganie = new Przeciaganie(TrybRysowania ? RodzajPrzeciagania.Rysowanie : RodzajPrzeciagania.Klikniecie, indeks, x, y, p);
        _rysowane = null;
        _powierzchnia.CapturePointer(e.Pointer);
    }

    private void PrzyRuchu(object sender, PointerRoutedEventArgs e)
    {
        if (_uklad.Count == 0)
            return;
        var p = e.GetCurrentPoint(_plotno).Position;
        if (_przeciaganie is { } d)
        {
            if (d.Rodzaj == RodzajPrzeciagania.Klikniecie)
                return;
            if (_rysowane == null && Math.Abs(p.X - d.Start.X) < ProgPrzeciagania && Math.Abs(p.Y - d.Start.Y) < ProgPrzeciagania)
                return;
            _podWskaznikiem = null;
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
                    _powierzchnia.UstawKursor(u.Kursor);
                    return;
                }
            }
            if (ProstokatNaEkranie(_miejsce).Contains(p))
            {
                UstawPodWskaznikiem(null);
                _powierzchnia.UstawKursor(InputSystemCursorShape.SizeAll);
                return;
            }
        }
        UstawPodWskaznikiem(MiejsceWPunkcie(p));
        var indeks = StronaPod(p);
        _powierzchnia.UstawKursor(indeks >= 0 && _strony[indeks].Obrot == 0 ? InputSystemCursorShape.Cross : InputSystemCursorShape.Arrow);
    }

    private void UstawPodWskaznikiem(MiejscePodpisu? nowe)
    {
        if (nowe == _podWskaznikiem)
            return;
        _podWskaznikiem = nowe;
        OdswiezNakladki();
    }

    private void PrzyZwolnieniu(object sender, PointerRoutedEventArgs e)
    {
        if (_przeciaganie is not { } d)
            return;
        _przeciaganie = null;
        _powierzchnia.ReleasePointerCapture(e.Pointer);

        // Bez przeciągnięcia — zwykłe kliknięcie: podpis w ostatnim rozmiarze, wyśrodkowany w punkcie kliknięcia.
        var nowe = _rysowane ?? (d.Rodzaj == RodzajPrzeciagania.Przesuwanie ? null : MiejsceWPunkcie(d.Start));
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
                _uchwyty[i].Visibility = Visibility.Collapsed;
                continue;
            }
            Canvas.SetLeft(_uchwyty[i], uchwyty[i].Srodek.X - 5);
            Canvas.SetTop(_uchwyty[i], uchwyty[i].Srodek.Y - 5);
            _uchwyty[i].Visibility = Visibility.Visible;
        }

        var opisywane = _rysowane ?? _miejsce;
        if (opisywane == null)
        {
            _etykieta.Visibility = Visibility.Collapsed;
            return;
        }
        var r = ProstokatNaEkranie(opisywane);
        _tekstEtykiety.Text = OpisRozmiaru(opisywane.Skala);
        _etykieta.Visibility = Visibility.Visible;
        _etykieta.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(_etykieta, r.Left + (r.Width - _etykieta.DesiredSize.Width) / 2);
        Canvas.SetTop(_etykieta, r.Bottom + 10);
    }

    /// <summary>Wizualizacja podpisu na stronie: tło w kolorze akcentu, obraz i obramowanie.</summary>
    private sealed class Nakladka
    {
        private readonly Image _obraz;

        public Nakladka(ImageSource wizualizacja, Brush akcent, Brush tlo, bool przerywana, double grubosc)
        {
            _obraz = new Image { Source = wizualizacja, Stretch = Stretch.Fill };
            var ramka = new Rectangle { Stroke = akcent, StrokeThickness = grubosc };
            if (przerywana)
                ramka.StrokeDashArray = new DoubleCollection { 4, 2 };
            Element = new Grid { IsHitTestVisible = false, Visibility = Visibility.Collapsed };
            Element.Children.Add(new Rectangle { Fill = tlo });
            Element.Children.Add(_obraz);
            Element.Children.Add(ramka);
        }

        public Grid Element { get; }

        public void Pokaz(Rect r, double krycie)
        {
            Canvas.SetLeft(Element, r.X);
            Canvas.SetTop(Element, r.Y);
            Element.Width = r.Width;
            Element.Height = r.Height;
            _obraz.Opacity = krycie;
            Element.Visibility = Visibility.Visible;
        }

        public void Ukryj() => Element.Visibility = Visibility.Collapsed;
    }
}
