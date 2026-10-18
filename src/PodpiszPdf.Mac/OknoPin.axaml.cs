using Avalonia.Controls;
using Avalonia.Interactivity;

namespace PodpiszPdf.Mac;

/// <summary>Okno PIN-u karty (gdy czytnik nie ma własnej klawiatury).</summary>
public sealed partial class OknoPin : Window
{
    private string? _wynik;

    public OknoPin()
    {
        InitializeComponent();
        Opened += (_, _) => Pin.Focus();
    }

    /// <summary>Zwraca PIN albo null po anulowaniu.</summary>
    public static async Task<string?> ZapytajAsync(Window wlasciciel, string karta, string osoba, string? ostrzezenie)
    {
        var okno = new OknoPin();
        okno.Opis.Text = $"Podpis certyfikatem {osoba} z karty „{karta}”.";
        okno.Ostrzezenie.IsVisible = ostrzezenie != null;
        okno.TekstOstrzezenia.Text = ostrzezenie;
        await okno.ShowDialog(wlasciciel);
        return okno._wynik;
    }

    internal void PokazPrzyklad(string karta, string osoba, string? ostrzezenie)
    {
        Opis.Text = $"Podpis certyfikatem {osoba} z karty „{karta}”.";
        Ostrzezenie.IsVisible = ostrzezenie != null;
        TekstOstrzezenia.Text = ostrzezenie;
        Pin.Text = "123456";
    }

    private void PrzyPodpisie(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(Pin.Text))
            return;
        _wynik = Pin.Text;
        Pin.Text = "";
        Close();
    }

    private void PrzyAnulowaniu(object? sender, RoutedEventArgs e) => Close();
}
