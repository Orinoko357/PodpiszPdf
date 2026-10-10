using System.IO.Pipes;
using System.Security.Principal;

namespace PodpisPdf;

/// <summary>
/// Pilnuje, żeby działała jedna kopia programu na użytkownika. Kolejne uruchomienia (np. z menu
/// kontekstowego, gdy program jest w zasobniku) przekazują ścieżkę pliku do działającej kopii.
/// </summary>
internal sealed class JednaInstancja : IDisposable
{
    private static readonly string Nazwa =
        "PodpisPdf-" + (WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName);

    private readonly Mutex _mutex;
    private readonly CancellationTokenSource _koniec = new();

    private JednaInstancja(Mutex mutex) => _mutex = mutex;

    /// <summary>Zwraca obiekt dla pierwszej kopii albo null, gdy program już działa.</summary>
    public static JednaInstancja? Przejmij()
    {
        var mutex = new Mutex(initiallyOwned: true, @"Local\" + Nazwa, out var utworzony);
        if (utworzony)
            return new JednaInstancja(mutex);
        mutex.Dispose();
        return null;
    }

    /// <summary>Przekazuje ścieżkę (lub pusty tekst = „pokaż okno”) do działającej kopii.</summary>
    public static bool Przekaz(string? plik)
    {
        try
        {
            using var klient = new NamedPipeClientStream(".", Nazwa, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            klient.Connect(5000);
            using var pisarz = new StreamWriter(klient);
            pisarz.WriteLine(plik ?? "");
            return true;
        }
        catch (Exception e) when (e is TimeoutException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Nasłuchuje kolejnych uruchomień; <paramref name="obsluz"/> jest wywoływane w wątku tła.</summary>
    public void Nasluchuj(Action<string> obsluz)
    {
        Task.Run(async () =>
        {
            while (!_koniec.IsCancellationRequested)
            {
                try
                {
                    await using var serwer = new NamedPipeServerStream(Nazwa, PipeDirection.In, 1,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await serwer.WaitForConnectionAsync(_koniec.Token);
                    using var czytnik = new StreamReader(serwer);
                    var linia = await czytnik.ReadLineAsync(_koniec.Token);
                    obsluz(linia?.Trim() ?? "");
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (IOException)
                {
                    // Przerwane połączenie — czekamy na następne.
                }
            }
        });
    }

    public void Dispose()
    {
        _koniec.Cancel();
        _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}
