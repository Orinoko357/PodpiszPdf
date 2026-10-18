using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace PodpiszPdf.Mac;

public sealed partial class App : Application
{
    internal static OknoGlowne? Okno { get; private set; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime pulpit)
        {
            var (ustawienia, blad) = Ustawienia.Wczytaj();
            Okno = new OknoGlowne(ustawienia);
            pulpit.MainWindow = Okno;
            pulpit.ShutdownMode = ShutdownMode.OnMainWindowClose;

            // Plik PDF otwarty z Findera („Otwórz za pomocą”, przeciągnięcie na ikonę w Docku) przychodzi
            // jako zdarzenie systemu, także po starcie programu.
            if (TryGetFeature(typeof(IActivatableLifetime)) is IActivatableLifetime aktywacja)
            {
                aktywacja.Activated += (_, e) =>
                {
                    if (e is FileActivatedEventArgs pliki)
                    {
                        var pdf = pliki.Files.Select(f => f.TryGetLocalPath()).FirstOrDefault(p => p != null);
                        if (pdf != null)
                            Dispatcher.UIThread.Post(() => Okno?.OtworzPlik(pdf));
                    }
                };
            }

            if (Program.PlikZArgumentow is { } plik)
                Dispatcher.UIThread.Post(() => Okno.OtworzPlik(plik));
            if (blad != null)
                Okno.Opened += async (_, _) => await Dialogi.BladAsync(Okno, blad);
            if (Program.TrybSprawdzenia)
                Okno.Opened += (_, _) => DispatcherTimer.RunOnce(() => pulpit.Shutdown(0), TimeSpan.FromSeconds(3));
        }
        base.OnFrameworkInitializationCompleted();
    }

    private void PrzyUstawieniach(object? sender, EventArgs e) => Okno?.OtworzUstawienia();

    private void PrzyOProgramie(object? sender, EventArgs e) => Okno?.OtworzUstawienia(sekcja: 3);
}
