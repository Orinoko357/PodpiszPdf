using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using PodpiszPdf.Core;

namespace PodpiszPdf.Mac;

/// <summary>Ustawienia: wizualizacja, znacznik czasu, karta, program, informacje. Zmiany obowiązują po „Zapisz”.</summary>
public sealed partial class StronaUstawien : UserControl
{
    private static readonly WyborStrony[] Strony = { WyborStrony.Ostatnia, WyborStrony.Pierwsza, WyborStrony.Numer };
    private static readonly Naroznik[] Narozniki = { Naroznik.PrawyDolny, Naroznik.LewyDolny, Naroznik.PrawyGorny, Naroznik.LewyGorny };
    private static readonly string[] Tytuly = { "Wizualizacja", "Znacznik czasu", "Karta", "Program", "Informacje" };

    private const string KomunikatHttps =
        "Przy logowaniu do serwera znacznika czasu adres musi zaczynać się od https:// — przez http login i hasło " +
        "byłyby wysyłane otwartym tekstem. Zmień adres na https:// albo usuń login (darmowy serwer Certum go nie wymaga).";

    private readonly OknoGlowne _okno;
    private readonly Ustawienia _ustawienia;
    private string? _sciezkaLogo;
    private string? _biblioteka;
    private int _numerPodgladu;
    private bool _zakonczono;
    private bool _wczytano;

    public StronaUstawien() : this(new OknoGlowne(), new Ustawienia(), 0)
    {
    }

    internal StronaUstawien(OknoGlowne okno, Ustawienia ustawienia, int sekcja)
    {
        _okno = okno;
        _ustawienia = ustawienia;
        InitializeComponent();
        Wersja.Text = $"PodpiszPdf {Program.Wersja} dla macOS";
        KartaFolderu.Opis = Ustawienia.Folder;
        KartaAktualizacji.Opis = $"Nowe wersje z {Aktualizacje.StronaWydan}";
        WczytajDoPol();
        _wczytano = true;
        PokazSekcje(sekcja);
        OdswiezPodglad();
    }

    internal void PokazSekcje(int indeks)
    {
        Nawigacja.SelectedIndex = indeks;
        UstawSekcje(indeks);
    }

    private void UstawSekcje(int indeks)
    {
        SekcjaWizualizacji.IsVisible = indeks == 0;
        SekcjaZnacznika.IsVisible = indeks == 1;
        SekcjaKarty.IsVisible = indeks == 2;
        SekcjaProgramu.IsVisible = indeks == 3;
        SekcjaInformacji.IsVisible = indeks == 4;
        Tytul.Text = Tytuly[indeks];
        Przewijanie.Offset = default;
        if (indeks == 2)
            OdswiezBiblioteki();
    }

    private void PrzyZmianieSekcji(object? sender, SelectionChangedEventArgs e)
    {
        if (Nawigacja.SelectedIndex >= 0 && Tytul != null)
            UstawSekcje(Nawigacja.SelectedIndex);
    }

    private void WczytajDoPol()
    {
        var u = _ustawienia;
        var p = u.Polozenie;
        Stanowisko.Text = u.Stanowisko ?? "";
        _sciezkaLogo = u.SciezkaLogo;
        _biblioteka = u.BibliotekaPkcs11;
        AdresTsa.Text = u.AdresTsa;
        LoginTsa.Text = u.LoginTsa ?? "";
        HasloTsa.Text = _okno.TrybTestowy ? "" : u.HasloTsa ?? "";
        ListaStron.SelectedIndex = Math.Max(0, Array.IndexOf(Strony, p.Strona));
        NumerStrony.Value = Math.Clamp(p.NumerStrony, 1, 9999);
        NumerStrony.IsEnabled = p.Strona == WyborStrony.Numer;
        ListaNaroznikow.SelectedIndex = Math.Max(0, Array.IndexOf(Narozniki, p.Naroznik));
        MarginesX.Value = (decimal)Math.Clamp(p.MarginesPoziomyMm, 0, 500);
        MarginesY.Value = (decimal)Math.Clamp(p.MarginesPionowyMm, 0, 500);
        Przyrostek.Text = u.Przyrostek;
        DodawajPrzyrostek.IsChecked = u.DodawajPrzyrostek;
        Przyrostek.IsEnabled = u.DodawajPrzyrostek;
        ListaMotywow.SelectedIndex = Math.Clamp((int)u.MotywWizualizacji, 0, ListaMotywow.ItemCount - 1);
        PoziomLT.IsChecked = u.PoziomLT;
        SprawdzajAktualizacje.IsChecked = u.SprawdzajAktualizacje;
        OdswiezLogo();
    }

    // ---- Wizualizacja ----

