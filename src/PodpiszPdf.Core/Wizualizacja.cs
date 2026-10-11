using System.Globalization;
using System.Text;
using iText.Forms.Form.Element;
using iText.IO.Font;
using iText.IO.Image;
using iText.Kernel.Colors;
using iText.Kernel.Font;
using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Xobject;
using iText.Layout.Borders;
using iText.Layout.Element;
using iText.Layout.Properties;
using iText.Signatures;
using Path = System.IO.Path;

namespace PodpiszPdf.Core;

/// <summary>Wygląd wizualizacji podpisu (wybór w ustawieniach).</summary>
public enum MotywWizualizacji
{
    /// <summary>Wszystkie wiersze czarne; nagłówek, stanowisko (7–9 pt) i data podobnej wielkości.</summary>
    Klasyczny,
    /// <summary>Nagłówek i data mniejsze i szare, stanowisko 9 pt czarne.</summary>
    Hierarchia,
    /// <summary>Jak <see cref="Hierarchia"/>, z pionową kreską między logo a tekstem; data bez strefy czasowej.</summary>
    Linia,
    /// <summary>Nagłówek wersalikami z rozstrzeleniem, imię i nazwisko w granacie.</summary>
    Akcent,
    /// <summary>Data pod poziomą kreską, z informacją o rodzaju podpisu (kwalifikowany).</summary>
    DataPodKreska,
}

/// <summary>Dane pokazywane w prostokącie podpisu.</summary>
/// <param name="Kwalifikowany">Certyfikat kwalifikowany — motyw <see cref="MotywWizualizacji.DataPodKreska"/> pisze wtedy
/// „Kwalifikowany podpis elektroniczny”.</param>
public sealed record DaneWizualizacji(string ImieNazwisko, string? Stanowisko, string? SciezkaLogo, DateTimeOffset Czas,
    MotywWizualizacji Motyw = MotywWizualizacji.Klasyczny, bool Kwalifikowany = false);

/// <summary>Wymiary wizualizacji w punktach PDF.</summary>
public readonly record struct RozmiarWizualizacji(float Szerokosc, float Wysokosc)
{
    public float Proporcje => Szerokosc / Wysokosc;
    public float SzerokoscMm => Szerokosc / Wizualizacja.PunktowNaMm;
    public float WysokoscMm => Wysokosc / Wizualizacja.PunktowNaMm;
}

/// <summary>Budowa widocznej wizualizacji podpisu: imię i nazwisko, stanowisko, logo, data i godzina.</summary>
public static class Wizualizacja
{
    /// <summary>Obsługiwane formaty logo: wektorowe (SVG, PDF) i rastrowe (PNG, JPG).</summary>
    public static IReadOnlyList<string> RozszerzeniaLogo => Logo.Rozszerzenia;

    public const float PunktowNaMm = 72f / 25.4f;

    /// <summary>
    /// Zakres skali przy rysowaniu podpisu na podglądzie. Skala 1 = rozmiar podstawowy: imię i nazwisko
    /// Arial 11 pt, nagłówek i data 8 pt; prostokąt wynika z treści.
    /// </summary>
    public const float MinSkala = 0.6f;
    public const float MaksSkala = 3f;

    // Rozmiary czcionek (pt) przy skali 1.
    private const float RozmiarNazwiska = 11f;
    private const float RozmiarNaglowka = 8f;
    private const float RozmiarDaty = 8f;
    // Stanowisko: jak największe w tym zakresie, byle nie szersze od pozostałych wierszy.
    private const float MaksRozmiarStanowiska = 9f;
    private const float MinRozmiarStanowiska = 7f;
    private const int MaksWierszyStanowiska = 2;

    private const float Odstep = 2f;
    private const float OdstepLogo = 6f;
    // Logo nie szersze niż blok tekstu.
    private const float MaksUdzialLogo = 1f;
    private const float InterliniaWzgledna = 1.1f;
    // Położenie glifów w wierszu (ułamki rozmiaru czcionki), zmierzone dla Ariala przy interlinii 1,1:
    // od góry wiersza do górnej krawędzi wielkich liter i od dołu wiersza do dolnej krawędzi znaków.
    private const float GornyLuzWiersza = 0.23f;
    private const float DolnyLuzWiersza = 0.10f;

