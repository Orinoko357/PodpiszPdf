using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace PodpiszPdf.Mac;

/// <summary>Proste okna komunikatów i pytań (arkusze nad oknem programu).</summary>
internal static class Dialogi
{
    /// <summary>
    /// Pokazuje komunikat z przyciskami (ostatni jest główny, Enter); zwraca indeks wybranego przycisku,
    /// -1 po zamknięciu okna lub Esc.
    /// </summary>
    public static async Task<int> PokazAsync(Window wlasciciel, string tytul, string tresc, params string[] przyciski)
    {
        if (przyciski.Length == 0)
            przyciski = new[] { "OK" };
        var okno = new Window
        {
            Title = tytul,
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            MaxWidth = 520,
        };
        var wynik = -1;
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        for (var i = 0; i < przyciski.Length; i++)
        {
            var indeks = i;
            var przycisk = new Button { Content = przyciski[i], MinWidth = 96, HorizontalContentAlignment = HorizontalAlignment.Center };
            if (i == przyciski.Length - 1)
            {
                przycisk.Classes.Add("accent");
                przycisk.IsDefault = true;
            }
            if (i == 0 && przyciski.Length > 1)
                przycisk.IsCancel = true;
            przycisk.Click += (_, _) => { wynik = indeks; okno.Close(); };
            panel.Children.Add(przycisk);
        }
        okno.Content = new StackPanel
        {
            Margin = new Thickness(24, 20, 24, 18),
            Spacing = 10,
            Children =
            {
                new TextBlock { Text = tytul, FontSize = 16, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap },
                new SelectableTextBlock { Text = tresc, TextWrapping = TextWrapping.Wrap, MaxWidth = 470 },
                new Border { Height = 6 },
                panel,
            },
        };
        okno.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
                okno.Close();
        };
        await okno.ShowDialog(wlasciciel);
        return wynik;
    }

    public static Task BladAsync(Window wlasciciel, string tresc, string tytul = "PodpiszPdf") =>
        PokazAsync(wlasciciel, tytul, tresc, "OK");
}
