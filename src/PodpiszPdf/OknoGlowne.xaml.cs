using System.Collections.Concurrent;
using System.Windows.Input;
using H.NotifyIcon;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using PodpiszPdf.Core;
using WinRT.Interop;

namespace PodpiszPdf;

/// <summary>
/// Jedyne okno programu: strony (główna, podgląd, ustawienia), ikona w zasobniku, polecenia z kolejnych
/// uruchomień programu (menu kontekstowe plików PDF).
/// </summary>
public sealed partial class OknoGlowne : Window
{
    private readonly bool _trybTestowy;
    private readonly ConcurrentQueue<string> _zlecenia = new();
    // Kolejka wątku interfejsu — zapamiętana w konstruktorze, bo polecenia z potoku przychodzą w wątku tła.
    private readonly DispatcherQueue _kolejkaUi;
    private TaskbarIcon? _zasobnik;
    private bool _pokazanoDymek;
    private bool _zamykanie;
    private bool _pytanieOZamkniecie;

    internal OknoGlowne(bool startWZasobniku, bool trybTestowy)
    {
        _trybTestowy = trybTestowy;
        _kolejkaUi = DispatcherQueue;
        App.Okno = this; // strony odwołują się do okna już w konstruktorach
        Ustawienia = trybTestowy ? new Ustawienia() : Ustawienia.Wczytaj();
        if (!trybTestowy)
            Ustawienia.PrzeniesLogoDoFolderuUstawien();

        App.Etap = "okno główne (XAML)";
        InitializeComponent();
        Hwnd = WindowNative.GetWindowHandle(this);

        Title = TytulProgramu;
        TytulOkna.Text = TytulProgramu;
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(PasekTytulu);
        App.Etap = "tło okna";
        if (!trybTestowy)
        {
            try
            {
                SystemBackdrop = new MicaBackdrop();
            }
            catch (Exception e)
            {
                App.Zapisz(e); // bez Mica okno ma zwykłe tło
            }
        }
        else
            Korzen.Background = (Brush)Application.Current.Resources["SolidBackgroundFillColorBaseBrush"];

        var ikona = Path.Combine(AppContext.BaseDirectory, "PodpiszPdf.ico");
        if (File.Exists(ikona))
        {
            AppWindow.SetIcon(ikona);
            IkonaTytulu.Source = new BitmapImage(new Uri(ikona));
        }

        var skala = (double)Win32Dpi(Hwnd) / 96;
        AppWindow.Resize(new Windows.Graphics.SizeInt32((int)(900 * skala), (int)(720 * skala)));
        if (AppWindow.Presenter is OverlappedPresenter prezenter)
        {
            prezenter.PreferredMinimumWidth = (int)(720 * skala);
            prezenter.PreferredMinimumHeight = (int)(600 * skala);
        }
        Wysrodkuj();

        AppWindow.Changed += PrzyZmianieOkna;
        AppWindow.Closing += (_, e) =>
        {
            if (!_zamykanie)
            {
                e.Cancel = true;
                ZamknijNaZadanie();
            }
        };

        App.Etap = "ikona w zasobniku";
        if (!trybTestowy && (Ustawienia.MinimalizujDoZasobnika || startWZasobniku))
        {
            try
            {
                UtworzIkoneZasobnika();
            }
            catch (Exception e)
            {
                App.Zapisz(e); // program działa dalej, tylko bez ikony obok zegara
                _zasobnik = null;
            }
        }

        App.Etap = "strona główna (XAML)";
        Ramka.Navigate(typeof(StronaGlowna));
        App.Etap = "po utworzeniu okna";
    }

    internal IntPtr Hwnd { get; }

    private static string TytulProgramu => $"PodpiszPdf {Instalacja.BiezacaWersja.ToString(3)}";

    /// <summary>Dopisek w pasku tytułu (np. nazwa podpisywanego pliku); null — sam tytuł programu.</summary>
    internal void UstawPodtytul(string? podtytul)
    {
        TytulOkna.Text = podtytul == null ? TytulProgramu : $"{TytulProgramu}  ·  {podtytul}";
        Title = podtytul == null ? TytulProgramu : $"{podtytul} — PodpiszPdf";
    }

    private Windows.Graphics.RectInt32? _rozmiarPrzedPodgladem;