    internal const string Naglowek = "Podpisano elektronicznie przez:";

    // Motywy inne niż klasyczny: stanowisko w stałym rozmiarze, zawijane dopiero powyżej tej szerokości (pt).
    private const float StalyRozmiarStanowiska = 9f;
    private const float SzerokoscZawijaniaStanowiska = 170f;
    // Kreska nad datą (motyw „data pod kreską”): odstęp nad kreską, grubość, odstęp pod nią.
    private const float OdstepNadKreska = 2.5f;
    private const float GruboscKreski = 0.5f;
    private const float OdstepPodKreska = 2f;
    // Kreska między logo a tekstem (motyw „linia”): grubość i odstęp od tekstu.
    private const float GruboscLinii = 0.75f;
    private const float OdstepOdLinii = 5f;

    private static readonly Color Czarny = ColorConstants.BLACK;
    private static readonly Color Szary = new DeviceRgb(0x59, 0x59, 0x59);
    private static readonly Color SzaryKreski = new DeviceRgb(0x9A, 0x9A, 0x9A);
    private static readonly Color Grafitowy = new DeviceRgb(0x22, 0x22, 0x22);
    private static readonly Color PrawieCzarny = new DeviceRgb(0x1F, 0x1F, 0x1F);
    private static readonly Color Granatowy = new DeviceRgb(0x1B, 0x3A, 0x5C);

    /// <summary>Wygląd poszczególnych wierszy w motywie.</summary>
    private sealed record Styl(string TekstNaglowka, float RozmiarNaglowka, float OdstepZnakowNaglowka, Color KolorNaglowka,
        Color KolorNazwiska, Color KolorStanowiska, bool StaleStanowisko, float RozmiarDaty, Color KolorDaty,
        bool KreskaNadData, bool LiniaPrzyLogo)
    {
        public static Styl Dla(MotywWizualizacji motyw) => motyw switch
        {
            MotywWizualizacji.Hierarchia => new(Naglowek, 7f, 0, Szary, Czarny, Czarny, true, 7f, Szary, false, false),
            MotywWizualizacji.Linia => new(Naglowek, 7f, 0, Szary, Czarny, Grafitowy, true, 7f, Szary, false, true),
            MotywWizualizacji.Akcent => new("PODPISANO ELEKTRONICZNIE", 6.5f, 0.6f, Szary, Granatowy, PrawieCzarny, true, 7f, Szary, false, false),
            MotywWizualizacji.DataPodKreska => new(Naglowek, 7f, 0, Szary, Czarny, Czarny, true, 6.5f, Szary, true, false),
            _ => new(Naglowek, Wizualizacja.RozmiarNaglowka, 0, Czarny, Czarny, Czarny, false, Wizualizacja.RozmiarDaty, Czarny, false, false),
        };
    }

    /// <summary>Wiersz daty w motywie.</summary>
    internal static string TekstDaty(DaneWizualizacji dane) => dane.Motyw switch
    {
        MotywWizualizacji.Klasyczny => "Data: " + FormatujCzas(dane.Czas),
        MotywWizualizacji.Linia => "Data podpisu: " + FormatujCzasPolski(dane.Czas, zeStrefa: false),
        MotywWizualizacji.DataPodKreska => (dane.Kwalifikowany ? "Kwalifikowany podpis elektroniczny" : "Podpis elektroniczny") +
                                           " · " + FormatujCzasPolski(dane.Czas, zeStrefa: true),
        _ => FormatujCzasPolski(dane.Czas, zeStrefa: true),
    };
    // Zapas szerokości bloku tekstu na zaokrąglenia układu, żeby wiersz się nie zawinął.
    private const float ZapasSzerokosci = 1.02f;
    // Tolerancja przy sprawdzaniu, czy prostokąt mieści się na stronie.
    private const float Tolerancja = 0.5f;

