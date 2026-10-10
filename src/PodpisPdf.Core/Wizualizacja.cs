using System.Globalization;
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

namespace PodpisPdf.Core;

/// <summary>Dane pokazywane w prostokącie podpisu.</summary>
public sealed record DaneWizualizacji(string ImieNazwisko, string? Stanowisko, string? SciezkaLogo, DateTimeOffset Czas);

/// <summary>Budowa widocznej wizualizacji podpisu: imię i nazwisko, stanowisko, logo, data i godzina.</summary>
public static class Wizualizacja
{
    /// <summary>Obsługiwane formaty logo: wektorowe (SVG, PDF) i rastrowe (PNG, JPG).</summary>
    public static IReadOnlyList<string> RozszerzeniaLogo => Logo.Rozszerzenia;

    /// <summary>Stały rozmiar wizualizacji.</summary>
    public const float SzerokoscMm = 85;
    public const float WysokoscMm = 25;

    public const float PunktowNaMm = 72f / 25.4f;
    public static float SzerokoscPt => SzerokoscMm * PunktowNaMm;
    public static float WysokoscPt => WysokoscMm * PunktowNaMm;

    /// <summary>Proporcje wizualizacji (szerokość / wysokość) — stałe przy każdej skali.</summary>
    public static float Proporcje => SzerokoscMm / WysokoscMm;

    /// <summary>Zakres skali przy rysowaniu podpisu na podglądzie (1 = 85 × 25 mm).</summary>
    public const float MinSkala = 0.6f;
    public const float MaksSkala = 3f;

    private const float Odstep = 3f;
    private const float OdstepLogo = 7f;
    private const float MaksUdzialLogo = 0.35f;
    // Położenie glifów w wierszu (ułamki rozmiaru czcionki), zmierzone dla DejaVu Sans przy interlinii 1,2:
    // od góry wiersza do górnej krawędzi wielkich liter i od dołu wiersza do dolnej krawędzi znaków.
    private const float GornyLuzWiersza = 0.27f;
    private const float DolnyLuzWiersza = 0.29f;
    private const float MaksRozmiarCzcionki = 11f;
    private const float MinRozmiarCzcionki = 4f;
    private const float InterliniaWzgledna = 1.2f;
    private const float SkalaNazwiska = 1.15f;
    private const float SkalaNaglowka = 0.85f;
    private const int MaksWierszyStanowiska = 2;

    internal const string Naglowek = "Podpisano elektronicznie przez:";
    // Zapas na zaokrąglenia układu, żeby wiersz nie zawinął się „na styk”.
    private const float ZapasSzerokosci = 0.96f;
    // Tolerancja przy sprawdzaniu, czy prostokąt mieści się na stronie.
    private const float Tolerancja = 0.5f;

