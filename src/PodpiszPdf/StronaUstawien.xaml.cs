using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using PodpiszPdf.Core;

namespace PodpiszPdf;

/// <summary>Ustawienia w układzie Ustawień Windows 11: nawigacja po lewej, karty ustawień po prawej.</summary>
public sealed partial class StronaUstawien : Page
{
    private static readonly WyborStrony[] Strony = { WyborStrony.Ostatnia, WyborStrony.Pierwsza, WyborStrony.Numer };
    private static readonly Naroznik[] Narozniki = { Naroznik.PrawyDolny, Naroznik.LewyDolny, Naroznik.PrawyGorny, Naroznik.LewyGorny };
    private static readonly string[] Tytuly = { "Wizualizacja", "Znacznik czasu", "Program", "Informacje" };

    private Ustawienia _ustawienia = new();
    private string? _sciezkaLogo;
    private int _numerPodgladu;
    private bool _zakonczono;

    public StronaUstawien()
    {
        InitializeComponent();
        KartaMenuKontekstowego.Description = $"„{PodpiszPdf.MenuKontekstowe.Etykieta}” pod prawym przyciskiem myszy";
        KartaWersji.Header = $"PodpiszPdf {Instalacja.BiezacaWersja.ToString(3)}";
        KartaFolderu.Description = Ustawienia.Folder;
        KartaAktualizacji.Description = $"Nowe wersje z {Aktualizacje.StronaWydan}";
        var ikona = Path.Combine(AppContext.BaseDirectory, "PodpiszPdf.ico");
        if (File.Exists(ikona))
            IkonaProgramu.UriSource = new Uri(ikona);
    }