    /// <summary>Rozmiar wizualizacji (pt) przy skali 1 — wynika z treści.</summary>
    public static RozmiarWizualizacji Rozmiar(DaneWizualizacji dane)
    {
        var u = Zaplanuj(dane, out _, out _);
        return new RozmiarWizualizacji(u.Szerokosc, u.Wysokosc);
    }

    /// <summary>Prostokąt wizualizacji wg położenia domyślnego (narożnik i odległość od krawędzi).</summary>
    public static Rectangle ObliczProstokat(Rectangle strona, PolozenieWizualizacji p, RozmiarWizualizacji rozmiar, float skala = 1f)
    {
        var w = rozmiar.Szerokosc * skala;
        var h = rozmiar.Wysokosc * skala;
        SprawdzRozmiarStrony(strona, w, h, naPodgladzie: false);
        var mx = p.MarginesPoziomyMm * PunktowNaMm;
        var my = p.MarginesPionowyMm * PunktowNaMm;

        var lewy = p.Naroznik is Naroznik.LewyDolny or Naroznik.LewyGorny;
        var dolny = p.Naroznik is Naroznik.LewyDolny or Naroznik.PrawyDolny;

        var x = lewy ? strona.GetLeft() + mx : strona.GetRight() - mx - w;
        var y = dolny ? strona.GetBottom() + my : strona.GetTop() - my - h;

        var prostokat = new Rectangle(x, y, w, h);
        if (!MiesciSie(strona, prostokat))
        {
            throw new BladPodpisu(
                "Wizualizacja podpisu nie mieści się na stronie. Zmniejsz odległość od krawędzi w ustawieniach.");
        }
        return prostokat;
    }

    /// <summary>Prostokąt wizualizacji w miejscu wskazanym na podglądzie.</summary>
    public static Rectangle ProstokatWMiejscu(Rectangle strona, MiejscePodpisu miejsce, RozmiarWizualizacji rozmiar)
    {
        if (miejsce.Skala < MinSkala - 0.001f || miejsce.Skala > MaksSkala + 0.001f)
            throw new BladPodpisu("Nieprawidłowy rozmiar podpisu. Wskaż miejsce ponownie.");
        var prostokat = new Rectangle(miejsce.X, miejsce.Y, rozmiar.Szerokosc * miejsce.Skala, rozmiar.Wysokosc * miejsce.Skala);
        SprawdzRozmiarStrony(strona, prostokat.GetWidth(), prostokat.GetHeight(), naPodgladzie: true);
        if (!MiesciSie(strona, prostokat))
            throw new BladPodpisu("Wskazane miejsce podpisu wychodzi poza stronę. Wskaż miejsce ponownie.");
        return prostokat;
    }

    /// <summary>
    /// Lewy dolny róg wizualizacji tak, aby jej środek był w punkcie (x, y), ale cała zmieściła się na stronie.
    /// </summary>
    public static (float X, float Y) DopasujDoStrony(Rectangle strona, float srodekX, float srodekY, RozmiarWizualizacji rozmiar, float skala = 1f)
    {
        var w = rozmiar.Szerokosc * skala;
        var h = rozmiar.Wysokosc * skala;
        var x = Math.Clamp(srodekX - w / 2, strona.GetLeft(), Math.Max(strona.GetLeft(), strona.GetRight() - w));
        var y = Math.Clamp(srodekY - h / 2, strona.GetBottom(), Math.Max(strona.GetBottom(), strona.GetTop() - h));
        return (x, y);
    }

    /// <summary>
    /// Wizualizacja większa niż cała strona (np. bardzo długie stanowisko) — żadne położenie nie pomoże,
    /// więc komunikat wskazuje prawdziwą przyczynę.
    /// </summary>
    private static void SprawdzRozmiarStrony(Rectangle strona, float w, float h, bool naPodgladzie)
    {
        var rada = naPodgladzie ? " lub zmniejsz podpis na podglądzie" : "";
        if (w > strona.GetWidth() + Tolerancja)
        {
            throw new BladPodpisu(
                $"Wizualizacja podpisu jest szersza niż strona — skróć stanowisko w ustawieniach (Enter dzieli je na wiersze){rada}.");
        }
        if (h > strona.GetHeight() + Tolerancja)
            throw new BladPodpisu($"Wizualizacja podpisu jest wyższa niż strona — skróć stanowisko w ustawieniach{rada}.");
    }