    /// <summary>Podgląd stron potrzebuje dużego okna — powiększamy je na czas wskazywania miejsca podpisu.</summary>
    internal void PowiekszDoPodgladu()
    {
        if (AppWindow.Presenter is not OverlappedPresenter { State: OverlappedPresenterState.Restored })
            return;
        var obszar = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        var szer = Math.Min(obszar.Width, (int)(obszar.Height * 0.95));
        var wys = (int)(obszar.Height * 0.9);
        var r = new Windows.Graphics.RectInt32(AppWindow.Position.X, AppWindow.Position.Y, AppWindow.Size.Width, AppWindow.Size.Height);
        if (r.Width >= szer && r.Height >= wys)
            return;
        _rozmiarPrzedPodgladem = r;
        szer = Math.Max(szer, r.Width);
        wys = Math.Max(wys, r.Height);
        AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(obszar.X + (obszar.Width - szer) / 2, obszar.Y + (obszar.Height - wys) / 2, szer, wys));
    }

    internal void PrzywrocRozmiarPoPodgladzie()
    {
        if (_rozmiarPrzedPodgladem is { } r && AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Restored })
            AppWindow.MoveAndResize(r);
        _rozmiarPrzedPodgladem = null;
    }

    internal Ustawienia Ustawienia { get; set; }

    internal bool TrybTestowy => _trybTestowy;

    /// <summary>
    /// Zapis bieżących ustawień. Wybór „po otwarciu pliku PDF” jest najpierw odczytywany z pliku — mógł go
    /// zmienić osobny proces (okno „Podpisz / Przeglądaj”), a ta kopia nie może go nadpisać starą wartością.
    /// </summary>
    internal void ZapiszUstawienia()
    {
        if (_trybTestowy)
            return;
        OdswiezPoOtwarciuPdf();
        Ustawienia.Zapisz();
    }

    /// <summary>Wybór „po otwarciu pliku PDF” z pliku ustawień (zmieniany także przez osobny proces).</summary>
    internal void OdswiezPoOtwarciuPdf()
    {
        if (!_trybTestowy && Ustawienia.OdczytajPoOtwarciuPdf() is { } zPliku)
            Ustawienia.PoOtwarciuPdf = zPliku;
    }

    internal StronaGlowna? StronaGlowna => _stronaGlowna ??= Ramka.Content as StronaGlowna;
    private StronaGlowna? _stronaGlowna;

    internal Frame Ramka_ => Ramka;

    internal Grid Korzen_ => Korzen;

    /// <summary>Start: certyfikaty, kopia programu w AppData, plik z menu kontekstowego.</summary>
    internal void Uruchom(string? plikStartowy)
    {
        if (!string.IsNullOrEmpty(plikStartowy))
            _zlecenia.Enqueue(plikStartowy);
        StronaGlowna?.Inicjalizuj();
    }

    /// <summary>Kolejne pliki do podpisu (z menu kontekstowego) — po zakończeniu bieżącej pracy.</summary>
    internal bool PobierzZlecenie(out string plik) => _zlecenia.TryDequeue(out plik!);

    /// <summary>
    /// Polecenie od kolejnego uruchomienia programu (np. z menu kontekstowego pliku PDF albo nowszej wersji).
    /// Wywoływane w wątku tła; zwraca odpowiedź dla tamtej kopii.
    /// </summary>
    internal string PrzyjmijPolecenie(string polecenie)
    {
        if (polecenie == JednaInstancja.Wersja)
            return Instalacja.BiezacaWersja.ToString();

        if (polecenie == JednaInstancja.Zakoncz)
        {
            var wynik = new TaskCompletionSource<bool>();
            _kolejkaUi.TryEnqueue(() =>
            {
                var zajety = StronaGlowna == null || StronaGlowna.Zajeta || Ramka.Content is not PodpiszPdf.StronaGlowna;
                if (!zajety)
                    ZamknijProgram();
                wynik.TrySetResult(zajety);
            });
            return wynik.Task.Wait(5000) && !wynik.Task.Result ? JednaInstancja.Ok : JednaInstancja.Zajety;
        }

        _zlecenia.Enqueue(polecenie.StartsWith(JednaInstancja.PrefiksPliku, StringComparison.Ordinal)
            ? polecenie[JednaInstancja.PrefiksPliku.Length..]
            : "");
        _kolejkaUi.TryEnqueue(() =>
        {
            // Okno pokazuje się od razu — także gdy otwarte są ustawienia, podgląd albo trwa wczytywanie
            // certyfikatów (plik poczeka wtedy w kolejce na powrót do strony głównej).
            Przywroc();
            StronaGlowna?.PrzetworzZlecenia();
        });
        return JednaInstancja.Ok;
    }

    // ---- Okno i zasobnik ----

    internal void Przywroc()
    {
        AppWindow.Show(true);
        if (AppWindow.Presenter is OverlappedPresenter p && p.State == OverlappedPresenterState.Minimized)
            p.Restore();
        Activate();
        Win32.SetForegroundWindow(Hwnd);
    }

    /// <summary>Minimalizacja okna — przy włączonym zasobniku okno chowa się do niego (PrzyZmianieOkna).</summary>
    internal void Minimalizuj()
    {
        _pokazanoDymek = true; // zamiast ogólnego dymka „działa w tle” — powiadomienie o podpisie
        if (AppWindow.Presenter is OverlappedPresenter p)
            p.Minimize();
    }

    private void PrzyZmianieOkna(AppWindow okno, AppWindowChangedEventArgs e)
    {
        if (!e.DidPresenterChange && !e.DidSizeChange)
            return;
        if (okno.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } && Ustawienia.MinimalizujDoZasobnika && _zasobnik != null)
        {
            okno.Hide();
            if (!_pokazanoDymek)
            {
                _pokazanoDymek = true;
                try
                {
                    _zasobnik.ShowNotification("PodpiszPdf działa w tle",
                        "Kliknij dwukrotnie ikonę, aby otworzyć okno. Pliki PDF można podpisywać z menu pod prawym przyciskiem myszy.");
                }
                catch (Exception)
                {
                    // Powiadomienia mogą być wyłączone w Windows.
                }
            }
        }
    }

    private void UtworzIkoneZasobnika()
    {
        var menu = new MenuFlyout();
        menu.Items.Add(new MenuFlyoutItem { Text = "Otwórz PodpiszPdf", Command = new Polecenie(Przywroc) });
        menu.Items.Add(new MenuFlyoutItem
        {
            Text = "Podpisz plik PDF…",
            Command = new Polecenie(() =>
            {
                Przywroc();
                StronaGlowna?.WybierzIPodpisz();
            }),
        });
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(new MenuFlyoutItem { Text = "Zakończ", Command = new Polecenie(ZamknijNaZadanie) });

        _zasobnik = new TaskbarIcon
        {
            ToolTipText = "PodpiszPdf",
            ContextFlyout = menu,
            ContextMenuMode = ContextMenuMode.PopupMenu,
            DoubleClickCommand = new Polecenie(Przywroc),
            NoLeftClickDelay = true,
        };
        var ikona = Path.Combine(AppContext.BaseDirectory, "PodpiszPdf.ico");
        if (File.Exists(ikona))
            _zasobnik.IconSource = new BitmapImage(new Uri(ikona));
        _zasobnik.ForceCreate(enablesEfficiencyMode: false);
    }

    /// <summary>Czy jest ikona w zasobniku, do której chowa się zminimalizowane okno.</summary>
    internal bool MaZasobnik => _zasobnik != null && Ustawienia.MinimalizujDoZasobnika;

    /// <summary>
    /// Powiadomienie z ikony w zasobniku, gdy okno jest ukryte. <paramref name="wymus"/> — bez sprawdzania,
    /// czy okno jest widoczne (zaraz po minimalizacji okno może jeszcze nie być schowane).
    /// </summary>
    internal void Powiadom(string tytul, string tresc, bool wymus = false)
    {
        if (_zasobnik == null || (!wymus && AppWindow.IsVisible))
            return;
        try
        {
            _zasobnik.ShowNotification(tytul, tresc);
        }
        catch (Exception)
        {
            // Powiadomienia mogą być wyłączone w Windows.
        }
    }

    /// <summary>Ikona w zasobniku zgodnie z ustawieniami (po ich zmianie).</summary>
    internal void OdswiezZasobnik()
    {
        if (_trybTestowy)
            return;
        if (Ustawienia.MinimalizujDoZasobnika && _zasobnik == null)
            UtworzIkoneZasobnika();
        else if (!Ustawienia.MinimalizujDoZasobnika && _zasobnik != null)
        {
            _zasobnik.Dispose();
            _zasobnik = null;
        }
    }

    /// <summary>
    /// Zamknięcie na żądanie użytkownika (X w oknie, „Zakończ” w zasobniku). W trakcie podpisu lub pobierania
    /// aktualizacji program się nie zamyka; gdy podpis czeka na decyzję (podgląd, pytanie) — pyta.
    /// </summary>
    internal async void ZamknijNaZadanie()
    {
        if (_zamykanie || _pytanieOZamkniecie)
            return;
        var strona = StronaGlowna;
        if (strona == null || !strona.Zajeta)
        {
            ZamknijProgram();
            return;
        }
        _pytanieOZamkniecie = true;
        try
        {
            Przywroc();
            if (strona.EtapKrytyczny)
            {
                await Dialogi.PokazAsync(Korzen.XamlRoot, "Trwa praca programu",
                    "Trwa podpisywanie dokumentu albo pobieranie aktualizacji. Poczekaj na zakończenie, a potem zamknij program.");
                return;
            }
            var odpowiedz = await Dialogi.PokazAsync(Korzen.XamlRoot, "Zamknąć PodpiszPdf?",
                "Podpisywanie nie zostało dokończone — dokument nie zostanie podpisany. Zamknąć program?",
                "Anuluj", "Zamknij", domyslnieGlowny: false);
            if (odpowiedz == ContentDialogResult.Primary && !strona.EtapKrytyczny)
                ZamknijProgram();
        }
        catch (Exception e)
        {
            App.Zapisz(e);
        }
        finally
        {
            _pytanieOZamkniecie = false;
        }
    }

    internal void ZamknijProgram()
    {
        if (_zamykanie)
            return;
        _zamykanie = true;
        _zasobnik?.Dispose();
        _zasobnik = null;
        Close();
        Application.Current.Exit();
    }

    private void Wysrodkuj()
    {
        var obszar = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        var r = AppWindow.Size;
        AppWindow.Move(new Windows.Graphics.PointInt32(obszar.X + (obszar.Width - r.Width) / 2, obszar.Y + (obszar.Height - r.Height) / 2));
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetDpiForWindow")]
    private static extern uint Win32Dpi(IntPtr hwnd);

    /// <summary>Proste polecenie dla menu i ikony w zasobniku.</summary>
    private sealed class Polecenie(Action akcja) : ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => akcja();
    }
}