    private static OknoGlowne Okno => App.Okno!;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _ustawienia = e.Parameter as Ustawienia ?? Okno.Ustawienia.Kopia();
        Okno.UstawPodtytul("Ustawienia");
        WczytajDoPol();
        PokazSekcje(0);
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        Okno.UstawPodtytul(null);
    }

    /// <summary>Pokazuje sekcję ustawień o podanym numerze (także do zrzutów ekranu).</summary>
    internal void PokazSekcje(int indeks)
    {
        Nawigacja.SelectedItem = Nawigacja.MenuItems[indeks];
        UstawSekcje(indeks);
    }

    private void UstawSekcje(int indeks)
    {
        SekcjaWizualizacji.Visibility = indeks == 0 ? Visibility.Visible : Visibility.Collapsed;
        SekcjaZnacznika.Visibility = indeks == 1 ? Visibility.Visible : Visibility.Collapsed;
        SekcjaProgramu.Visibility = indeks == 2 ? Visibility.Visible : Visibility.Collapsed;
        SekcjaInformacji.Visibility = indeks == 3 ? Visibility.Visible : Visibility.Collapsed;
        Tytul.Text = Tytuly[indeks];
        Przewijanie.ChangeView(null, 0, null, true);
    }

    private void PrzyZmianieSekcji(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem { Tag: string tag } && int.TryParse(tag, out var indeks))
            UstawSekcje(indeks);
    }

    // ---- Dane ----

    private void WczytajDoPol()
    {
        var u = _ustawienia;
        var p = u.Polozenie;
        Stanowisko.Text = (u.Stanowisko ?? "").Replace("\n", "\r");
        _sciezkaLogo = u.SciezkaLogo;
        AdresTsa.Text = u.AdresTsa;
        LoginTsa.Text = u.LoginTsa ?? "";
        HasloTsa.Password = u.HasloTsa ?? "";
        ListaStron.SelectedIndex = Math.Max(0, Array.IndexOf(Strony, p.Strona));
        NumerStrony.Value = Math.Clamp(p.NumerStrony, 1, 9999);
        NumerStrony.IsEnabled = p.Strona == WyborStrony.Numer;
        ListaNaroznikow.SelectedIndex = Math.Max(0, Array.IndexOf(Narozniki, p.Naroznik));
        MarginesX.Value = Math.Clamp(p.MarginesPoziomyMm, 0, 500);
        MarginesY.Value = Math.Clamp(p.MarginesPionowyMm, 0, 500);
        Przyrostek.Text = u.Przyrostek;
        PoziomLT.IsOn = u.PoziomLT;
        Zasobnik.IsOn = u.MinimalizujDoZasobnika;
        MinimalizujPoPodpisie.IsOn = u.MinimalizujPoPodpisie;
        MinimalizujPoPodpisie.IsEnabled = Zasobnik.IsOn;
        Autostart.IsOn = u.Autostart;
        MenuKontekstowe.IsOn = u.MenuKontekstowe;
        ListaPoOtwarciu.SelectedIndex = (int)u.PoOtwarciuPdf;
        SprawdzajAktualizacje.IsOn = u.SprawdzajAktualizacje;
        OdswiezStanIntegracji();
        OdswiezLogo();
    }

    /// <summary>„Minimalizuj po podpisaniu” działa tylko z zasobnikiem — wynik pokazuje wtedy dymek z ikony.</summary>
    private void PrzyZmianieZasobnika(object sender, RoutedEventArgs e) =>
        MinimalizujPoPodpisie.IsEnabled = Zasobnik.IsOn;

    private void PrzyZmianieStrony(object sender, SelectionChangedEventArgs e) =>
        NumerStrony.IsEnabled = ListaStron.SelectedIndex >= 0 && Strony[ListaStron.SelectedIndex] == WyborStrony.Numer;

    private void PrzyZmianieStanowiska(object sender, TextChangedEventArgs e) => OdswiezPodglad();

    /// <summary>Stanowisko z pola: wiersze bez pustych i bez spacji na brzegach.</summary>
    private string StanowiskoZPola() =>
        string.Join("\n", Stanowisko.Text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').Select(w => w.Trim()).Where(w => w.Length > 0));

    private void OdswiezLogo()
    {
        if (string.IsNullOrEmpty(_sciezkaLogo))
        {
            OpisLogo.Text = "Brak logo";
            PrzyciskUsunLogo.IsEnabled = false;
        }
        else
        {
            var wektorowe = Path.GetExtension(_sciezkaLogo).ToLowerInvariant() is ".svg" or ".pdf";
            OpisLogo.Text = $"{Path.GetFileName(_sciezkaLogo)}  ·  {(wektorowe ? "wektorowe" : "rastrowe")}";
            PrzyciskUsunLogo.IsEnabled = true;
        }
        ToolTipService.SetToolTip(OpisLogo, _sciezkaLogo);
        OdswiezPodglad();
    }

    /// <summary>Podgląd wizualizacji z bieżącym stanowiskiem i logo (w tle).</summary>
    private async void OdswiezPodglad()
    {
        var numer = ++_numerPodgladu;
        var stanowisko = StanowiskoZPola();
        var logo = _sciezkaLogo;
        var gestosc = XamlRoot?.RasterizationScale ?? 1.5;
        var (maksW, maksH) = (PodgladWizualizacji.Width * gestosc, PodgladWizualizacji.Height * gestosc);
        try
        {
            await Task.Delay(250); // przy pisaniu odświeżamy dopiero po chwili
            if (numer != _numerPodgladu)
                return;
            var obraz = await Task.Run(() =>
            {
                var dane = new DaneWizualizacji("Imię Nazwisko", stanowisko, logo, DateTimeOffset.Now);
                var rozmiar = Wizualizacja.Rozmiar(dane);
                return DokumentPdf.Renderuj(Wizualizacja.PodgladPdf(dane), 0,
                    Math.Min(maksW / rozmiar.Szerokosc, maksH / rozmiar.Wysokosc));
            });
            if (numer == _numerPodgladu)
                PodgladWizualizacji.Source = Obrazy.NaObraz(obraz);
        }
        catch (Exception)
        {
            // Podgląd jest tylko pomocą — błąd logo wyjdzie przy zapisie.
        }
    }

    private async void PrzyZmianieLogo(object sender, RoutedEventArgs e)
    {
        try
        {
            var plik = await Dialogi.WybierzPlikAsync(Okno.Hwnd, ".svg", ".pdf", ".png", ".jpg", ".jpeg");
            if (plik == null)
                return;
            _sciezkaLogo = plik;
            OdswiezLogo();
        }
        catch (Exception ex)
        {
            await Dialogi.BladAsync(XamlRoot, "Nie udało się otworzyć okna wyboru pliku.\n\n" + ex.Message);
        }
    }

    private void PrzyUsunieciuLogo(object sender, RoutedEventArgs e)
    {
        _sciezkaLogo = null;
        OdswiezLogo();
    }

    private async void PrzyTescieTsa(object sender, RoutedEventArgs e)
    {
        var adres = AdresTsa.Text.Trim();
        var login = string.IsNullOrWhiteSpace(LoginTsa.Text) ? null : LoginTsa.Text.Trim();
        var haslo = HasloTsa.Password;
        if (login != null && !CzyHttps(adres))
        {
            await Dialogi.BladAsync(XamlRoot, KomunikatHttps, "Popraw ustawienia");
            return;
        }
        PrzyciskTestuTsa.IsEnabled = false;
        TestTsaPostep.IsActive = true;
        try
        {
            var czas = await Task.Run(() =>
            {
                var klient = KlientZnacznikaCzasu.DlaAdresu(adres, login, haslo);
                // Pobranie pełnego znacznika (a nie tylko czasu), żeby zobaczyć ewentualny błąd serwera.
                var skrot = klient.GetMessageDigest().Digest(Guid.NewGuid().ToByteArray());
                klient.GetTimeStampToken(skrot);
                return klient.PobierzCzas();
            });
            await Dialogi.PokazAsync(XamlRoot, "Połączenie działa",
                "Połączenie z serwerem znacznika czasu działa." +
                (czas != null ? $"\nCzas serwera: {czas.Value.ToLocalTime():yyyy-MM-dd HH:mm:ss}" : ""));
        }
        catch (Exception ex)
        {
            await Dialogi.BladAsync(XamlRoot, KomunikatyBledow.Opisz(ex), "Brak połączenia");
        }
        finally
        {
            TestTsaPostep.IsActive = false;
            PrzyciskTestuTsa.IsEnabled = true;
        }
    }

    private void PrzyOtwarciuFolderu(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(Ustawienia.Folder);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{Ustawienia.Folder}\"") { UseShellExecute = true });
        }
        catch (Exception)
        {
            // Brak eksploratora nie jest błędem.
        }
    }

    // ---- Integracja z Windows: aplikacja domyślna, menu Windows 11, aktualizacje ----

    /// <summary>Opisy i przyciski zgodnie z bieżącym stanem systemu.</summary>
    private void OdswiezStanIntegracji()
    {
        KartaDomyslnej.Description = DomyslnaAplikacja.JestDomyslna
            ? "PodpiszPdf otwiera pliki PDF. Zmienisz to w Ustawieniach Windows → Aplikacje domyślne."
            : "Pliki PDF otwierane dwuklikiem trafią do PodpiszPdf, a ten zapyta: podpisać czy przeglądać (w Adobe Acrobat Reader)";
        PrzyciskDomyslnej.Content = DomyslnaAplikacja.JestDomyslna ? "Ustawienia Windows…" : "Ustaw jako domyślną…";

        if (!MenuWindows11.JestWindows11)
        {
            KartaMenu11.Description = "Dostępne tylko w Windows 11. W Windows 10 działa zwykłe menu kontekstowe.";
            PrzyciskMenu11.IsEnabled = false;
            return;
        }
        if (!MenuWindows11.MaPakiet)
        {
            KartaMenu11.Description = "Ta kopia programu nie zawiera podpisanego pakietu menu (zainstaluj PodpiszPdf instalatorem MSI z GitHub).";
            PrzyciskMenu11.IsEnabled = false;
            return;
        }
        var zarejestrowana = MenuWindows11.ZarejestrowanaWersja() != null;
        var zaufany = MenuWindows11.CertyfikatZaufany();
        KartaMenu11.Description = zarejestrowana
            ? $"„{PodpiszPdf.MenuKontekstowe.Etykieta}” jest w menu Windows 11 (bez „Pokaż więcej opcji”)"
            : zaufany
                ? $"Dodaje „{PodpiszPdf.MenuKontekstowe.Etykieta}” bezpośrednio do menu Windows 11"
                : "Certyfikat PodpiszPdf nie jest zaufany na tym komputerze — zainstaluj program ponownie instalatorem MSI (dodaje certyfikat)";
        PrzyciskMenu11.Content = zarejestrowana ? "Usuń z menu" : "Dodaj do menu";
        PrzyciskMenu11.IsEnabled = zarejestrowana || zaufany;
    }

    private async void PrzyUstawieniuDomyslnej(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!Okno.TrybTestowy)
            {
                DomyslnaAplikacja.Zarejestruj(Okno.Ustawienia);
                _ustawienia.PoprzedniaAplikacjaPdf = Okno.Ustawienia.PoprzedniaAplikacjaPdf;
                Okno.ZapiszUstawienia();
            }
            if (!DomyslnaAplikacja.JestDomyslna)
            {
                await Dialogi.PokazAsync(XamlRoot, "Wybierz PodpiszPdf w Ustawieniach Windows",
                    "Windows nie pozwala programom samodzielnie zostać aplikacją domyślną.\n\n" +
                    "W oknie, które się otworzy, kliknij pozycję „.pdf” i wybierz „PodpiszPdf”.");
            }
            DomyslnaAplikacja.OtworzUstawieniaWindows();
        }
        catch (Exception ex)
        {
            await Dialogi.BladAsync(XamlRoot, "Nie udało się zarejestrować PodpiszPdf jako aplikacji do PDF.\n\n" + KomunikatyBledow.Opisz(ex));
        }
    }

    private async void PrzyMenu11(object sender, RoutedEventArgs e)
    {
        var dodaj = MenuWindows11.ZarejestrowanaWersja() == null;
        PostepMenu11.IsActive = true;
        PrzyciskMenu11.IsEnabled = false;
        try
        {
            if (dodaj)
                await MenuWindows11.ZarejestrujAsync();
            else
                await MenuWindows11.WyrejestrujAsync();
            // Zmiana działa od razu — zapisujemy ją niezależnie od przycisku „Zapisz”.
            _ustawienia.PozycjaMenuWindows11 = dodaj;
            Okno.Ustawienia.PozycjaMenuWindows11 = dodaj;
            Okno.ZapiszUstawienia();
            if (dodaj)
                await Dialogi.PokazAsync(XamlRoot, "Gotowe",
                    $"„{PodpiszPdf.MenuKontekstowe.Etykieta}” pojawi się w menu kontekstowym plików PDF. " +
                    "Jeśli jeszcze go nie widać, zamknij i otwórz ponownie okno Eksploratora.");
        }
        catch (Exception ex)
        {
            await Dialogi.BladAsync(XamlRoot, ex is InvalidOperationException ? ex.Message : KomunikatyBledow.Opisz(ex));
        }
        finally
        {
            PostepMenu11.IsActive = false;
            OdswiezStanIntegracji();
        }
    }

    private async void PrzySprawdzeniuAktualizacji(object sender, RoutedEventArgs e)
    {
        PrzyciskSprawdzenia.IsEnabled = false;
        PostepAktualizacji.IsActive = true;
        try
        {
            var aktualizacja = await AktualizacjaProgramu.SprawdzAsync();
            if (aktualizacja == null)
            {
                KartaAktualizacji.Description = $"Masz najnowszą wersję ({Instalacja.BiezacaWersja.ToString(3)}). Sprawdzono {DateTime.Now:HH:mm}.";
                return;
            }
            var odpowiedz = await Dialogi.PokazAsync(XamlRoot, $"Dostępna wersja {aktualizacja.Wersja}",
                $"Masz wersję {Instalacja.BiezacaWersja}. Zaktualizować teraz? Program uruchomi się ponownie." +
                (string.IsNullOrWhiteSpace(aktualizacja.Opis) ? "" : "\n\n" + aktualizacja.Opis.Trim()),
                "Później", "Zaktualizuj");
            var glowna = Okno.StronaGlowna;
            glowna?.PokazAktualizacje(aktualizacja);
            if (odpowiedz == ContentDialogResult.Primary && glowna != null)
            {
                Wroc();
                glowna.ZainstalujAktualizacje(aktualizacja);
            }
        }
        catch (Exception ex)
        {
            KartaAktualizacji.Description = "Nie udało się sprawdzić aktualizacji: " + KomunikatyBledow.Opisz(ex);
        }
        finally
        {
            PrzyciskSprawdzenia.IsEnabled = true;
            PostepAktualizacji.IsActive = false;
        }
    }

    // ---- Zapis ----

    private void PrzyAnulowaniu(object sender, RoutedEventArgs e) => Wroc();

    private async void PrzyZapisie(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_zakonczono || !await ZapiszZPol())
                return;
            // Najpierw zapis i zastosowanie, potem powrót — strona główna od razu pracuje na nowych ustawieniach.
            var bledy = Okno.StronaGlowna?.PoZmianieUstawien(_ustawienia) ?? new List<string>();
            if (bledy.Count > 0)
                await Dialogi.BladAsync(XamlRoot, string.Join("\n\n", bledy));
        }
        catch (Exception ex)
        {
            await Dialogi.BladAsync(XamlRoot, "Nie udało się zastosować ustawień.\n\n" + KomunikatyBledow.Opisz(ex));
        }
        Wroc();
    }

    private void Wroc()
    {
        if (_zakonczono)
            return;
        _zakonczono = true;
        if (Frame.CanGoBack)
            Frame.GoBack();
    }

    private async Task<bool> Blad(int sekcja, string komunikat, Control? pole = null)
    {
        PokazSekcje(sekcja);
        await Dialogi.BladAsync(XamlRoot, komunikat, "Popraw ustawienia");
        pole?.Focus(FocusState.Programmatic);
        return false;
    }

    private const string KomunikatHttps =
        "Przy logowaniu do serwera znacznika czasu adres musi zaczynać się od https:// — przez http login i hasło " +
        "byłyby wysyłane otwartym tekstem. Zmień adres na https:// albo usuń login (darmowy serwer Certum go nie wymaga).";

    private static bool CzyHttps(string adres) =>
        Uri.TryCreate(adres, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;

    private async Task<bool> ZapiszZPol()
    {
        var przyrostek = Przyrostek.Text.Trim();
        if (przyrostek.Length == 0 || przyrostek.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return await Blad(2, "Przyrostek nazwy pliku nie może być pusty ani zawierać znaków \\ / : * ? \" < > |.", Przyrostek);

        var adres = AdresTsa.Text.Trim();
        if (!Uri.TryCreate(adres, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
            return await Blad(1, "Podaj poprawny adres serwera znacznika czasu, np. http://time.certum.pl", AdresTsa);
        if (!string.IsNullOrWhiteSpace(LoginTsa.Text) && !CzyHttps(adres))
            return await Blad(1, KomunikatHttps, AdresTsa);

        var logo = _sciezkaLogo ?? "";
        if (logo.Length > 0 && !File.Exists(logo))
            return await Blad(0, "Nie znaleziono pliku logo. Wybierz plik ponownie albo usuń logo.");
        if (logo.Length > 0 && !Wizualizacja.RozszerzeniaLogo.Contains(Path.GetExtension(logo).ToLowerInvariant()))
            return await Blad(0, "Nieobsługiwany format logo. Użyj pliku SVG, PDF, PNG lub JPG.");

        // Sprawdzenie, czy logo da się odczytać — lepiej teraz niż przy podpisie.
        if (logo.Length > 0)
        {
            try
            {
                await Task.Run(() => Wizualizacja.PodgladPdf(new DaneWizualizacji("Test", null, logo, DateTimeOffset.Now)));
            }
            catch (Exception ex)
            {
                return await Blad(0, KomunikatyBledow.Opisz(ex));
            }
        }

        string? kopiaLogo;
        if (Okno.TrybTestowy)
        {
            kopiaLogo = logo.Length == 0 ? null : logo;
        }
        else
        {
            try
            {
                kopiaLogo = Ustawienia.ZachowajKopieLogo(logo.Length == 0 ? null : logo);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return await Blad(0, $"Nie udało się zapisać kopii logo w folderze ustawień ({Ustawienia.Folder}).\n\n{ex.Message}");
            }
        }

        var u = _ustawienia;
        var stanowisko = StanowiskoZPola();
        u.Stanowisko = stanowisko.Length == 0 ? null : stanowisko;
        u.SciezkaLogo = kopiaLogo;
        u.AdresTsa = adres;
        u.LoginTsa = string.IsNullOrWhiteSpace(LoginTsa.Text) ? null : LoginTsa.Text.Trim();
        u.HasloTsa = HasloTsa.Password;
        u.Polozenie = new PolozenieWizualizacji
        {
            Strona = Strony[Math.Max(0, ListaStron.SelectedIndex)],
            NumerStrony = double.IsNaN(NumerStrony.Value) ? 1 : (int)NumerStrony.Value,
            Naroznik = Narozniki[Math.Max(0, ListaNaroznikow.SelectedIndex)],
            MarginesPoziomyMm = double.IsNaN(MarginesX.Value) ? 15 : (float)MarginesX.Value,
            MarginesPionowyMm = double.IsNaN(MarginesY.Value) ? 15 : (float)MarginesY.Value,
        };
        u.Przyrostek = przyrostek;
        u.PoziomLT = PoziomLT.IsOn;
        u.MinimalizujDoZasobnika = Zasobnik.IsOn;
        u.MinimalizujPoPodpisie = MinimalizujPoPodpisie.IsOn;
        u.Autostart = Autostart.IsOn;
        u.MenuKontekstowe = MenuKontekstowe.IsOn;
        u.PoOtwarciuPdf = (PoOtwarciuPdf)Math.Max(0, ListaPoOtwarciu.SelectedIndex);
        u.SprawdzajAktualizacje = SprawdzajAktualizacje.IsOn;
        return true;
    }
}