    private static bool MiesciSie(Rectangle strona, Rectangle p) =>
        p.GetLeft() >= strona.GetLeft() - Tolerancja && p.GetBottom() >= strona.GetBottom() - Tolerancja &&
        p.GetRight() <= strona.GetRight() + Tolerancja && p.GetTop() <= strona.GetTop() + Tolerancja;

    /// <summary>Wygląd pola podpisu: bez ramki i z przezroczystym tłem.</summary>
    /// <param name="skala">Skala całej wizualizacji (rozmiar, czcionki, odstępy); proporcje pozostają stałe.</param>
    /// <param name="bezPrzezroczystosci">Dokument PDF/A-1 — logo bez przezroczystości (zob. <see cref="Logo.Wczytaj"/>).</param>
    internal static SignatureFieldAppearance Utworz(DaneWizualizacji dane, float skala = 1f, bool bezPrzezroczystosci = false)
    {
        var wyglad = new SignatureFieldAppearance(SignerProperties.IGNORED_ID);
        wyglad.SetContent(UtworzZawartosc(dane, skala, out var czcionka, bezPrzezroczystosci));
        wyglad.SetBorder(Border.NO_BORDER);
        foreach (var bok in new[] { Property.PADDING_LEFT, Property.PADDING_RIGHT, Property.PADDING_TOP, Property.PADDING_BOTTOM })
            wyglad.SetProperty(bok, UnitValue.CreatePointValue(0));
        wyglad.SetFont(czcionka);
        return wyglad;
    }

    /// <summary>
    /// Jednostronicowy PDF o rozmiarze wizualizacji z jej zawartością — do pokazania na podglądzie
    /// dokładnie tego, co znajdzie się w podpisie.
    /// </summary>
    public static byte[] PodgladPdf(DaneWizualizacji dane, float skala = 1f)
    {
        var rozmiar = Rozmiar(dane);
        using var ms = new MemoryStream();
        using (var pdf = new iText.Kernel.Pdf.PdfDocument(new iText.Kernel.Pdf.PdfWriter(ms)))
        {
            using var dokument = new iText.Layout.Document(pdf, new PageSize(rozmiar.Szerokosc * skala, rozmiar.Wysokosc * skala));
            dokument.SetMargins(0, 0, 0, 0);
            dokument.Add(UtworzZawartosc(dane, skala, out _));
        }
        return ms.ToArray();
    }

    /// <summary>Układ wizualizacji przy skali 1 (pt).</summary>
    private sealed record Uklad(List<Wiersz> Linie, float TekstW, float LogoW, float LogoH, float LogoHPelne, float Szerokosc, float Wysokosc,
        (PdfXObject Obraz, float Proporcje)? Logo, Styl Styl, float WciecieTekstu);

