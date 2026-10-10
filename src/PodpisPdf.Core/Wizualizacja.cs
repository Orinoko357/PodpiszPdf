using System.Globalization;
using iText.Forms.Form.Element;
using iText.IO.Font;
using iText.IO.Image;
using iText.Kernel.Colors;
using iText.Kernel.Font;
using iText.Kernel.Geom;
using iText.Layout.Borders;
using iText.Layout.Element;
using iText.Layout.Properties;
using iText.Signatures;
using Path = System.IO.Path;

namespace PodpisPdf.Core;

/// <summary>Dane pokazywane w prostokącie podpisu.</summary>
public sealed record DaneWizualizacji(string ImieNazwisko, string? Stanowisko, string? SciezkaLogo, DateTimeOffset Czas);

/// <summary>Budowa widocznej wizualizacji podpisu: imię i nazwisko, stanowisko, logo, data i godzina.</summary>
internal static class Wizualizacja
{
    private const float PunktowNaMm = 72f / 25.4f;
    private const float Obramowanie = 0.75f;
    private const float Odstep = 3f;
    private const float MaksRozmiarCzcionki = 11f;
    private const float MinRozmiarCzcionki = 4f;
    private const float InterliniaWzgledna = 1.2f;
    private const float SkalaNazwiska = 1.15f;
    private const float SkalaNaglowka = 0.85f;
    private const int MaksWierszyStanowiska = 2;

    internal const string Naglowek = "Podpisano elektronicznie przez:";
    // Zapas na zaokrąglenia układu, żeby wiersz nie zawinął się „na styk”.
    private const float ZapasSzerokosci = 0.96f;

    /// <summary>Prostokąt wizualizacji w układzie współrzędnych strony.</summary>
    public static Rectangle ObliczProstokat(Rectangle strona, PolozenieWizualizacji p)
    {
        var w = p.SzerokoscMm * PunktowNaMm;
        var h = p.WysokoscMm * PunktowNaMm;
        var mx = p.MarginesPoziomyMm * PunktowNaMm;
        var my = p.MarginesPionowyMm * PunktowNaMm;

        if (w <= 0 || h <= 0)
            throw new BladPodpisu("Rozmiar wizualizacji podpisu musi być większy od zera. Popraw go w ustawieniach.");

        var lewy = p.Naroznik is Naroznik.LewyDolny or Naroznik.LewyGorny;
        var dolny = p.Naroznik is Naroznik.LewyDolny or Naroznik.PrawyDolny;

        var x = lewy ? strona.GetLeft() + mx : strona.GetRight() - mx - w;
        var y = dolny ? strona.GetBottom() + my : strona.GetTop() - my - h;

        if (x < strona.GetLeft() - 0.5f || y < strona.GetBottom() - 0.5f ||
            x + w > strona.GetRight() + 0.5f || y + h > strona.GetTop() + 0.5f)
        {
            throw new BladPodpisu(
                "Wizualizacja podpisu nie mieści się na stronie. Zmniejsz jej rozmiar lub marginesy w ustawieniach.");
        }

        return new Rectangle(x, y, w, h);
    }

    public static SignatureFieldAppearance Utworz(DaneWizualizacji dane, float szerokosc, float wysokosc)
    {
        var zwykla = UtworzCzcionke("PodpisPdf.Core.DejaVuSans.ttf");
        var pogrubiona = UtworzCzcionke("PodpisPdf.Core.DejaVuSans-Bold.ttf");

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

        var wnetrzeW = szerokosc - 2 * (Obramowanie + Odstep);
        var wnetrzeH = wysokosc - 2 * (Obramowanie + Odstep);

        float logoW = 0;
        if (logo != null)
        {
            // Logo w lewej kolumnie: pełna wysokość, nie więcej niż 35% szerokości.
            logoW = Math.Min(wnetrzeH * logo.GetWidth() / logo.GetHeight(), wnetrzeW * 0.35f);
        }
        var tekstW = wnetrzeW - (logo != null ? logoW + Odstep : 0);

        var rozmiar = DobierzRozmiarCzcionki(linie, tekstW, wnetrzeH);

        var tekst = new Div()
            .SetPadding(0)
            .SetMargin(0);
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

        var zawartosc = new Div()
            .SetMargin(0)
            .SetPadding(Odstep)
            .SetHeight(wysokosc - 2 * Obramowanie)
            .SetVerticalAlignment(VerticalAlignment.MIDDLE);

        if (logo == null)
        {
            zawartosc.Add(tekst);
        }
        else
        {
            var tabela = new Table(UnitValue.CreatePointArray(new[] { logoW + Odstep, tekstW }))
                .SetFixedLayout()
                .SetWidth(wnetrzeW)
                .SetBorder(Border.NO_BORDER)
                .SetMargin(0);
            var obraz = new Image(logo).SetAutoScale(true);
            tabela.AddCell(new Cell()
                .Add(obraz)
                .SetBorder(Border.NO_BORDER)
                .SetPadding(0)
                .SetPaddingRight(Odstep)
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

        var wyglad = new SignatureFieldAppearance(SignerProperties.IGNORED_ID);
        wyglad.SetContent(zawartosc);
        foreach (var bok in new[] { Property.PADDING_LEFT, Property.PADDING_RIGHT, Property.PADDING_TOP, Property.PADDING_BOTTOM })
            wyglad.SetProperty(bok, UnitValue.CreatePointValue(0));
        wyglad.SetBorder(new SolidBorder(new DeviceRgb(0x60, 0x60, 0x60), Obramowanie));
        wyglad.SetBackgroundColor(ColorConstants.WHITE);
        wyglad.SetFont(zwykla);
        return wyglad;
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
    private static float DobierzRozmiarCzcionki(List<Wiersz> linie, float szerokosc, float wysokosc)
    {
        var dostepnaSzerokosc = szerokosc * ZapasSzerokosci;
        for (var rozmiar = MaksRozmiarCzcionki; rozmiar >= MinRozmiarCzcionki; rozmiar -= 0.1f)
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
                lacznie += wiersze * r * InterliniaWzgledna;
            }
            if (miesci && lacznie <= wysokosc)
                return rozmiar;
        }
        // Przy bardzo długim tekście lepiej zawinąć wiersz niż użyć nieczytelnej czcionki.
        return MinRozmiarCzcionki;
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

    private static ImageData? WczytajLogo(string? sciezka)
    {
        if (string.IsNullOrWhiteSpace(sciezka))
            return null;
        if (!File.Exists(sciezka))
            throw new BladPodpisu($"Nie znaleziono pliku logo: {sciezka}. Popraw ścieżkę w ustawieniach lub usuń logo.");
        try
        {
            var obraz = ImageDataFactory.Create(File.ReadAllBytes(sciezka));
            if (obraz.GetWidth() <= 0 || obraz.GetHeight() <= 0)
                throw new InvalidDataException("obraz o zerowym rozmiarze");
            return obraz;
        }
        catch (Exception e) when (e is not BladPodpisu)
        {
            throw new BladPodpisu($"Nie można odczytać pliku logo ({Path.GetFileName(sciezka)}). Użyj pliku PNG lub JPG.", e);
        }
    }
}
