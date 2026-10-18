using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using PodpiszPdf.Core;

namespace PodpiszPdf.Mac;

/// <summary>Certyfikat na liście (dane do wyświetlenia).</summary>
public sealed class ElementCertyfikatu(CertyfikatInfo certyfikat)
{
    public CertyfikatInfo Certyfikat { get; } = certyfikat;
    public string Nazwa => Certyfikat.ImieNazwisko;
    public bool Kwalifikowany => Certyfikat.Kwalifikowany;
    public string Opis =>
        $"{Certyfikat.Wystawca}  ·  ważny do {Certyfikat.WaznyDo:dd.MM.yyyy}" +
        (Certyfikat.Karta is { } k ? $"  ·  karta {k.EtykietaTokenu}" : "  ·  pęk kluczy");
}

/// <summary>Strona główna: dokument, certyfikat i przycisk podpisu.</summary>
public sealed partial class StronaGlowna : UserControl
{
    private readonly OknoGlowne _okno;
    private string? _plik;
    private bool _przebiegPodpisu;
    private bool _etapKrytyczny;
    private bool _wczytywanieCertyfikatow;
    private bool _zainicjalizowano;
    private int _numerMiniatury;
    private string? _adresAktualizacji;
    private string? _plikZFindera;

    public StronaGlowna() : this(new OknoGlowne())
    {
    }

    internal StronaGlowna(OknoGlowne okno)
    {
        _okno = okno;
        InitializeComponent();
        AddHandler(DragDrop.DragOverEvent, PrzyPrzeciaganiu);
        AddHandler(DragDrop.DropEvent, PrzyUpuszczeniu);
        UstawPlik(null);
        OdswiezInformacje();
    }

    private Ustawienia Ustawienia => _okno.Ustawienia;

    /// <summary>Trwa przebieg podpisu (pytania, podgląd, PIN, podpis).</summary>
    internal bool Zajeta => _przebiegPodpisu;

    /// <summary>Trwa sam podpis — nie wolno go przerwać zamknięciem okna.</summary>
    internal bool EtapKrytyczny => _etapKrytyczny;

    private CertyfikatInfo? WybranyCertyfikat => (ListaCertyfikatow.SelectedItem as ElementCertyfikatu)?.Certyfikat;

    internal async void Inicjalizuj()
    {
        if (_zainicjalizowano)
            return;
        _zainicjalizowano = true;
        await WczytajCertyfikaty();
        if (_plikZFindera is { } plik)
        {
            _plikZFindera = null;
            OtworzZFindera(plik);
        }
        _ = WatekRoboczy.Uruchom(() => Rozgrzewka.Wykonaj());
        await SprawdzAktualizacje();
    }

    /// <summary>Inna biblioteka karty w ustawieniach — lista certyfikatów od nowa.</summary>
    internal async void OdswiezCertyfikatyPoZmianie() => await WczytajCertyfikaty();

    /// <summary>Po powrocie z podglądu lub ustawień.</summary>
    internal void PoPowrocie()
    {
        OdswiezInformacje();
        OdswiezStan();
    }

    /// <summary>Plik z Findera: wybór i od razu podpis (jak z menu kontekstowego w Windows).</summary>
    internal void OtworzZFindera(string plik)
    {
        if (!_zainicjalizowano || _wczytywanieCertyfikatow)
        {
            _plikZFindera = plik; // po wczytaniu certyfikatów
            return;
        }
        if (Zajeta || !ReferenceEquals(_okno.Tresc.Content, this))
            return;
        UstawPlik(plik);
        if (_plik != null && WybranyCertyfikat != null)
            Podpisz();
    }

    /// <summary>Zrzuty ekranu i test uruchomienia: przykładowy plik i certyfikaty.</summary>
    internal void PokazDanePrzykladowe(string? plik, IReadOnlyList<CertyfikatInfo> certyfikaty)
    {
        _zainicjalizowano = true;
        UstawCertyfikaty(certyfikaty, null);
        UstawPlik(plik);
        UstawStatus("Gotowy. Program zapamięta wybrany certyfikat.");
    }

    // ---- Aktualizacje ----