    /// <summary>
    /// Wiersze, rozmiary czcionek i wymiary przy skali 1. Szerokość wyznacza najszerszy wiersz,
    /// wysokość — suma wierszy (lub logo); wszystko jest potem mnożone przez skalę.
    /// </summary>
    private static Uklad Zaplanuj(DaneWizualizacji dane, out PdfFont zwykla, out PdfFont pogrubiona, bool bezPrzezroczystosci = false)
    {
        (zwykla, pogrubiona) = Czcionki();
        SprawdzZnaki(dane.ImieNazwisko, pogrubiona, "Imię i nazwisko z certyfikatu zawiera", "Nie da się ich pokazać na wizualizacji podpisu.");
        SprawdzZnaki(dane.Stanowisko, zwykla, "Stanowisko zawiera", "Usuń je ze stanowiska w ustawieniach.");
        var styl = Styl.Dla(dane.Motyw);
        var naglowek = new Wiersz(styl.TekstNaglowka, zwykla, styl.RozmiarNaglowka, styl.KolorNaglowka, styl.OdstepZnakowNaglowka);
        var nazwisko = new Wiersz(dane.ImieNazwisko, pogrubiona, RozmiarNazwiska, styl.KolorNazwiska);
        var data = new Wiersz(TekstDaty(dane), zwykla, styl.RozmiarDaty, styl.KolorDaty, KreskaNad: styl.KreskaNadData);

        var bazowa = new[] { naglowek, nazwisko, data }.Max(w => Szerokosc(w));
        var (stanowisko, szerokoscStanowiska) = styl.StaleStanowisko
            ? UlozStanowiskoStale(WierszeStanowiska(dane.Stanowisko), zwykla, Math.Max(bazowa, SzerokoscZawijaniaStanowiska))
            : UlozStanowisko(WierszeStanowiska(dane.Stanowisko), zwykla, bazowa);

        var linie = new List<Wiersz> { naglowek, nazwisko };
        linie.AddRange(stanowisko.Select(w => w with { Kolor = styl.KolorStanowiska }));
        linie.Add(data);
        var tekstW = Math.Max(bazowa, szerokoscStanowiska);
        var tekstH = linie.Sum(w => WysokoscWiersza(w.Czcionka, w.Rozmiar) + (w.KreskaNad ? OdstepNadKreska + GruboscKreski + OdstepPodKreska : 0));

        var logo = WczytajLogo(dane.SciezkaLogo, bezPrzezroczystosci);
        float logoW = 0, logoH = 0, logoHPelne = 0, odstepLogo = 0;
        if (logo != null)
        {
            // Logo ma wysokość widocznego tekstu: od góry wielkich liter nagłówka do dołu daty.
            logoHPelne = tekstH - GornyLuzWiersza * linie[0].Rozmiar - DolnyLuzWiersza * linie[^1].Rozmiar;
            logoW = Math.Min(logoHPelne * logo.Value.Proporcje, tekstW * MaksUdzialLogo);
            logoH = logoW / logo.Value.Proporcje;
            odstepLogo = OdstepLogo;
        }
        // Kreska między logo a tekstem — tylko gdy jest logo.
        var wciecie = logo != null && styl.LiniaPrzyLogo ? GruboscLinii + OdstepOdLinii : 0;
        var szerokosc = 2 * Odstep + logoW + odstepLogo + wciecie + tekstW * ZapasSzerokosci;
        var wysokosc = 2 * Odstep + tekstH;
        return new Uklad(linie, tekstW * ZapasSzerokosci, logoW, logoH, logoHPelne, szerokosc, wysokosc, logo, styl, wciecie);
    }

    /// <summary>
    /// Stanowisko: największy rozmiar (9 → 7 pt), przy którym nie jest szersze od pozostałych wierszy.
    /// Podział wpisany przez użytkownika (Enter) jest zachowany; bez niego długie stanowisko zawija się
    /// do dwóch wierszy. Gdy i to nie wystarcza, wizualizacja się poszerza.
    /// </summary>
    private static (List<Wiersz> Wiersze, float Szerokosc) UlozStanowisko(List<string> wiersze, PdfFont czcionka, float szerokosc)
    {
        if (wiersze.Count == 0)
            return (new(), 0);
        var zawijaj = wiersze.Count == 1;
        for (var r = MaksRozmiarStanowiska; r >= MinRozmiarStanowiska - 0.001f; r -= 0.25f)
        {
            var ulozone = zawijaj ? Zawin(wiersze[0], czcionka, r, szerokosc) : wiersze;
            if (ulozone.Count <= (zawijaj ? MaksWierszyStanowiska : wiersze.Count) &&
                ulozone.All(w => czcionka.GetWidth(w, r) <= szerokosc))
                return (ulozone.Select(w => new Wiersz(w, czcionka, r)).ToList(), szerokosc);
        }

        // Za długie: najmniejsza czcionka i szerszy blok (pojedyncze stanowisko — dwa możliwie równe wiersze).
        var min = MinRozmiarStanowiska;
        var wynik = wiersze;
        if (zawijaj)
        {
            var w = Math.Max(szerokosc, czcionka.GetWidth(wiersze[0], min) / MaksWierszyStanowiska);
            while ((wynik = Zawin(wiersze[0], czcionka, min, w)).Count > MaksWierszyStanowiska)
                w += 2;
        }
        return (wynik.Select(t => new Wiersz(t, czcionka, min)).ToList(), wynik.Max(t => czcionka.GetWidth(t, min)));
    }

