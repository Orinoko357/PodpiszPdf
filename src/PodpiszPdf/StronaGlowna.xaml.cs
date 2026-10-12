using System.Diagnostics;
using System.Security.Cryptography;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using PodpiszPdf.Core;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace PodpiszPdf;

/// <summary>Certyfikat na liście (dane do wyświetlenia).</summary>
public sealed partial class ElementCertyfikatu(CertyfikatInfo certyfikat)
{
    public CertyfikatInfo Certyfikat { get; } = certyfikat;
    public string Nazwa => Certyfikat.ImieNazwisko;
    public string Opis => $"{Certyfikat.Wystawca}  ·  ważny do {Certyfikat.WaznyDo:dd.MM.yyyy}";
    public Visibility WidocznyKwalifikowany => Certyfikat.Kwalifikowany ? Visibility.Visible : Visibility.Collapsed;
    public Visibility WidocznyNiekwalifikowany => Certyfikat.Kwalifikowany ? Visibility.Collapsed : Visibility.Visible;
    public override string ToString() => $"{Nazwa}, {(Certyfikat.Kwalifikowany ? "kwalifikowany" : "niekwalifikowany")}, {Certyfikat.Wystawca}";
}

/// <summary>Strona główna: dokument, certyfikat i przycisk podpisu.</summary>
public sealed partial class StronaGlowna : Page
{
    private string? _plik;
    private bool _trwaPodpisywanie;
    // Przebieg podpisu od pierwszego kliknięcia (wybór pliku, pytania, podgląd) do końca — jeden naraz.
    private bool _przebiegPodpisu;
    // Właściwy podpis albo pobieranie i instalacja aktualizacji — tego nie wolno przerwać zamknięciem programu.
    private bool _etapKrytyczny;
    private bool _wczytywanieCertyfikatow;
    private bool _zainicjalizowano;
    private bool _gotowa;
    private int _numerMiniatury;

    public StronaGlowna()
    {
        InitializeComponent();
        UstawPlik(null);
        OdswiezInformacje();
    }

    private static OknoGlowne Okno => App.Okno!;

    private Ustawienia Ustawienia => Okno.Ustawienia;

    /// <summary>Trwa praca, której nie wolno przerwać (podpisywanie z wyborem pliku i pytaniami, podgląd, aktualizacja).</summary>
    internal bool Zajeta => _trwaPodpisywanie || _przebiegPodpisu;

    /// <summary>Trwa sam podpis albo pobieranie/instalacja aktualizacji.</summary>
    internal bool EtapKrytyczny => _etapKrytyczny;