    private void PrzyZmianieStrony(object? sender, SelectionChangedEventArgs e)
    {
        if (NumerStrony != null)
            NumerStrony.IsEnabled = ListaStron.SelectedIndex >= 0 && Strony[ListaStron.SelectedIndex] == WyborStrony.Numer;
    }

    private void PrzyZmianieStanowiska(object? sender, TextChangedEventArgs e) => OdswiezPodglad();

    private MotywWizualizacji WybranyMotyw =>
        ListaMotywow == null || ListaMotywow.SelectedIndex < 0 ? MotywWizualizacji.Klasyczny : (MotywWizualizacji)ListaMotywow.SelectedIndex;

    private void PrzyZmianieMotywu(object? sender, SelectionChangedEventArgs e) => OdswiezPodglad();

    private string StanowiskoZPola() =>
        string.Join("\n", (Stanowisko.Text ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').Select(w => w.Trim()).Where(w => w.Length > 0));

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
        ToolTip.SetTip(OpisLogo, _sciezkaLogo);
        OdswiezPodglad();
    }

    private async void OdswiezPodglad()
    {
        if (!_wczytano)
            return;
        var numer = ++_numerPodgladu;
        var stanowisko = StanowiskoZPola();
        var logo = _sciezkaLogo;
        var motyw = WybranyMotyw;
        var gestosc = 2.0; // ekrany Retina; na zwykłym obraz jest tylko ostrzejszy
        var (maksW, maksH) = (PodgladWizualizacji.Width * gestosc, PodgladWizualizacji.Height * gestosc);
        try
        {
            await Task.Delay(_okno.TrybTestowy ? 0 : 250); // przy pisaniu odświeżamy dopiero po chwili
            if (numer != _numerPodgladu)
                return;
            var obraz = await WatekRoboczy.Uruchom(() =>
            {
                var dane = new DaneWizualizacji("Imię Nazwisko", stanowisko, logo, DateTimeOffset.Now, motyw, Kwalifikowany: true);
                var rozmiar = Wizualizacja.Rozmiar(dane);
                return DokumentPdf.Renderuj(Wizualizacja.PodgladPdf(dane), 0,
                    Math.Min(maksW / rozmiar.Szerokosc, maksH / rozmiar.Wysokosc));
            });
            if (numer == _numerPodgladu)
                PodgladWizualizacji.Source = Obrazy.NaObraz(obraz);
        }
        catch (Exception)
        {
            // Błąd (np. znaki spoza czcionki) wyjdzie przy zapisie albo podpisie.
        }
    }

    internal Task CzekajNaPodglad() => Task.Delay(1500);

    private async void PrzyZmianieLogo(object? sender, RoutedEventArgs e)
    {
        var pliki = await _okno.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Wybierz logo",
            FileTypeFilter = new[] { new FilePickerFileType("Logo (SVG, PDF, PNG, JPG)") { Patterns = new[] { "*.svg", "*.pdf", "*.png", "*.jpg", "*.jpeg" } } },
        });
        var plik = pliki.Select(p => p.TryGetLocalPath()).FirstOrDefault(p => p != null);
        if (plik == null)
            return;
        _sciezkaLogo = plik;
        OdswiezLogo();
    }

    private void PrzyUsunieciuLogo(object? sender, RoutedEventArgs e)
    {
        _sciezkaLogo = null;
        OdswiezLogo();
    }

    // ---- Znacznik czasu ----

    private static bool CzyHttps(string adres) =>
        Uri.TryCreate(adres, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;

    private async void PrzyTescieTsa(object? sender, RoutedEventArgs e)
    {
        var adres = (AdresTsa.Text ?? "").Trim();
        var login = string.IsNullOrWhiteSpace(LoginTsa.Text) ? null : LoginTsa.Text.Trim();
        var haslo = HasloTsa.Text;
        if (login != null && !CzyHttps(adres))
        {
            await Dialogi.BladAsync(_okno, KomunikatHttps, "Popraw ustawienia");
            return;
        }
        PrzyciskTestuTsa.IsEnabled = false;
        TestTsaPostep.IsVisible = true;
        try
        {
            var czas = await WatekRoboczy.Uruchom(() =>
            {
                var klient = KlientZnacznikaCzasu.DlaAdresu(adres, login, haslo);
                var skrot = klient.GetMessageDigest().Digest(Guid.NewGuid().ToByteArray());
                klient.GetTimeStampToken(skrot);
                return klient.PobierzCzas();
            });
            await Dialogi.PokazAsync(_okno, "Połączenie działa",
                "Połączenie z serwerem znacznika czasu działa." +
                (czas != null ? $"\nCzas serwera: {czas.Value.ToLocalTime():yyyy-MM-dd HH:mm:ss}" : ""));
        }
        catch (Exception ex)
        {
            await Dialogi.BladAsync(_okno, KomunikatyBledow.Opisz(ex), "Brak połączenia");
        }
        finally
        {
            TestTsaPostep.IsVisible = false;
            PrzyciskTestuTsa.IsEnabled = true;
        }
    }

    // ---- Karta ----

    private void OdswiezBiblioteki()
    {
        var znalezione = KartaPkcs11.ZnajdzBiblioteki();
        OpisBiblioteki.Text = _biblioteka != null
            ? $"Wskazana biblioteka: {_biblioteka}"
            : znalezione.Count > 0
                ? "Biblioteki są wyszukiwane automatycznie (lista poniżej)."
                : "Nie znaleziono żadnej biblioteki PKCS#11. Zainstaluj oprogramowanie karty albo wskaż plik biblioteki.";
        PrzyciskBibliotekiAutomatycznej.IsEnabled = _biblioteka != null;
        ZnalezioneBiblioteki.Text = znalezione.Count > 0 ? string.Join("\n", znalezione) : "—";
    }

    private async void PrzyWyborzeBiblioteki(object? sender, RoutedEventArgs e)
    {
        var start = await _okno.StorageProvider.TryGetFolderFromPathAsync(Directory.Exists("/usr/local/lib") ? "/usr/local/lib" : "/Library");
        var pliki = await _okno.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Wskaż bibliotekę PKCS#11 sterownika karty",
            SuggestedStartLocation = start,
            FileTypeFilter = new[] { new FilePickerFileType("Biblioteka PKCS#11 (.dylib, .so)") { Patterns = new[] { "*.dylib", "*.so" } } },
        });
        var plik = pliki.Select(p => p.TryGetLocalPath()).FirstOrDefault(p => p != null);
        if (plik == null)
            return;
        try
        {
            var certyfikaty = await WatekRoboczy.Uruchom(() =>
            {
                lock (StronaGlowna.BlokadaKarty)
                    return KartaPkcs11.PobierzCertyfikaty(plik, DateTime.Now);
            });
            _biblioteka = plik;
            OdswiezBiblioteki();
            await Dialogi.PokazAsync(_okno, "Biblioteka działa",
                certyfikaty.Count > 0
                    ? $"Znaleziono certyfikaty do podpisu: {certyfikaty.Count}. Zapisz ustawienia, aby ich używać."
                    : "Biblioteka działa, ale nie widać certyfikatu do podpisu — sprawdź, czy karta jest w czytniku.");
        }
        catch (Exception ex)
        {
            await Dialogi.BladAsync(_okno, KomunikatyBledow.Opisz(ex), "Nie można użyć tej biblioteki");
        }
    }

    private void PrzyAutomatycznejBibliotece(object? sender, RoutedEventArgs e)
    {
        _biblioteka = null;
        OdswiezBiblioteki();
    }

    // ---- Program i informacje ----

    private void PrzyZmianiePrzyrostka(object? sender, RoutedEventArgs e)
    {
        if (Przyrostek != null)
            Przyrostek.IsEnabled = DodawajPrzyrostek.IsChecked == true;
    }

    private void PrzyOtwarciuFolderu(object? sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(Ustawienia.Folder);
            Process.Start(new ProcessStartInfo("open", new[] { Ustawienia.Folder }) { UseShellExecute = false });
        }
        catch (Exception)
        {
            // Bez Findera — ścieżka jest widoczna w opisie.
        }
    }

    private void PrzyKodzieZrodlowym(object? sender, RoutedEventArgs e) => StronaGlowna.Otworz("https://github.com/Orinoko357/PodpiszPdf");

    private async void PrzySprawdzeniuAktualizacji(object? sender, RoutedEventArgs e)
    {
        PrzyciskSprawdzenia.IsEnabled = false;
        PostepAktualizacji.IsVisible = true;
        try
        {
            var wynik = await AktualizacjeMac.SprawdzAsync(Version.Parse(Program.Wersja));
            if (wynik is not { } a)
            {
                KartaAktualizacji.Opis = $"Masz najnowszą wersję ({Program.Wersja}). Sprawdzono {DateTime.Now:HH:mm}.";
                return;
            }
            _okno.StronaGlowna.PokazAktualizacje(a.Wersja, a.Adres);
            if (await Dialogi.PokazAsync(_okno, $"Dostępna wersja {a.Wersja}",
                    $"Masz wersję {Program.Wersja}. Pobrać nową? Otwórz pobrany obraz DMG i przeciągnij PodpiszPdf do folderu Aplikacje (zastąp poprzednią wersję).",
                    "Później", "Pobierz") == 1)
                StronaGlowna.Otworz(a.Adres);
        }
        catch (Exception ex)
        {
            KartaAktualizacji.Opis = "Nie udało się sprawdzić aktualizacji: " + KomunikatyBledow.Opisz(ex);
        }
        finally
        {
            PrzyciskSprawdzenia.IsEnabled = true;
            PostepAktualizacji.IsVisible = false;
        }
    }

    // ---- Zapis ----

    private void PrzyAnulowaniu(object? sender, RoutedEventArgs e) => Wroc();

    private async void PrzyZapisie(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (_zakonczono || !await ZapiszZPol())
                return;
            _ustawienia.OstatniCertyfikat = _okno.Ustawienia.OstatniCertyfikat;
            _ustawienia.OstatnieSprawdzenieAktualizacji = _okno.Ustawienia.OstatnieSprawdzenieAktualizacji;
            var zmianaKarty = _ustawienia.BibliotekaPkcs11 != _okno.Ustawienia.BibliotekaPkcs11;
            _okno.Ustawienia = _ustawienia;
            _okno.ZapiszUstawienia();
            Wroc();
            if (zmianaKarty)
                _okno.StronaGlowna.OdswiezCertyfikatyPoZmianie();
        }
        catch (Exception ex)
        {
            await Dialogi.BladAsync(_okno, "Nie udało się zapisać ustawień.\n\n" + KomunikatyBledow.Opisz(ex));
        }
    }

    private void Wroc()
    {
        if (_zakonczono)
            return;
        _zakonczono = true;
        _okno.PokazStroneGlowna();
    }

    private async Task<bool> Blad(int sekcja, string komunikat, Control? pole = null)
    {
        PokazSekcje(sekcja);
        await Dialogi.BladAsync(_okno, komunikat, "Popraw ustawienia");
        pole?.Focus();
        return false;
    }

    private async Task<bool> ZapiszZPol()
    {
        var przyrostek = (Przyrostek.Text ?? "").Trim();
        var dodawaj = DodawajPrzyrostek.IsChecked == true;
        if (!dodawaj && przyrostek.Length == 0)
            przyrostek = Ustawienia.DomyslnyPrzyrostek;
        if (przyrostek.Length == 0 || przyrostek.IndexOfAny(new[] { '/', ':', '\\', '\0' }) >= 0)
            return await Blad(3, "Przyrostek nazwy pliku nie może być pusty ani zawierać znaków / : \\.", Przyrostek);

        var adres = (AdresTsa.Text ?? "").Trim();
        if (!Uri.TryCreate(adres, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
            return await Blad(1, "Podaj poprawny adres serwera znacznika czasu, np. http://time.certum.pl", AdresTsa);
        var login = string.IsNullOrWhiteSpace(LoginTsa.Text) ? null : LoginTsa.Text.Trim();
        if (login != null && !CzyHttps(adres))
            return await Blad(1, KomunikatHttps, AdresTsa);

        var logo = _sciezkaLogo ?? "";
        if (logo.Length > 0 && !File.Exists(logo))
            return await Blad(0, "Nie znaleziono pliku logo. Wybierz plik ponownie albo usuń logo.");
        if (logo.Length > 0 && !Wizualizacja.RozszerzeniaLogo.Contains(Path.GetExtension(logo).ToLowerInvariant()))
            return await Blad(0, "Nieobsługiwany format logo. Użyj pliku SVG, PDF, PNG lub JPG.");
        if (logo.Length > 0)
        {
            try
            {
                await WatekRoboczy.Uruchom(() => Wizualizacja.PodgladPdf(new DaneWizualizacji("Test", null, logo, DateTimeOffset.Now)));
            }
            catch (Exception ex)
            {
                return await Blad(0, KomunikatyBledow.Opisz(ex));
            }
        }

        string? kopiaLogo;
        if (_okno.TrybTestowy)
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
        var poprzedniLogin = u.LoginTsa;
        u.LoginTsa = login;
        if (!_okno.TrybTestowy)
        {
            try
            {
                if (poprzedniLogin != null && poprzedniLogin != login)
                    PekKluczy.Zapisz(poprzedniLogin, null);
                u.HasloTsa = HasloTsa.Text;
            }
            catch (IOException ex)
            {
                return await Blad(1, ex.Message, HasloTsa);
            }
        }
        u.Polozenie = new PolozenieWizualizacji
        {
            Strona = Strony[Math.Max(0, ListaStron.SelectedIndex)],
            NumerStrony = (int)(NumerStrony.Value ?? 1),
            Naroznik = Narozniki[Math.Max(0, ListaNaroznikow.SelectedIndex)],
            MarginesPoziomyMm = (float)(MarginesX.Value ?? 15),
            MarginesPionowyMm = (float)(MarginesY.Value ?? 15),
        };
        u.Przyrostek = przyrostek;
        u.DodawajPrzyrostek = dodawaj;
        u.MotywWizualizacji = WybranyMotyw;
        u.PoziomLT = PoziomLT.IsChecked == true;
        u.BibliotekaPkcs11 = _biblioteka;
        u.SprawdzajAktualizacje = SprawdzajAktualizacje.IsChecked == true;
        return true;
    }
}
