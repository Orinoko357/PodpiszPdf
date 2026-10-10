using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace PodpisPdf;

/// <summary>Okna dialogowe w stylu WinUI (ContentDialog) i wybór plików.</summary>
internal static class Dialogi
{
    // W oknie może być otwarte tylko jedno okno ContentDialog naraz.
    private static readonly SemaphoreSlim Kolejka = new(1, 1);

    /// <summary>Komunikat z przyciskami; zwraca wybrany przycisk.</summary>
    public static async Task<ContentDialogResult> PokazAsync(XamlRoot? korzen, string tytul, string tresc,
        string zamknij = "OK", string? glowny = null, bool domyslnieGlowny = true)
    {
        if (korzen == null)
        {
            // Okno nie jest jeszcze pokazane (np. start w zasobniku) — zwykły komunikat Windows.
            if (glowny == null)
            {
                Win32.Komunikat(tresc, Win32.Informacja);
                return ContentDialogResult.None;
            }
            return Win32.Komunikat(tresc, Win32.Informacja, Win32.TakNie) == Win32.Tak ? ContentDialogResult.Primary : ContentDialogResult.None;
        }

        await Kolejka.WaitAsync();
        try
        {
            var dialog = new ContentDialog
            {
                XamlRoot = korzen,
                Title = tytul,
                Content = new TextBlock { Text = tresc, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true },
                CloseButtonText = zamknij,
                DefaultButton = glowny != null && domyslnieGlowny ? ContentDialogButton.Primary : ContentDialogButton.Close,
                Style = (Style)Application.Current.Resources["DefaultContentDialogStyle"],
                RequestedTheme = (korzen.Content as FrameworkElement)?.ActualTheme ?? ElementTheme.Default,
            };
            if (glowny != null)
                dialog.PrimaryButtonText = glowny;
            return await dialog.ShowAsync();
        }
        finally
        {
            Kolejka.Release();
        }
    }

    public static Task BladAsync(XamlRoot? korzen, string tresc, string tytul = "Wystąpił problem") =>
        PokazAsync(korzen, tytul, tresc);

    /// <summary>Wybór jednego pliku; zwraca pełną ścieżkę albo null.</summary>
    public static async Task<string?> WybierzPlikAsync(IntPtr okno, params string[] rozszerzenia)
    {
        var wybor = new FileOpenPicker { ViewMode = PickerViewMode.List, SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        foreach (var r in rozszerzenia)
            wybor.FileTypeFilter.Add(r);
        WinRT.Interop.InitializeWithWindow.Initialize(wybor, okno);
        var plik = await wybor.PickSingleFileAsync();
        return plik?.Path;
    }
}