    /// <summary>
    /// Nowa wersja w repozytorium wydań (najwyżej raz na dobę): pasek z odnośnikiem do obrazu DMG. Program dla macOS
    /// nie instaluje się sam — bez podpisu Apple system i tak poprosi o potwierdzenie przy pierwszym otwarciu.
    /// </summary>
    private async Task SprawdzAktualizacje()
    {
        var u = Ustawienia;
        if (!u.SprawdzajAktualizacje || _okno.TrybTestowy || Program.TrybSprawdzenia)
            return;
        if (u.OstatnieSprawdzenieAktualizacji is { } ostatnie && DateTimeOffset.Now - ostatnie < TimeSpan.FromHours(20))
            return;
        try
        {
            var wynik = await AktualizacjeMac.SprawdzAsync(Version.Parse(Program.Wersja));
            Ustawienia.OstatnieSprawdzenieAktualizacji = DateTimeOffset.Now;
            ZapiszCicho();
            if (wynik is { } a)
                PokazAktualizacje(a.Wersja, a.Adres);
        }
        catch (Exception)
        {
            // Brak sieci — spróbujemy przy następnym uruchomieniu.
        }
    }

    internal void PokazAktualizacje(Version wersja, string adres)
    {
        _adresAktualizacji = adres;
        TytulAktualizacji.Text = $"Dostępna nowa wersja {wersja}";
        OpisAktualizacji.Text = $"Masz wersję {Program.Wersja}. Pobierz obraz DMG i przeciągnij PodpiszPdf do folderu Aplikacje.";
        Aktualizacja.IsVisible = true;
    }

    private void PrzyAktualizacji(object? sender, RoutedEventArgs e)
    {
        if (_adresAktualizacji != null)
            Otworz(_adresAktualizacji);
    }

    private void PrzyZamknieciuAktualizacji(object? sender, RoutedEventArgs e) => Aktualizacja.IsVisible = false;

    // ---- Certyfikaty ----

    /// <summary>Certyfikaty z kart (biblioteki PKCS#11) i z pęku kluczy — w tle.</summary>
    private async Task WczytajCertyfikaty()
    {
        if (_wczytywanieCertyfikatow)
            return;
        _wczytywanieCertyfikatow = true;
        var poprzedni = WybranyCertyfikat?.Odcisk ?? Ustawienia.OstatniCertyfikat;
        UstawStatus("Wczytywanie certyfikatów…");
        WczytywanieCertyfikatow.IsVisible = true;
        BrakCertyfikatow.IsVisible = false;
        OdswiezStan();
        var biblioteka = Ustawienia.BibliotekaPkcs11;
        var (certyfikaty, bledy, biblioteki) = await WatekRoboczy.Uruchom(() => OdczytajCertyfikaty(biblioteka));
        _wczytywanieCertyfikatow = false;
        WczytywanieCertyfikatow.IsVisible = false;

        UstawCertyfikaty(certyfikaty, poprzedni);
        OpisBrakuCertyfikatow.Text = biblioteki == 0
            ? "Nie znaleziono sterownika karty (biblioteki PKCS#11). Zainstaluj oprogramowanie karty (np. proCertum CardManager) " +
              "albo wskaż bibliotekę w Ustawieniach → Karta. Potem kliknij „Odśwież”."
            : "Włóż kartę do czytnika i kliknij „Odśwież”." + (bledy.Count > 0 ? "\n\n" + string.Join("\n", bledy) : "");
        UstawStatus(certyfikaty.Count == 0 ? "Brak certyfikatu do podpisu." : "Gotowy. Program zapamięta wybrany certyfikat.");
        if (certyfikaty.Count > 0 && bledy.Count > 0)
            UstawStatus(bledy[0]);
    }

    private static (IReadOnlyList<CertyfikatInfo> Certyfikaty, List<string> Bledy, int Biblioteki) OdczytajCertyfikaty(string? wybrana)
    {
        var bledy = new List<string>();
        var wszystkie = new List<CertyfikatInfo>();
        var biblioteki = wybrana != null ? new[] { wybrana } : KartaPkcs11.ZnajdzBiblioteki();
        foreach (var biblioteka in biblioteki)
        {
            try
            {
                lock (BlokadaKarty)
                    wszystkie.AddRange(KartaPkcs11.PobierzCertyfikaty(biblioteka, DateTime.Now));
            }
            catch (Exception e)
            {
                bledy.Add(KomunikatyBledow.Opisz(e));
            }
        }
        try
        {
            // Certyfikaty z kluczem w pęku kluczy (np. z pliku .p12); karty widzi tylko PKCS#11.
            foreach (var c in MagazynCertyfikatow.PobierzCertyfikatyDoPodpisu())
            {
                if (wszystkie.All(w => w.Odcisk != c.Odcisk))
                    wszystkie.Add(c);
            }
        }
        catch (Exception e)
        {
            bledy.Add("Pęk kluczy: " + KomunikatyBledow.Opisz(e));
        }
        // Ten sam certyfikat z dwóch bibliotek (np. sterownik producenta i OpenSC) — raz.
        var unikalne = wszystkie.GroupBy(c => c.Odcisk).Select(g => g.First());
        return (MagazynCertyfikatow.Uporzadkuj(unikalne), bledy, biblioteki.Count);
    }

