using Avalonia;
using Avalonia.Controls;

namespace PodpiszPdf.Mac;

/// <summary>Wiersz na stronie ustawień: nagłówek, opis i kontrolka (wygląd w App.axaml).</summary>
public sealed class KartaUstawien : ContentControl
{
    public static readonly StyledProperty<string?> NaglowekProperty = AvaloniaProperty.Register<KartaUstawien, string?>(nameof(Naglowek));
    public static readonly StyledProperty<string?> OpisProperty = AvaloniaProperty.Register<KartaUstawien, string?>(nameof(Opis));

    public string? Naglowek
    {
        get => GetValue(NaglowekProperty);
        set => SetValue(NaglowekProperty, value);
    }

    public string? Opis
    {
        get => GetValue(OpisProperty);
        set => SetValue(OpisProperty, value);
    }
}