    /// <summary>Prostokąt wizualizacji wg położenia domyślnego (narożnik i odległość od krawędzi).</summary>
    public static Rectangle ObliczProstokat(Rectangle strona, PolozenieWizualizacji p, float skala = 1f)
    {
        var w = SzerokoscPt * skala;
        var h = WysokoscPt * skala;
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
    public static Rectangle ProstokatWMiejscu(Rectangle strona, MiejscePodpisu miejsce)
    {
        if (miejsce.Skala < MinSkala - 0.001f || miejsce.Skala > MaksSkala + 0.001f)
            throw new BladPodpisu("Nieprawidłowy rozmiar podpisu. Wskaż miejsce ponownie.");
        var prostokat = new Rectangle(miejsce.X, miejsce.Y, SzerokoscPt * miejsce.Skala, WysokoscPt * miejsce.Skala);
        if (!MiesciSie(strona, prostokat))
            throw new BladPodpisu("Wskazane miejsce podpisu wychodzi poza stronę. Wskaż miejsce ponownie.");
        return prostokat;
    }

    /// <summary>
    /// Lewy dolny róg wizualizacji tak, aby jej środek był w punkcie (x, y), ale cała zmieściła się na stronie.
    /// </summary>
    public static (float X, float Y) DopasujDoStrony(Rectangle strona, float srodekX, float srodekY, float skala = 1f)
    {
        var w = SzerokoscPt * skala;
        var h = WysokoscPt * skala;
        var x = Math.Clamp(srodekX - w / 2, strona.GetLeft(), Math.Max(strona.GetLeft(), strona.GetRight() - w));
        var y = Math.Clamp(srodekY - h / 2, strona.GetBottom(), Math.Max(strona.GetBottom(), strona.GetTop() - h));
        return (x, y);
    }

    private static bool MiesciSie(Rectangle strona, Rectangle p) =>
        p.GetLeft() >= strona.GetLeft() - Tolerancja && p.GetBottom() >= strona.GetBottom() - Tolerancja &&
        p.GetRight() <= strona.GetRight() + Tolerancja && p.GetTop() <= strona.GetTop() + Tolerancja;

    /// <summary>Wygląd pola podpisu: bez ramki i z przezroczystym tłem.</summary>
    /// <param name="skala">Skala całej wizualizacji (rozmiar, czcionki, odstępy); proporcje pozostają stałe.</param>
    internal static SignatureFieldAppearance Utworz(DaneWizualizacji dane, float skala = 1f)
    {
        var wyglad = new SignatureFieldAppearance(SignerProperties.IGNORED_ID);
        wyglad.SetContent(UtworzZawartosc(dane, skala, out var czcionka));
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
        using var ms = new MemoryStream();
        using (var pdf = new iText.Kernel.Pdf.PdfDocument(new iText.Kernel.Pdf.PdfWriter(ms)))
        {
            using var dokument = new iText.Layout.Document(pdf, new PageSize(SzerokoscPt * skala, WysokoscPt * skala));
            dokument.SetMargins(0, 0, 0, 0);
            dokument.Add(UtworzZawartosc(dane, skala, out _));
        }
        return ms.ToArray();
    }

    /// <summary>
    /// Zawartość wizualizacji. Wszystkie wymiary (prostokąt, odstępy, granice rozmiaru czcionki) są mnożone
    /// przez skalę, więc wizualizacja w innej skali jest dokładnym powiększeniem lub pomniejszeniem.
    /// </summary>
    private static Div UtworzZawartosc(DaneWizualizacji dane, float skala, out PdfFont czcionkaPodstawowa)
    {
        var szerokosc = SzerokoscPt * skala;
        var wysokosc = WysokoscPt * skala;
        var odstep = Odstep * skala;
        var odstepLogo = OdstepLogo * skala;
        var zwykla = UtworzCzcionke("PodpisPdf.Core.DejaVuSans.ttf");
        var pogrubiona = UtworzCzcionke("PodpisPdf.Core.DejaVuSans-Bold.ttf");
        czcionkaPodstawowa = zwykla;

        var linie = new List<Wiersz>
        {
            new(Naglowek, zwykla, SkalaNaglowka),
            new(dane.ImieNazwisko, pogrubiona, SkalaNazwiska),
        };
        var stanowisko = WierszeStanowiska(dane.Stanowisko);
        if (stanowisko.Count == 1)
            linie.Add(new(stanowisko[0], zwykla, 1f, MaksWierszyStanowiska));
        else
            linie.AddRange(stanowisko.Select(w => new Wiersz(w, zwykla, 1f)));
        linie.Add(new("Data: " + FormatujCzas(dane.Czas), zwykla, 1f));

        var logo = WczytajLogo(dane.SciezkaLogo);

        var wnetrzeW = szerokosc - 2 * odstep;
        var wnetrzeH = wysokosc - 2 * odstep;

        // Logo ma dokładnie wysokość bloku tekstu (góra herbu = góra pierwszego wiersza, dół = dół ostatniego).
        // Wysokość tekstu zależy od rozmiaru czcionki, a ten od miejsca zajętego przez logo — stąd kilka przybliżeń.
        float logoW = 0, logoH = wnetrzeH;
        var tekstW = wnetrzeW;
        var rozmiar = DobierzRozmiarCzcionki(linie, tekstW, wnetrzeH, skala);
        for (var krok = 0; logo != null && krok < 6; krok++)
        {
            logoW = Math.Min(logoH * logo.Value.Proporcje, wnetrzeW * MaksUdzialLogo);
            tekstW = wnetrzeW - logoW - odstepLogo;
            rozmiar = DobierzRozmiarCzcionki(linie, tekstW, wnetrzeH, skala);
            logoH = Math.Min(wnetrzeH, WidocznaWysokoscTekstu(linie, rozmiar, tekstW));
        }
        if (logo != null)
        {
            logoW = Math.Min(logoH * logo.Value.Proporcje, wnetrzeW * MaksUdzialLogo);
            logoH = logoW / logo.Value.Proporcje;
        }

        var tekst = UtworzBlokTekstu(linie, rozmiar);

        var zawartosc = new Div()
            .SetMargin(0)
            .SetPadding(odstep)
            .SetWidth(szerokosc)
            .SetHeight(wysokosc)
            .SetVerticalAlignment(VerticalAlignment.MIDDLE);

        if (logo == null)
        {
            zawartosc.Add(tekst);
        }
        else
        {
            var tabela = new Table(UnitValue.CreatePointArray(new[] { logoW + odstepLogo, tekstW }))
                .SetFixedLayout()
                .SetWidth(wnetrzeW)
                .SetBorder(Border.NO_BORDER)
                .SetMargin(0);
            var obraz = (logo.Value.Obraz switch
            {
                iText.Kernel.Pdf.Xobject.PdfFormXObject f => new Image(f),
                var x => new Image((iText.Kernel.Pdf.Xobject.PdfImageXObject)x),
            }).SetWidth(logoW).SetHeight(logoH);
            tabela.AddCell(new Cell()
                .Add(obraz)
                .SetBorder(Border.NO_BORDER)
                .SetPadding(0)
                .SetPaddingRight(odstepLogo)
                .SetHeight(wnetrzeH)
                .SetVerticalAlignment(VerticalAlignment.MIDDLE));
            tabela.AddCell(new Cell()
                .Add(tekst)
                .SetBorder(Border.NO_BORDER)
                .SetPadding(0)
                .SetHeight(wnetrzeH)
                .SetVerticalAlignment(VerticalAlignment.MIDDLE));
            zawartosc.Add(tabela);
        }
        return zawartosc;
    }

    /// <summary>
    /// Wiersze stanowiska. Podział wpisany przez użytkownika (Enter w ustawieniach) jest zachowany dokładnie;
    /// bez niego program sam zawija stanowisko do dwóch wierszy.
    /// </summary>
    internal static List<string> WierszeStanowiska(string? stanowisko) =>
        (stanowisko ?? "")
            .Split('\n')
            .Select(w => w.Trim())
            .Where(w => w.Length > 0)
            .ToList();

    internal static string FormatujCzas(DateTimeOffset czas)
    {
        var lokalny = czas.ToLocalTime();
        var przesuniecie = lokalny.Offset;
        var znak = przesuniecie < TimeSpan.Zero ? "-" : "+";
        return lokalny.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) +
               $" (UTC{znak}{przesuniecie.Duration():hh\\:mm})";
    }