    /// <summary>
    /// Stanowisko w stałym rozmiarze 9 pt (motywy inne niż klasyczny): podział wpisany przez użytkownika
    /// jest zachowany, bez niego stanowisko szersze niż <paramref name="szerokosc"/> zawija się do dwóch
    /// wierszy; dłuższe poszerza wizualizację.
    /// </summary>
    private static (List<Wiersz> Wiersze, float Szerokosc) UlozStanowiskoStale(List<string> wiersze, PdfFont czcionka, float szerokosc)
    {
        if (wiersze.Count == 0)
            return (new(), 0);
        var r = StalyRozmiarStanowiska;
        var wynik = wiersze;
        if (wiersze.Count == 1)
        {
            var w = Math.Max(szerokosc, czcionka.GetWidth(wiersze[0], r) / MaksWierszyStanowiska);
            while ((wynik = Zawin(wiersze[0], czcionka, r, w)).Count > MaksWierszyStanowiska)
                w += 2;
        }
        return (wynik.Select(t => new Wiersz(t, czcionka, r)).ToList(), wynik.Max(t => czcionka.GetWidth(t, r)));
    }

    /// <summary>Zawinięcie tekstu na granicach słów; słowo dłuższe od szerokości zostaje w swoim wierszu.</summary>
    internal static List<string> Zawin(string tekst, PdfFont czcionka, float rozmiar, float szerokosc)
    {
        var wiersze = new List<string>();
        var biezacy = "";
        foreach (var slowo in tekst.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var proba = biezacy.Length == 0 ? slowo : biezacy + " " + slowo;
            if (biezacy.Length > 0 && czcionka.GetWidth(proba, rozmiar) > szerokosc)
            {
                wiersze.Add(biezacy);
                biezacy = slowo;
            }
            else
            {
                biezacy = proba;
            }
        }
        if (biezacy.Length > 0)
            wiersze.Add(biezacy);
        return wiersze;
    }

    private static float Szerokosc(Wiersz w) => w.Czcionka.GetWidth(w.Tekst, w.Rozmiar) + w.OdstepZnakow * w.Tekst.Length;