    private CertyfikatInfo? WybranyCertyfikat => (ListaCertyfikatow.SelectedItem as ElementCertyfikatu)?.Certyfikat;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        // Pliki z menu kontekstowego, które przyszły w czasie pracy w ustawieniach — po zakończeniu nawigacji.
        if (e.NavigationMode == NavigationMode.Back)
            DispatcherQueue.TryEnqueue(PrzetworzZlecenia);
    }

    /// <summary>
    /// Start programu. Wolniejsze czynności (certyfikaty, kopia programu, biblioteki PDF) wykonują się w tle,
    /// więc okno jest od razu gotowe.
    /// </summary>
    internal async void Inicjalizuj()
    {
        if (_zainicjalizowano)
            return;
        _zainicjalizowano = true;

        var ustawienia = Ustawienia;
        var instalacja = Task.Run(async () =>
        {
            var komunikat = OdswiezInstalacje(ustawienia);
            OdswiezMenuKontekstowe(ustawienia);
            AktualizacjaProgramu.UsunPobraneInstalatory();
            try
            {
                // Program przeniesiony lub zaktualizowany: wpisy w rejestrze i pakiet muszą wskazywać na ten plik.
                if (DomyslnaAplikacja.JestZarejestrowana && !DomyslnaAplikacja.JestAktualna)
                    DomyslnaAplikacja.Zarejestruj(ustawienia);
                await MenuWindows11.OdswiezAsync(ustawienia);
            }
            catch (Exception e)
            {
                App.Zapisz(e); // nie przerywa startu — szczegóły w %TEMP%\PodpiszPdf-blad.txt
            }
            return komunikat;
        });

        await WczytajCertyfikaty();
        _gotowa = true;
        PrzetworzZlecenia();

        var komunikat = await instalacja;
        if (Program.PoAktualizacji)
            komunikat = PokazWynikAktualizacji(Program.KodAktualizacji);
        if (komunikat != null && !Zajeta)
            UstawStatus(komunikat);

        // Przygotowanie bibliotek PDF w tle — pierwszy podgląd i podpis będą szybsze.
        _ = Task.Run(() =>
        {
            Thread.CurrentThread.Priority = ThreadPriority.BelowNormal;
            Rozgrzewka.Wykonaj();
        });

        // Aktualizacje: przy starcie (najwyżej raz na dobę) i co kilka godzin, gdy program czeka w zasobniku.
        await SprawdzAktualizacjeAutomatycznie();
        var zegar = DispatcherQueue.CreateTimer();
        zegar.Interval = TimeSpan.FromHours(6);
        zegar.Tick += async (_, _) => await SprawdzAktualizacjeAutomatycznie();
        zegar.Start();
    }

    // ---- Aktualizacje ----

    private DostepnaAktualizacja? _aktualizacja;

    /// <summary>
    /// Program uruchomiony ponownie po instalatorze aktualizacji: komunikat zależny od kodu msiexec
    /// (null — starsza wersja przekazywała sam parametr po udanej instalacji). Zwraca tekst paska stanu.
    /// </summary>
    private string PokazWynikAktualizacji(int? kod)
    {
        switch (kod)
        {
            case null or 0 or 1641:
                return $"Zaktualizowano PodpiszPdf do wersji {Instalacja.BiezacaWersja}.";
            case 3010:
                return $"Zaktualizowano PodpiszPdf do wersji {Instalacja.BiezacaWersja}. Windows zaleca ponowne uruchomienie komputera.";
            case 1602:
                _ = Dialogi.PokazAsync(XamlRoot, "Aktualizacja anulowana",
                    $"Aktualizacja została anulowana. Działa dotychczasowa wersja {Instalacja.BiezacaWersja}.");
                return "Aktualizacja anulowana.";
            default:
                _ = Dialogi.PokazAsync(XamlRoot, "Aktualizacja nie powiodła się",
                    $"Aktualizacja nie powiodła się (kod {kod}). Działa dotychczasowa wersja {Instalacja.BiezacaWersja}.\n\n" +
                    "Instalacja wymaga uprawnień administratora komputera. " +
                    $"Nową wersję można też pobrać ręcznie: {Aktualizacje.StronaWydan}");
                return $"Aktualizacja nie powiodła się (kod {kod}).";
        }
    }

    private async Task SprawdzAktualizacjeAutomatycznie()
    {
        var u = Ustawienia;
        if (!u.SprawdzajAktualizacje || Okno.TrybTestowy)
            return;
        if (u.OstatnieSprawdzenieAktualizacji is { } ostatnie && DateTimeOffset.Now - ostatnie < TimeSpan.FromHours(20))
            return;
        try
        {
            var aktualizacja = await AktualizacjaProgramu.SprawdzAsync();
            // Bieżący obiekt ustawień — w czasie sprawdzania mógł zostać zastąpiony (zapis na stronie ustawień).
            Ustawienia.OstatnieSprawdzenieAktualizacji = DateTimeOffset.Now;
            ZapiszCicho();
            if (aktualizacja != null)
            {
                PokazAktualizacje(aktualizacja);
                Okno.Powiadom($"Dostępna nowa wersja PodpiszPdf {aktualizacja.Wersja}",
                    "Otwórz program, aby zaktualizować go jednym kliknięciem.");
            }
        }
        catch (Exception)
        {
            // Brak sieci lub serwera — spróbujemy przy następnej okazji.
        }
    }

    /// <summary>Pasek z informacją o nowej wersji i przyciskiem aktualizacji.</summary>
    internal void PokazAktualizacje(DostepnaAktualizacja aktualizacja)
    {
        _aktualizacja = aktualizacja;
        Aktualizacja.Title = $"Dostępna nowa wersja {aktualizacja.Wersja}";
        Aktualizacja.Message = $"Masz wersję {Instalacja.BiezacaWersja}. Aktualizacja zajmie chwilę, program uruchomi się ponownie.";
        PrzyciskAktualizacji.IsEnabled = true;
        Aktualizacja.IsOpen = true;
    }

    private void PrzyAktualizacji(object sender, RoutedEventArgs e)
    {
        if (_aktualizacja != null)
            ZainstalujAktualizacje(_aktualizacja);
    }

    /// <summary>Pobiera i instaluje nową wersję, potem uruchamia ją w miejsce tej kopii.</summary>
    internal async void ZainstalujAktualizacje(DostepnaAktualizacja aktualizacja)
    {
        if (Zajeta)
        {
            await Dialogi.PokazAsync(XamlRoot, "Trwa podpisywanie", "Dokończ podpisywanie i spróbuj ponownie.");
            return;
        }
        UstawTrwaPodpisywanie(true);
        _etapKrytyczny = true;
        PrzyciskAktualizacji.IsEnabled = false;
        try
        {
            var postep = new Progress<double>(p => UstawStatus($"Pobieranie wersji {aktualizacja.Wersja}… {p:P0}"));
            UstawStatus($"Pobieranie wersji {aktualizacja.Wersja}…");
            var instalator = await AktualizacjaProgramu.PobierzAsync(aktualizacja, postep);
            UstawStatus("Instalowanie nowej wersji… Windows poprosi o zgodę administratora.");
            await AktualizacjaProgramu.ZainstalujIUruchomAsync(instalator, aktualizacja.Sha256);
            Okno.ZamknijProgram();
        }
        catch (Exception ex)
        {
            _etapKrytyczny = false;
            UstawTrwaPodpisywanie(false);
            PrzyciskAktualizacji.IsEnabled = true;
            UstawStatus("Nie udało się zaktualizować programu.");
            await Dialogi.BladAsync(XamlRoot, "Nie udało się zaktualizować programu.\n\n" + KomunikatyBledow.Opisz(ex) +
                $"\n\nNową wersję można też pobrać ręcznie: {Aktualizacje.StronaWydan}");
        }
    }

    /// <summary>Zrzuty ekranu i test uruchomienia: przykładowy plik i certyfikaty zamiast prawdziwych.</summary>
    internal void PokazDanePrzykladowe(string? plik, IReadOnlyList<CertyfikatInfo> certyfikaty)
    {
        _zainicjalizowano = true;
        _gotowa = true;
        UstawCertyfikaty(certyfikaty, null);
        UstawPlik(plik);
        UstawStatus("Gotowy. Program zapamięta wybrany certyfikat.");
    }

    // ---- Kolejka plików (menu kontekstowe, zasobnik) ----

    internal void PrzetworzZlecenia()
    {
        // Pliki, które przyszły przed wczytaniem certyfikatów, czekają w kolejce (Inicjalizuj wywoła to ponownie).
        if (!_gotowa || Zajeta || !ReferenceEquals(Okno.Ramka_.Content, this) || !Okno.PobierzZlecenie(out var plik))
            return;
        Okno.Przywroc();
        if (string.IsNullOrEmpty(plik))
        {
            PrzetworzZlecenia();
            return;
        }
        UstawPlik(plik);
        if (_plik != null)
            Podpisz();
        else
            PrzetworzZlecenia();
    }

    internal void WybierzIPodpisz()
    {
        if (Zajeta || !ReferenceEquals(Okno.Ramka_.Content, this))
            return;
        Podpisz(wybierzPlik: true);
    }

    // ---- Instalacja i menu kontekstowe ----

    /// <summary>
    /// Sprzątanie po wersjach sprzed instalatora MSI i autostart wskazujący na zainstalowany program.
    /// Wywoływane w tle; zwraca komunikat do paska stanu albo null.
    /// </summary>
    private static string? OdswiezInstalacje(Ustawienia ustawienia)
    {
        try
        {
            var komunikat = Instalacja.PosprzatajStaraKopie();
            if (ustawienia.Autostart)
                Instalacja.UstawAutostart(true);
            return komunikat;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return "Nie udało się uporządkować instalacji: " + e.Message;
        }
    }

    /// <summary>Zwraca opis błędu albo null.</summary>
    private static string? OdswiezMenuKontekstowe(Ustawienia ustawienia)
    {
        try
        {
            if (ustawienia.MenuKontekstowe)
            {
                if (!MenuKontekstowe.JestAktualne())
                    MenuKontekstowe.Zarejestruj();
            }
            else
            {
                MenuKontekstowe.Wyrejestruj();
            }
            return null;
        }
        catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            return e.Message;
        }
    }

    /// <summary>
    /// Nowe ustawienia zatwierdzone na stronie ustawień — zapisywane od razu (przed powrotem do strony głównej).
    /// Zwraca opisy błędów (pusta lista — wszystko się udało).
    /// </summary>
    internal List<string> PoZmianieUstawien(Ustawienia nowe)
    {
        nowe.OstatniCertyfikat = Ustawienia.OstatniCertyfikat;
        nowe.OstatnieSprawdzenieAktualizacji = Ustawienia.OstatnieSprawdzenieAktualizacji;
        nowe.PoprzedniaAplikacjaPdf = Ustawienia.PoprzedniaAplikacjaPdf;
        nowe.PozycjaMenuWindows11 = Ustawienia.PozycjaMenuWindows11;
        Okno.Ustawienia = nowe;
        OdswiezInformacje();
        Okno.OdswiezZasobnik();
        var bledy = new List<string>();
        if (Okno.TrybTestowy)
            return bledy;

        try
        {
            nowe.Zapisz();
        }
        catch (Exception e)
        {
            bledy.Add("Nie udało się zapisać ustawień.\n" + KomunikatyBledow.Opisz(e));
        }
        try
        {
            Instalacja.UstawAutostart(nowe.Autostart);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            bledy.Add("Nie udało się zmienić autostartu.\n" + e.Message);
        }
        if (OdswiezMenuKontekstowe(nowe) is { } blad)
            bledy.Add("Nie udało się zmienić menu kontekstowego plików PDF.\n" + blad);
        return bledy;
    }

    // ---- Certyfikaty ----

    /// <summary>Lista certyfikatów — odczyt magazynu (i sterowników kart) w tle, bez blokowania okna.</summary>
    private async Task WczytajCertyfikaty()
    {
        if (_wczytywanieCertyfikatow)
            return;
        _wczytywanieCertyfikatow = true;
        var poprzedni = WybranyCertyfikat?.Odcisk ?? Ustawienia.OstatniCertyfikat;
        IReadOnlyList<CertyfikatInfo> certyfikaty;
        UstawStatus("Wczytywanie certyfikatów…");
        WczytywanieCertyfikatow.IsActive = true;
        BrakCertyfikatow.Visibility = Visibility.Collapsed;
        OdswiezStan();
        try
        {
            certyfikaty = await Task.Run(MagazynCertyfikatow.PobierzCertyfikatyDoPodpisu);
        }
        catch (Exception e)
        {
            await Dialogi.BladAsync(XamlRoot, "Nie udało się odczytać certyfikatów z magazynu Windows.\n\n" + KomunikatyBledow.Opisz(e));
            certyfikaty = Array.Empty<CertyfikatInfo>();
        }
        finally
        {
            _wczytywanieCertyfikatow = false;
            WczytywanieCertyfikatow.IsActive = false;
        }

        UstawCertyfikaty(certyfikaty, poprzedni);
        UstawStatus(certyfikaty.Count == 0 ? "Brak certyfikatu do podpisu." : "Gotowy. Program zapamięta wybrany certyfikat.");
    }

    private void UstawCertyfikaty(IReadOnlyList<CertyfikatInfo> certyfikaty, string? odciskDoZaznaczenia)
    {
        var elementy = certyfikaty.Select(c => new ElementCertyfikatu(c)).ToList();
        ListaCertyfikatow.ItemsSource = elementy;
        ListaCertyfikatow.SelectedItem =
            elementy.FirstOrDefault(e => string.Equals(e.Certyfikat.Odcisk, odciskDoZaznaczenia, StringComparison.OrdinalIgnoreCase))
            ?? elementy.FirstOrDefault();
        BrakCertyfikatow.Visibility = elementy.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        OdswiezStan();
    }

    private async void PrzyOdswiezeniu(object sender, RoutedEventArgs e) => await WczytajCertyfikaty();

    private void PrzyWyborzeCertyfikatu(object sender, SelectionChangedEventArgs e) => OdswiezStan();

    private void PrzyDwukliku(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (WybranyCertyfikat != null && _plik != null)
            Podpisz();
    }

    // ---- Dokument ----

    /// <summary>Okno wyboru pliku; zwraca true, gdy wybrano istniejący plik.</summary>
    private async Task<bool> WybierzPlik()
    {
        string? plik;
        try
        {
            plik = await Dialogi.WybierzPlikAsync(Okno.Hwnd, ".pdf");
        }
        catch (Exception e)
        {
            await Dialogi.BladAsync(XamlRoot, "Nie udało się otworzyć okna wyboru pliku.\n\n" + e.Message);
            return false;
        }
        if (plik == null)
            return false;
        UstawPlik(plik);
        return _plik != null;
    }

    private async void PrzyWyborzePliku(object sender, RoutedEventArgs e)
    {
        if (Zajeta)
            return;
        // Wybór pliku też blokuje podpis — plik z menu kontekstowego poczeka w kolejce.
        _przebiegPodpisu = true;
        OdswiezStan();
        try
        {
            await WybierzPlik();
        }
        finally
        {
            _przebiegPodpisu = false;
            OdswiezStan();
            DispatcherQueue.TryEnqueue(PrzetworzZlecenia);
        }
    }

    private void PrzyUsunieciuPliku(object sender, RoutedEventArgs e) => UstawPlik(null);

    private void UstawPlik(string? sciezka)
    {
        if (sciezka != null && !File.Exists(sciezka))
        {
            _ = Dialogi.BladAsync(XamlRoot, $"Nie znaleziono pliku:\n{sciezka}");
            sciezka = null;
        }
        _plik = sciezka;
        BezPliku.Visibility = _plik == null ? Visibility.Visible : Visibility.Collapsed;
        ZPlikiem.Visibility = _plik != null ? Visibility.Visible : Visibility.Collapsed;
        Miniatura.Source = null;
        if (_plik != null)
        {
            NazwaPliku.Text = Path.GetFileName(_plik);
            var rozmiar = Obrazy.Rozmiar(new FileInfo(_plik).Length);
            var folder = Path.GetDirectoryName(_plik);
            OpisPliku.Text = $"{rozmiar}  ·  {folder}";
            ToolTipService.SetToolTip(NazwaPliku, _plik);
            _ = WczytajMiniature(_plik, rozmiar, folder);
        }
        OdswiezStan();
    }

    /// <summary>Miniatura pierwszej strony i liczba stron — w tle, żeby okno nie czekało.</summary>
    private async Task WczytajMiniature(string plik, string rozmiar, string? folder)
    {
        var numer = ++_numerMiniatury;
        try
        {
            var pikseli = 70 * (XamlRoot?.RasterizationScale ?? 1.5) * 1.5;
            var (obraz, strony) = await Task.Run(() =>
            {
                var s = DokumentPdf.Strony(plik);
                var skala = pikseli / Math.Max(1, s[0].Wysokosc);
                return (DokumentPdf.Renderuj(File.ReadAllBytes(plik), 0, skala), s.Count);
            });
            if (numer != _numerMiniatury || _plik != plik)
                return;
            Miniatura.Source = Obrazy.NaObraz(obraz);
            OpisPliku.Text = $"{Obrazy.LiczbaStron(strony)}  ·  {rozmiar}  ·  {folder}";
        }
        catch (Exception)
        {
            // Miniatura jest tylko pomocą — błąd pliku wyjdzie przy podglądzie.
        }
    }

    private void PrzyPrzeciaganiu(object sender, DragEventArgs e)
    {
        if (Zajeta || !e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.None;
            return;
        }
        e.AcceptedOperation = DataPackageOperation.Copy;
        e.DragUIOverride.Caption = "Wybierz dokument";
    }

    private async void PrzyUpuszczeniu(object sender, DragEventArgs e)
    {
        if (Zajeta || !e.DataView.Contains(StandardDataFormats.StorageItems))
            return;
        try
        {
            var elementy = await e.DataView.GetStorageItemsAsync();
            var pdf = elementy.OfType<StorageFile>()
                .FirstOrDefault(f => string.Equals(Path.GetExtension(f.Path), ".pdf", StringComparison.OrdinalIgnoreCase));
            if (Zajeta)
                return; // w czasie odczytu zaczął się podpis pliku z menu kontekstowego
            if (pdf != null)
                UstawPlik(pdf.Path);
            else
                await Dialogi.BladAsync(XamlRoot, "Przeciągnij plik PDF.");
        }
        catch (Exception ex)
        {
            await Dialogi.BladAsync(XamlRoot, "Nie udało się odczytać przeciągniętego pliku.\n\n" + ex.Message);
        }
    }

    // ---- Ustawienia ----

    private void PrzyUstawieniach(object sender, RoutedEventArgs e) => OtworzUstawienia();

    /// <summary>Strona ustawień (przycisk albo menu ikony w zasobniku) — gdy nic nie jest w toku.</summary>
    internal void OtworzUstawienia()
    {
        if (Zajeta || !ReferenceEquals(Okno.Ramka_.Content, this))
            return;
        Okno.OdswiezPoOtwarciuPdf(); // mógł go zmienić osobny proces („Zapamiętaj mój wybór”)
        Okno.Ramka_.Navigate(typeof(StronaUstawien), Ustawienia.Kopia(), new DrillInNavigationTransitionInfo());
    }

    /// <summary>Opis tego, co się stanie przy podpisie: format, serwer znacznika czasu, wizualizacja.</summary>
    private void OdswiezInformacje()
    {
        if (App.Okno == null)
            return;
        var u = Ustawienia;
        var serwer = Uri.TryCreate(u.AdresTsa, UriKind.Absolute, out var uri) ? uri.Host : u.AdresTsa;
        Informacja.Title = $"PAdES {(u.PoziomLT ? "B-LT" : "B-T")}";
        Informacja.Message = $"Znacznik czasu: {serwer}  ·  wizualizacja Arial, imię i nazwisko {Wizualizacja.RozmiarImieniaINazwiska:0.#} pt" +
                             (string.IsNullOrEmpty(u.SciezkaLogo) ? "" : " z logo");
    }

    // ---- Podpis ----

    private void PrzyPodpisie(object sender, RoutedEventArgs e) => Podpisz();

    /// <summary>
    /// Cały przebieg podpisu. Blokada obejmuje go od początku (wybór pliku, pytania, podgląd) — pliki z menu
    /// kontekstowego, które przyjdą w tym czasie, czekają w kolejce.
    /// </summary>
    /// <param name="wybierzPlik">Najpierw okno wyboru pliku (menu ikony w zasobniku).</param>
    private async void Podpisz(bool wybierzPlik = false)
    {
        if (Zajeta)
            return;
        _przebiegPodpisu = true;
        OdswiezStan();
        try
        {
            await PodpiszAsync(wybierzPlik);
        }
        catch (Exception e)
        {
            UstawStatus("Nie podpisano dokumentu.");
            await Dialogi.BladAsync(XamlRoot, KomunikatyBledow.Opisz(e), "Nie podpisano dokumentu");
        }
        finally
        {
            _etapKrytyczny = false;
            _przebiegPodpisu = false;
            UstawTrwaPodpisywanie(false);
            DispatcherQueue.TryEnqueue(PrzetworzZlecenia); // kolejne pliki z menu kontekstowego
        }
    }

    private async Task PodpiszAsync(bool wybierzPlik)
    {
        if ((wybierzPlik || _plik == null) && !await WybierzPlik())
            return;
        if (_plik == null)
            return;
        var certyfikat = WybranyCertyfikat;
        if (certyfikat == null)
        {
            await Dialogi.BladAsync(XamlRoot, "Wybierz certyfikat z listy. Jeśli lista jest pusta, włóż kartę i kliknij „Odśwież”.");
            return;
        }

        // Program działa tygodniami w zasobniku — ważność sprawdzamy w chwili podpisu, nie tylko przy budowie listy.
        var teraz = DateTime.Now;
        if (teraz < certyfikat.Certyfikat.NotBefore || teraz > certyfikat.Certyfikat.NotAfter)
        {
            await Dialogi.BladAsync(XamlRoot,
                $"Certyfikat {certyfikat.ImieNazwisko} ({certyfikat.Wystawca}) nie jest teraz ważny " +
                $"(ważny od {certyfikat.Certyfikat.NotBefore:dd.MM.yyyy} do {certyfikat.Certyfikat.NotAfter:dd.MM.yyyy}).\n\n" +
                "Lista certyfikatów zostanie odświeżona.", "Certyfikat nieważny");
            await WczytajCertyfikaty();
            return;
        }
        if (!certyfikat.Kwalifikowany)
        {
            var zgoda = await Dialogi.PokazAsync(XamlRoot, "Certyfikat niekwalifikowany",
                "Ten certyfikat nie jest kwalifikowany — podpis nie będzie kwalifikowanym podpisem elektronicznym. Kontynuować?",
                "Anuluj", "Kontynuuj", domyslnieGlowny: false);
            if (zgoda != ContentDialogResult.Primary)
                return;
        }

        var wejscie = _plik;
        var ustawienia = Ustawienia;
        var wyjscie = ustawienia.PlikWynikowy(wejscie);
        if (!string.Equals(Path.GetFullPath(wyjscie), Path.GetFullPath(wejscie), StringComparison.OrdinalIgnoreCase) && File.Exists(wyjscie))
        {
            var odpowiedz = await Dialogi.PokazAsync(XamlRoot, "Plik już istnieje",
                $"Plik {Path.GetFileName(wyjscie)} już istnieje. Zastąpić go?", "Anuluj", "Zastąp", domyslnieGlowny: false);
            if (odpowiedz != ContentDialogResult.Primary)
                return;
        }

        var hwnd = Okno.Hwnd;
        var opcje = ustawienia.UtworzOpcjePodpisu();
        opcje.PrzygotujKlucz = klucz => UstawOknoNadrzedneDlaPinu(klucz, hwnd);

        UstawTrwaPodpisywanie(true);

        // Podgląd wszystkich stron: użytkownik klika, gdzie ma być wizualizacja podpisu.
        UstawStatus("Otwieranie podglądu…");
        var dane = new DaneWizualizacji(certyfikat.ImieNazwisko, ustawienia.Stanowisko, ustawienia.SciezkaLogo, DateTimeOffset.Now,
            ustawienia.MotywWizualizacji, certyfikat.Kwalifikowany);
        var zadanie = await Task.Run(() => ZadaniePodgladu.Przygotuj(wejscie, dane, ustawienia.Polozenie, 1f)); // zawsze 11 pt na start
        UstawStatus("Wskaż na podglądzie miejsce podpisu.");
        Okno.Ramka_.Navigate(typeof(StronaPodgladu), zadanie, new DrillInNavigationTransitionInfo());
        opcje.Miejsce = await zadanie.Wynik.Task;
        if (opcje.Miejsce == null)
        {
            UstawStatus("Podpisywanie anulowane.");
            return;
        }

        UstawStatus("Podpisywanie… Sterownik karty może poprosić o PIN.");
        _etapKrytyczny = true;
        var wynik = await Task.Run(() =>
        {
            var tsa = ustawienia.UtworzKlientaTsa();
            return new PodpisywaczPdf(tsa).Podpisz(wejscie, wyjscie, certyfikat.Certyfikat, opcje);
        });
        _etapKrytyczny = false;

        Ustawienia.OstatniCertyfikat = certyfikat.Odcisk;
        ZapiszCicho();

        UstawStatus($"Podpisano: {wynik.SciezkaWyniku}");
        // Bez okienka tylko wtedy, gdy wynik pokaże dymek z ikony w zasobniku i czas pochodzi z serwera
        // (ostrzeżenie o czasie z zegara komputera musi być widoczne).
        if (Ustawienia.MinimalizujPoPodpisie && !Okno.TrybTestowy && Okno.MaZasobnik && wynik.CzasZSerwera)
        {
            Okno.Minimalizuj();
            Okno.Powiadom("Dokument został podpisany", Path.GetFileName(wynik.SciezkaWyniku), wymus: true);
            return;
        }
        var zrodloCzasu = wynik.CzasZSerwera ? "z serwera znacznika czasu" : "z zegara komputera — serwer nie odpowiedział przed podpisem";
        var decyzja = await Dialogi.PokazAsync(XamlRoot, "Dokument został podpisany",
            $"Zapisano jako:\n{wynik.SciezkaWyniku}\n\n" +
            $"Czas na wizualizacji: {wynik.CzasNaWizualizacji.ToLocalTime():yyyy-MM-dd HH:mm} ({zrodloCzasu}).\n" +
            "Wiążący czas podpisu zawiera znacznik czasu.", "Zamknij", "Pokaż w folderze");
        if (decyzja == ContentDialogResult.Primary)
            PokazWFolderze(wynik.SciezkaWyniku);
    }

    /// <summary>Zapis bieżących ustawień okna (nigdy wcześniej zapamiętanej, nieaktualnej kopii).</summary>
    private void ZapiszCicho()
    {
        try { Okno.ZapiszUstawienia(); } catch (Exception) { /* zapamiętanie wyboru nie jest krytyczne */ }
    }

    /// <summary>
    /// Wskazuje sterownikowi karty okno nadrzędne, żeby okno PIN-u pojawiło się nad aplikacją, a nie pod nią.
    /// </summary>
    private static void UstawOknoNadrzedneDlaPinu(AsymmetricAlgorithm klucz, IntPtr okno)
    {
        CngKey? cng = klucz switch
        {
            RSACng rsa => rsa.Key,
            ECDsaCng ec => ec.Key,
            _ => null,
        };
        if (cng == null)
            return;
        try
        {
            cng.SetProperty(new CngProperty("HWND Handle", BitConverter.GetBytes(okno.ToInt64()), CngPropertyOptions.None));
        }
        catch (CryptographicException)
        {
            // Nie każdy dostawca obsługuje tę właściwość — wtedy okno PIN-u i tak się pojawi.
        }
    }

    private static void PokazWFolderze(string sciezka)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{sciezka}\"") { UseShellExecute = true });
        }
        catch (Exception)
        {
            // Brak eksploratora nie jest błędem podpisu.
        }
    }

    private void UstawTrwaPodpisywanie(bool trwa)
    {
        _trwaPodpisywanie = trwa;
        Postep.IsActive = trwa;
        OdswiezStan();
    }

    private void OdswiezStan()
    {
        var zajeta = Zajeta;
        PrzyciskPodpisz.IsEnabled = !zajeta && _plik != null && WybranyCertyfikat != null;
        PrzyciskZmien.IsEnabled = !zajeta;
        PrzyciskUsun.IsEnabled = !zajeta;
        PrzyciskOdswiez.IsEnabled = !zajeta && !_wczytywanieCertyfikatow;
        PrzyciskUstawienia.IsEnabled = !zajeta;
        ListaCertyfikatow.IsEnabled = !zajeta;
    }

    private void UstawStatus(string tekst) => Status.Text = tekst;
}