    /// <summary>Operacje na karcie jedna naraz (biblioteka sterownika jest wczytywana na czas operacji).</summary>
    internal static readonly object BlokadaKarty = new();

    private void UstawCertyfikaty(IReadOnlyList<CertyfikatInfo> certyfikaty, string? odciskDoZaznaczenia)
    {
        var elementy = certyfikaty.Select(c => new ElementCertyfikatu(c)).ToList();
        ListaCertyfikatow.ItemsSource = elementy;
        ListaCertyfikatow.SelectedItem =
            elementy.FirstOrDefault(e => string.Equals(e.Certyfikat.Odcisk, odciskDoZaznaczenia, StringComparison.OrdinalIgnoreCase))
            ?? elementy.FirstOrDefault();
        BrakCertyfikatow.IsVisible = elementy.Count == 0;
        OdswiezStan();
    }

    private async void PrzyOdswiezeniu(object? sender, RoutedEventArgs e) => await WczytajCertyfikaty();

    private void PrzyWyborzeCertyfikatu(object? sender, SelectionChangedEventArgs e) => OdswiezStan();

    private void PrzyDwukliku(object? sender, TappedEventArgs e)
    {
        if (WybranyCertyfikat != null && _plik != null)
            Podpisz();
    }

    // ---- Dokument ----