    /// <summary>
    /// Zawartość wizualizacji: układ ze skali 1 pomnożony przez skalę, więc wizualizacja w innej skali
    /// jest dokładnym powiększeniem lub pomniejszeniem.
    /// </summary>
    private static Div UtworzZawartosc(DaneWizualizacji dane, float skala, out PdfFont czcionkaPodstawowa, bool bezPrzezroczystosci = false)
    {
        var u = Zaplanuj(dane, out czcionkaPodstawowa, out _, bezPrzezroczystosci);
        var tekst = new Div().SetPadding(0).SetMargin(0).SetWidth(u.TekstW * skala);
        foreach (var w in u.Linie)
        {
            var akapit = new Paragraph(w.Tekst)
                .SetFont(w.Czcionka)
                .SetFontSize(w.Rozmiar * skala)
                .SetFontColor(w.Kolor ?? Czarny)
                .SetMultipliedLeading(InterliniaWzgledna)
                .SetMargin(0)
                .SetPadding(0);
            if (w.OdstepZnakow > 0)
                akapit.SetCharacterSpacing(w.OdstepZnakow * skala);
            if (w.KreskaNad)
            {
                akapit.SetBorderTop(new SolidBorder(SzaryKreski, GruboscKreski * skala))
                    .SetMarginTop(OdstepNadKreska * skala)
                    .SetPaddingTop(OdstepPodKreska * skala);
            }
            tekst.Add(akapit);
        }

        var zawartosc = new Div()
            .SetMargin(0)
            .SetPadding(Odstep * skala)
            // Wymiary bez marginesu wewnętrznego (iText dolicza go do szerokości i wysokości).
            .SetWidth((u.Szerokosc - 2 * Odstep) * skala)
            .SetHeight((u.Wysokosc - 2 * Odstep) * skala)
            .SetVerticalAlignment(VerticalAlignment.MIDDLE);

        if (u.Logo is not { } logo)
        {
            zawartosc.Add(tekst);
            return zawartosc;
        }

        var tabela = new Table(UnitValue.CreatePointArray(new[] { (u.LogoW + OdstepLogo) * skala, (u.WciecieTekstu + u.TekstW) * skala }))
            .SetFixedLayout()
            .SetWidth((u.LogoW + OdstepLogo + u.WciecieTekstu + u.TekstW) * skala)
            .SetBorder(Border.NO_BORDER)
            .SetMargin(0);
        var obraz = (logo.Obraz switch
        {
            PdfFormXObject f => new Image(f),
            var x => new Image((PdfImageXObject)x),
        }).SetWidth(u.LogoW * skala).SetHeight(u.LogoH * skala);
        // Logo zaczyna się na wysokości górnej krawędzi wielkich liter nagłówka (węższe logo — pośrodku tekstu).
        var gora = GornyLuzWiersza * u.Linie[0].Rozmiar + (u.LogoHPelne - u.LogoH) / 2;
        tabela.AddCell(new Cell()
            .Add(obraz)
            .SetBorder(Border.NO_BORDER)
            .SetPadding(0)
            .SetPaddingTop(gora * skala)
            .SetPaddingRight(OdstepLogo * skala)
            .SetVerticalAlignment(VerticalAlignment.TOP));
        var komorkaTekstu = new Cell()
            .Add(tekst)
            .SetBorder(Border.NO_BORDER)
            .SetPadding(0)
            .SetVerticalAlignment(VerticalAlignment.TOP);
        if (u.WciecieTekstu > 0)
        {
            komorkaTekstu.SetBorderLeft(new SolidBorder(SzaryKreski, GruboscLinii * skala))
                .SetPaddingLeft(OdstepOdLinii * skala);
        }
        tabela.AddCell(komorkaTekstu);
        zawartosc.Add(tabela);
        return zawartosc;
    }

    /// <summary>
    /// Wiersze stanowiska wpisane przez użytkownika (Enter w ustawieniach = nowy wiersz).
    /// </summary>
    internal static List<string> WierszeStanowiska(string? stanowisko) =>
        (stanowisko ?? "")
            .Split('\n')
            .Select(w => w.Trim())
            .Where(w => w.Length > 0)
            .ToList();

    /// <summary>Data po polsku: „11.10.2026, 16:02” (opcjonalnie ze strefą czasową).</summary>
    internal static string FormatujCzasPolski(DateTimeOffset czas, bool zeStrefa)
    {
        var lokalny = czas.ToLocalTime();
        var tekst = lokalny.ToString("dd.MM.yyyy, HH:mm", CultureInfo.InvariantCulture);
        if (!zeStrefa)
            return tekst;
        var przesuniecie = lokalny.Offset;
        var znak = przesuniecie < TimeSpan.Zero ? "-" : "+";
        return tekst + $" (UTC{znak}{przesuniecie.Duration():hh\\:mm})";
    }

