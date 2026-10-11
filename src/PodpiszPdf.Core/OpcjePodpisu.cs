namespace PodpiszPdf.Core;

public enum WyborStrony
{
    Pierwsza,
    Ostatnia,
    Numer,
}

public enum Naroznik
{
    LewyDolny,
    PrawyDolny,
    LewyGorny,
    PrawyGorny,
}

/// <summary>Położenie i rozmiar wizualizacji podpisu na stronie.</summary>
public sealed class PolozenieWizualizacji
{
    public WyborStrony Strona { get; set; } = WyborStrony.Ostatnia;

    /// <summary>Numer strony (od 1), używany gdy <see cref="Strona"/> = <see cref="WyborStrony.Numer"/>.</summary>
    public int NumerStrony { get; set; } = 1;

    /// <summary>Naroznik strony, od którego liczone są marginesy.</summary>
    public Naroznik Naroznik { get; set; } = Naroznik.PrawyDolny;

    public float MarginesPoziomyMm { get; set; } = 15;
    public float MarginesPionowyMm { get; set; } = 15;
}

/// <summary>
/// Miejsce wskazane na podglądzie: numer strony (od 1), lewy dolny róg wizualizacji w punktach
/// (układ współrzędnych strony PDF) i skala (1 = rozmiar podstawowy, imię i nazwisko 11 pt; proporcje zawsze stałe).
/// </summary>
public sealed record MiejscePodpisu(int NumerStrony, float X, float Y, float Skala = 1f);

/// <summary>Parametry jednego podpisu.</summary>
public sealed class OpcjePodpisu
{
    /// <summary>Stanowisko pokazywane na wizualizacji (opcjonalne).</summary>
    public string? Stanowisko { get; set; }

    /// <summary>Ścieżka do logo SVG/PDF (wektorowe) albo PNG/JPG (opcjonalna).</summary>
    public string? SciezkaLogo { get; set; }

    /// <summary>Położenie domyślne (z ustawień), używane gdy nie wskazano <see cref="Miejsce"/>.</summary>
    public PolozenieWizualizacji Polozenie { get; set; } = new();

    /// <summary>Miejsce wskazane na podglądzie; ma pierwszeństwo przed <see cref="Polozenie"/>.</summary>
    public MiejscePodpisu? Miejsce { get; set; }

    /// <summary>Dołączenie danych OCSP/CRL do pliku (PAdES B-LT) zamiast samego B-T.</summary>
    public bool PoziomLT { get; set; }

    /// <summary>
    /// Wywoływane z kluczem prywatnym tuż przed podpisem, np. aby wskazać okno nadrzędne
    /// dla okna PIN-u sterownika karty.
    /// </summary>
    public Action<System.Security.Cryptography.AsymmetricAlgorithm>? PrzygotujKlucz { get; set; }
}