    /// <summary>
    /// Największy rozmiar czcionki, przy którym wszystko mieści się w prostokącie.
    /// Imię i nazwisko, nagłówek i data zajmują po jednym wierszu; długie stanowisko może się zawinąć,
    /// żeby nie zmniejszać całego tekstu.
    /// </summary>
    private static float DobierzRozmiarCzcionki(List<Wiersz> linie, float szerokosc, float wysokosc, float skala)
    {
        var dostepnaSzerokosc = szerokosc * ZapasSzerokosci;
        for (var rozmiar = MaksRozmiarCzcionki * skala; rozmiar >= MinRozmiarCzcionki * skala; rozmiar -= 0.1f * skala)
        {
            var lacznie = 0f;
            var miesci = true;
            foreach (var w in linie)
            {
                var r = rozmiar * w.Skala;
                var wiersze = LiczbaWierszy(w.Tekst, w.Czcionka, r, dostepnaSzerokosc);
                if (wiersze > w.MaksWierszy)
                {
                    miesci = false;
                    break;
                }
                lacznie += wiersze * WysokoscWiersza(w.Czcionka, r);
            }
            if (miesci && lacznie <= wysokosc)
                return rozmiar;
        }
        // Przy bardzo długim tekście lepiej zawinąć wiersz niż użyć nieczytelnej czcionki.
        return MinRozmiarCzcionki * skala;
    }