    internal static string FormatujCzas(DateTimeOffset czas)
    {
        var lokalny = czas.ToLocalTime();
        var przesuniecie = lokalny.Offset;
        var znak = przesuniecie < TimeSpan.Zero ? "-" : "+";
        return lokalny.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) +
               $" (UTC{znak}{przesuniecie.Duration():hh\\:mm})";
    }

    /// <summary>Wysokość wiersza tak, jak liczy ją iText: interlinia × (ascender − descender) czcionki.</summary>
    private static float WysokoscWiersza(PdfFont czcionka, float rozmiar)
    {
        var m = czcionka.GetFontProgram().GetFontMetrics();
        return InterliniaWzgledna * (m.GetAscender() - m.GetDescender()) / 1000f * rozmiar;
    }

    private sealed record Wiersz(string Tekst, PdfFont Czcionka, float Rozmiar, Color? Kolor = null, float OdstepZnakow = 0,
        bool KreskaNad = false);

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte[]> DaneCzcionek = new();

    /// <summary>
    /// Arial i Arial Bold z systemu Windows; gdzie ich brak (np. Linux w testach) — wbudowana Liberation Sans,
    /// metrycznie zgodna z Arialem (te same szerokości i wysokości znaków, więc ten sam układ).
    /// </summary>
    private static (PdfFont Zwykla, PdfFont Pogrubiona) Czcionki() =>
        (UtworzCzcionke("arial.ttf", "PodpiszPdf.Core.LiberationSans-Regular.ttf"),
         UtworzCzcionke("arialbd.ttf", "PodpiszPdf.Core.LiberationSans-Bold.ttf"));

    /// <summary>
    /// Nowa czcionka dla każdego dokumentu. Bez pamięci podręcznej iText: wspólny obiekt czcionki czyta
    /// plik ze wspólnego bufora i nie może być używany przez kilka wątków naraz (np. podpis i podgląd).
    /// </summary>
    private static PdfFont UtworzCzcionke(string plikSystemowy, string zasob)
    {
        var dane = DaneCzcionek.GetOrAdd(plikSystemowy, _ =>
        {
            try
            {
                var sciezka = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), plikSystemowy);
                if (OperatingSystem.IsWindows() && File.Exists(sciezka))
                    return File.ReadAllBytes(sciezka);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Wbudowana czcionka zastępcza.
            }
            using var strumien = typeof(Wizualizacja).Assembly.GetManifestResourceStream(zasob)
                                 ?? throw new InvalidOperationException($"Brak zasobu czcionki {zasob}.");
            using var ms = new MemoryStream();
            strumien.CopyTo(ms);
            return ms.ToArray();
        });
        var program = FontProgramFactory.CreateFont(dane, cached: false);
        return PdfFontFactory.CreateFont(program, PdfEncodings.IDENTITY_H, PdfFontFactory.EmbeddingStrategy.FORCE_EMBEDDED);
    }

    /// <summary>
    /// Znaki, których czcionka nie zawiera, iText po cichu pomija — zamiast niepełnego nazwiska na podpisie
    /// pokazujemy błąd z listą tych znaków.
    /// </summary>
    private static void SprawdzZnaki(string? tekst, PdfFont czcionka, string poczatek, string rada)
    {
        if (string.IsNullOrEmpty(tekst))
            return;
        var brakujace = tekst.EnumerateRunes()
            .Where(z => !Rune.IsControl(z) && !Rune.IsWhiteSpace(z) && !czcionka.ContainsGlyph(z.Value))
            .Select(z => z.ToString())
            .Distinct()
            .ToList();
        if (brakujace.Count == 0)
            return;
        var lista = string.Join(" ", brakujace.Take(10)) + (brakujace.Count > 10 ? " …" : "");
        throw new BladPodpisu($"{poczatek} znaki, których nie ma w czcionce podpisu (Arial): {lista}. {rada}");
    }

    private static (PdfXObject Obraz, float Proporcje)? WczytajLogo(string? sciezka, bool bezPrzezroczystosci)
    {
        if (string.IsNullOrWhiteSpace(sciezka))
            return null;
        if (!File.Exists(sciezka))
            throw new BladPodpisu($"Nie znaleziono pliku logo: {sciezka}. Popraw ścieżkę w ustawieniach lub usuń logo.");
        try
        {
            return Logo.Wczytaj(sciezka, bezPrzezroczystosci);
        }
        catch (Exception e) when (e is not BladPodpisu)
        {
            throw new BladPodpisu($"Nie można odczytać pliku logo ({Path.GetFileName(sciezka)}). Użyj pliku SVG, PDF, PNG lub JPG.", e);
        }
    }
}