    private async Task<bool> WybierzPlik()
    {
        var pliki = await _okno.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Wybierz dokument PDF",
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("Dokument PDF") { Patterns = new[] { "*.pdf" }, AppleUniformTypeIdentifiers = new[] { "com.adobe.pdf" } } },
        });
        var sciezka = pliki.Select(p => p.TryGetLocalPath()).FirstOrDefault(p => p != null);
        if (sciezka == null)
            return false;
        UstawPlik(sciezka);
        return _plik != null;
    }

    internal async void WybierzPlikZMenu()
    {
        if (Zajeta || !ReferenceEquals(_okno.Tresc.Content, this))
            return;
        await WybierzPlik();
    }

    internal void PodpiszZMenu()
    {
        if (!Zajeta && ReferenceEquals(_okno.Tresc.Content, this))
            Podpisz();
    }

    private async void PrzyWyborzePliku(object? sender, RoutedEventArgs e)
    {
        if (Zajeta)
            return;
        await WybierzPlik();
    }

    private void PrzyUsunieciuPliku(object? sender, RoutedEventArgs e) => UstawPlik(null);

    private void UstawPlik(string? sciezka)
    {
        if (sciezka != null && !File.Exists(sciezka))
        {
            _ = Dialogi.BladAsync(_okno, $"Nie znaleziono pliku:\n{sciezka}");
            sciezka = null;
        }
        _plik = sciezka;
        BezPliku.IsVisible = _plik == null;
        ZPlikiem.IsVisible = _plik != null;
        Miniatura.Source = null;
        if (_plik != null)
        {
            NazwaPliku.Text = Path.GetFileName(_plik);
            var rozmiar = Obrazy.Rozmiar(new FileInfo(_plik).Length);
            var folder = Path.GetDirectoryName(_plik);
            OpisPliku.Text = $"{rozmiar}  ·  {folder}";
            ToolTip.SetTip(NazwaPliku, _plik);
            _ = WczytajMiniature(_plik, rozmiar, folder);
        }
        OdswiezStan();
    }

    private async Task WczytajMiniature(string plik, string rozmiar, string? folder)
    {
        var numer = ++_numerMiniatury;
        try
        {
            var pikseli = 70 * (TopLevel.GetTopLevel(this)?.RenderScaling ?? 2) * 1.5;
            var (obraz, strony) = await WatekRoboczy.Uruchom(() =>
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

    private void PrzyPrzeciaganiu(object? sender, DragEventArgs e)
    {
#pragma warning disable CS0618 // GetFiles — w Avalonii 11 bez zamiennika dla przeciągania z Findera
        e.DragEffects = !Zajeta && e.Data.GetFiles()?.Any() == true ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private async void PrzyUpuszczeniu(object? sender, DragEventArgs e)
    {
        if (Zajeta)
            return;
        var pdf = e.Data.GetFiles()?.Select(f => f.TryGetLocalPath())
            .FirstOrDefault(p => p != null && string.Equals(Path.GetExtension(p), ".pdf", StringComparison.OrdinalIgnoreCase));
#pragma warning restore CS0618
        if (pdf != null)
            UstawPlik(pdf);
        else
            await Dialogi.BladAsync(_okno, "Przeciągnij plik PDF.");
    }

    // ---- Ustawienia ----

    private void PrzyUstawieniach(object? sender, RoutedEventArgs e) => _okno.OtworzUstawienia();

    private void OdswiezInformacje()
    {
        var u = Ustawienia;
        var serwer = Uri.TryCreate(u.AdresTsa, UriKind.Absolute, out var uri) ? uri.Host : u.AdresTsa;
        TytulInformacji.Text = $"PAdES {(u.PoziomLT ? "B-LT" : "B-T")}";
        OpisInformacji.Text = $"Znacznik czasu: {serwer}  ·  wizualizacja Arial, imię i nazwisko {Wizualizacja.RozmiarImieniaINazwiska:0.#} pt" +
                              (string.IsNullOrEmpty(u.SciezkaLogo) ? "" : " z logo");
    }

    // ---- Podpis ----

    private void PrzyPodpisie(object? sender, RoutedEventArgs e) => Podpisz();

    private async void Podpisz()
    {
        if (Zajeta)
            return;
        _przebiegPodpisu = true;
        UstawPostep(true);
        try
        {
            await PodpiszAsync();
        }
        catch (OperationCanceledException)
        {
            UstawStatus("Podpisywanie anulowane.");
        }
        catch (Exception e)
        {
            UstawStatus("Nie podpisano dokumentu.");
            await Dialogi.BladAsync(_okno, KomunikatyBledow.Opisz(e), "Nie podpisano dokumentu");
        }
        finally
        {
            _etapKrytyczny = false;
            _przebiegPodpisu = false;
            UstawPostep(false);
        }
    }

    private async Task PodpiszAsync()
    {
        if (_plik == null && !await WybierzPlik())
            return;
        if (_plik == null)
            return;
        var certyfikat = WybranyCertyfikat;
        if (certyfikat == null)
        {
            await Dialogi.BladAsync(_okno, "Wybierz certyfikat z listy. Jeśli lista jest pusta, włóż kartę i kliknij „Odśwież”.");
            return;
        }
        if (CertyfikatInfo.PowodNiewaznosci(certyfikat.Certyfikat, DateTime.Now) is { } niewazny)
        {
            await Dialogi.BladAsync(_okno, niewazny, "Certyfikat nieważny");
            await WczytajCertyfikaty();
            return;
        }
        if (!certyfikat.Kwalifikowany &&
            await Dialogi.PokazAsync(_okno, "Certyfikat niekwalifikowany",
                "Ten certyfikat nie jest kwalifikowany — podpis nie będzie kwalifikowanym podpisem elektronicznym. Kontynuować?",
                "Anuluj", "Kontynuuj") != 1)
            return;

        var wejscie = _plik;
        var ustawienia = Ustawienia;
        var wyjscie = ustawienia.PlikWynikowy(wejscie);
        if (Path.GetFullPath(wyjscie) != Path.GetFullPath(wejscie) && File.Exists(wyjscie) &&
            await Dialogi.PokazAsync(_okno, "Plik już istnieje", $"Plik {Path.GetFileName(wyjscie)} już istnieje. Zastąpić go?",
                "Anuluj", "Zastąp") != 1)
            return;

        // Podgląd: użytkownik wskazuje miejsce wizualizacji.
        UstawStatus("Otwieranie podglądu…");
        var dane = new DaneWizualizacji(certyfikat.ImieNazwisko, ustawienia.Stanowisko, ustawienia.SciezkaLogo, DateTimeOffset.Now,
            ustawienia.MotywWizualizacji, certyfikat.Kwalifikowany);
        var zadanie = await WatekRoboczy.Uruchom(() => ZadaniePodgladu.Przygotuj(wejscie, dane, ustawienia.Polozenie, 1f));
        UstawStatus("Wskaż na podglądzie miejsce podpisu.");
        _okno.PokazPodglad(new StronaPodgladu(_okno, zadanie), wejscie);
        var miejsce = await zadanie.Wynik.Task;
        if (miejsce == null)
        {
            UstawStatus("Podpisywanie anulowane.");
            return;
        }

        var opcje = ustawienia.UtworzOpcjePodpisu();
        opcje.Miejsce = miejsce;
        SesjaKarty? sesja = null;
        try
        {
            if (certyfikat.Karta is { } karta)
            {
                sesja = await ZalogujDoKarty(certyfikat, karta);
                if (sesja == null)
                {
                    UstawStatus("Podpisywanie anulowane.");
                    return;
                }
                opcje.PodpisZewnetrzny = sesja;
                opcje.CertyfikatyWystawcow = sesja.CertyfikatyWystawcow;
            }

            UstawStatus(certyfikat.Karta == null ? "Podpisywanie… macOS może zapytać o dostęp do klucza w pęku kluczy." : "Podpisywanie…");
            _etapKrytyczny = true;
            var wynik = await WatekRoboczy.Uruchom(() =>
            {
                lock (BlokadaKarty)
                    return new PodpisywaczPdf(ustawienia.UtworzKlientaTsa()).Podpisz(wejscie, wyjscie, certyfikat.Certyfikat, opcje);
            });
            _etapKrytyczny = false;

            Ustawienia.OstatniCertyfikat = certyfikat.Odcisk;
            ZapiszCicho();
            UstawStatus($"Podpisano: {wynik.SciezkaWyniku}");
            var zrodloCzasu = wynik.CzasZSerwera ? "z serwera znacznika czasu" : "z zegara komputera — serwer nie odpowiedział przed podpisem";
            var decyzja = await Dialogi.PokazAsync(_okno, "Dokument został podpisany",
                $"Zapisano jako:\n{wynik.SciezkaWyniku}\n\n" +
                $"Czas na wizualizacji: {wynik.CzasNaWizualizacji.ToLocalTime():yyyy-MM-dd HH:mm} ({zrodloCzasu}).\n" +
                "Wiążący czas podpisu zawiera znacznik czasu.", "Zamknij", "Pokaż w Finderze");
            if (decyzja == 1)
                PokazWFinderze(wynik.SciezkaWyniku);
        }
        finally
        {
            if (sesja != null)
            {
                lock (BlokadaKarty)
                    sesja.Dispose();
            }
        }
    }

    /// <summary>
    /// PIN w oknie programu (albo na klawiaturze czytnika) i logowanie do karty; przy błędnym PIN-ie pyta ponownie.
    /// Zwraca null po anulowaniu.
    /// </summary>
    private async Task<SesjaKarty?> ZalogujDoKarty(CertyfikatInfo certyfikat, ZrodloPkcs11 karta)
    {
        var stan = await WatekRoboczy.Uruchom(() =>
        {
            lock (BlokadaKarty)
                return KartaPkcs11.SprawdzPin(karta);
        });
        if (stan == StanPinu.Zablokowany)
            throw new BladPinu("PIN karty jest zablokowany. Odblokuj go kodem PUK w programie producenta karty.", stan);

        string? komunikat = stan switch
        {
            StanPinu.OstatniaProba => "Uwaga: została ostatnia próba — błędny PIN zablokuje kartę.",
            StanPinu.MaloProb => "Uwaga: zostało niewiele prób przed zablokowaniem karty.",
            _ => null,
        };
        while (true)
        {
            string? pin = null;
            if (karta.CzytnikZKlawiatura)
            {
                UstawStatus("Wpisz PIN na klawiaturze czytnika.");
            }
            else
            {
                pin = await OknoPin.ZapytajAsync(_okno, karta.EtykietaTokenu, certyfikat.ImieNazwisko, komunikat);
                if (pin == null)
                    return null;
                UstawStatus("Logowanie do karty…");
            }
            try
            {
                return await WatekRoboczy.Uruchom(() =>
                {
                    lock (BlokadaKarty)
                        return KartaPkcs11.Otworz(karta, certyfikat.Certyfikat, pin);
                });
            }
            catch (BladPinu e) when (e.Stan != StanPinu.Zablokowany && !karta.CzytnikZKlawiatura)
            {
                komunikat = e.Message;
            }
        }
    }

    private void ZapiszCicho()
    {
        try { _okno.ZapiszUstawienia(); } catch (Exception) { /* zapamiętanie wyboru nie jest krytyczne */ }
    }

    private static void PokazWFinderze(string sciezka)
    {
        try
        {
            Process.Start(new ProcessStartInfo("open", new[] { "-R", sciezka }) { UseShellExecute = false });
        }
        catch (Exception)
        {
            // Brak Findera nie jest błędem podpisu.
        }
    }

    internal static void Otworz(string adres)
    {
        try
        {
            Process.Start(new ProcessStartInfo("open", new[] { adres }) { UseShellExecute = false });
        }
        catch (Exception)
        {
            // Bez przeglądarki — adres widać w komunikacie.
        }
    }

    private void UstawPostep(bool trwa)
    {
        Postep.IsVisible = trwa;
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