    private static Div UtworzBlokTekstu(List<Wiersz> linie, float rozmiar)
    {
        var tekst = new Div().SetPadding(0).SetMargin(0);
        foreach (var (t, czcionka, skala, _) in linie)
        {
            tekst.Add(new Paragraph(t)
                .SetFont(czcionka)
                .SetFontSize(rozmiar * skala)
                .SetFontColor(ColorConstants.BLACK)
                .SetMultipliedLeading(InterliniaWzgledna)
                .SetMargin(0)
                .SetPadding(0));
        }
        return tekst;
    }

    /// <summary>Wysokość od górnej krawędzi pierwszego wiersza tekstu do dolnej krawędzi ostatniego.</summary>
    private static float WidocznaWysokoscTekstu(List<Wiersz> linie, float rozmiar, float szerokosc)
    {
        var wysokosc = 0f;
        foreach (var w in linie)
            wysokosc += LiczbaWierszy(w.Tekst, w.Czcionka, rozmiar * w.Skala, szerokosc * ZapasSzerokosci) *
                        WysokoscWiersza(w.Czcionka, rozmiar * w.Skala);
        var pierwszy = rozmiar * linie[0].Skala;
        var ostatni = rozmiar * linie[^1].Skala;
        return wysokosc - GornyLuzWiersza * pierwszy - DolnyLuzWiersza * ostatni;
    }

    /// <summary>Wysokość wiersza tak, jak liczy ją iText: interlinia × (ascender − descender) czcionki.</summary>
    private static float WysokoscWiersza(PdfFont czcionka, float rozmiar)
    {
        var m = czcionka.GetFontProgram().GetFontMetrics();
        return InterliniaWzgledna * (m.GetAscender() - m.GetDescender()) / 1000f * rozmiar;
    }

    /// <summary>Liczba wierszy po zawinięciu tekstu na granicach słów (int.MaxValue, gdy słowo się nie mieści).</summary>
    internal static int LiczbaWierszy(string tekst, PdfFont czcionka, float rozmiar, float szerokosc)
    {
        var spacja = czcionka.GetWidth(" ", rozmiar);
        var wiersze = 1;
        var biezaca = 0f;
        foreach (var slowo in tekst.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var w = czcionka.GetWidth(slowo, rozmiar);
            if (w > szerokosc)
                return int.MaxValue;
            if (biezaca == 0)
                biezaca = w;
            else if (biezaca + spacja + w <= szerokosc)
                biezaca += spacja + w;
            else
            {
                wiersze++;
                biezaca = w;
            }
        }
        return wiersze;
    }

    private sealed record Wiersz(string Tekst, PdfFont Czcionka, float Skala, int MaksWierszy = 1);

    private static PdfFont UtworzCzcionke(string zasob)
    {
        using var strumien = typeof(Wizualizacja).Assembly.GetManifestResourceStream(zasob)
                             ?? throw new InvalidOperationException($"Brak zasobu czcionki {zasob}.");
        using var ms = new MemoryStream();
        strumien.CopyTo(ms);
        return PdfFontFactory.CreateFont(ms.ToArray(), PdfEncodings.IDENTITY_H,
            PdfFontFactory.EmbeddingStrategy.FORCE_EMBEDDED);
    }

    private static (iText.Kernel.Pdf.Xobject.PdfXObject Obraz, float Proporcje)? WczytajLogo(string? sciezka)
    {
        if (string.IsNullOrWhiteSpace(sciezka))
            return null;
        if (!File.Exists(sciezka))
            throw new BladPodpisu($"Nie znaleziono pliku logo: {sciezka}. Popraw ścieżkę w ustawieniach lub usuń logo.");
        try
        {
            return Logo.Wczytaj(sciezka);
        }
        catch (Exception e) when (e is not BladPodpisu)
        {
            throw new BladPodpisu($"Nie można odczytać pliku logo ({Path.GetFileName(sciezka)}). Użyj pliku SVG, PDF, PNG lub JPG.", e);
        }
    }
}
