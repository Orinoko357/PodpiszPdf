using System.IO.Pipes;
using System.Security.Principal;

namespace PodpiszPdf;

/// <summary>
/// Pilnuje, żeby działała jedna kopia programu na użytkownika. Kolejne uruchomienia (np. z menu
/// kontekstowego, gdy program jest w zasobniku) rozmawiają z działającą kopią przez nazwany potok:
/// pytają o jej wersję, przekazują plik albo proszą o zamknięcie (aktualizacja).
/// </summary>
internal sealed class JednaInstancja : IDisposable
{
    public const string Pokaz = "POKAZ";
    public const string Wersja = "WERSJA";
    public const string Zakoncz = "ZAKONCZ";
    public const string PrefiksPliku = "PLIK\t";
    public const string Ok = "OK";
    public const string Zajety = "ZAJETY";

    private static readonly string Uzytkownik = WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
    // Nazwa blokady wspólna dla wszystkich wersji programu.
    private static readonly string NazwaBlokady = @"Local\PodpiszPdf-" + Uzytkownik;
    // Potok w wersji 2 (żądanie i odpowiedź). Wersja 1.0.0.1 miała potok bez odpowiedzi pod inną nazwą.
    private static readonly string NazwaPotoku = "PodpiszPdf2-" + Uzytkownik;

    private readonly Mutex _mutex;
    private readonly CancellationTokenSource _koniec = new();

    private JednaInstancja(Mutex mutex) => _mutex = mutex;

    /// <summary>
    /// Zwraca obiekt dla jedynej działającej kopii albo null, gdy działa inna kopia
    /// (po odczekaniu <paramref name="oczekiwanie"/> na jej zamknięcie). Rzuca UnauthorizedAccessException,
    /// gdy blokadę utworzyła kopia uruchomiona jako administrator (zwykły proces nie ma do niej dostępu).
    /// </summary>
    public static JednaInstancja? Przejmij(TimeSpan oczekiwanie = default)
    {
        var mutex = new Mutex(initiallyOwned: false, NazwaBlokady);
        try
        {
            if (mutex.WaitOne(oczekiwanie))
                return new JednaInstancja(mutex);
        }
        catch (AbandonedMutexException)
        {
            // Poprzednia kopia zakończyła się bez zwolnienia blokady — blokada jest teraz nasza.
            return new JednaInstancja(mutex);
        }
        mutex.Dispose();
        return null;
    }

    /// <summary>
    /// Wysyła polecenie do działającej kopii i zwraca jej odpowiedź; null, gdy nie odpowiada
    /// (np. działa wersja 1.0.0.1, która nie zna tego sposobu komunikacji).
    /// </summary>
    public static string? Wyslij(string polecenie, int limitMs = 3000)
    {
        try
        {
            using var klient = new NamedPipeClientStream(".", NazwaPotoku, PipeDirection.InOut, PipeOptions.CurrentUserOnly);
            klient.Connect(limitMs);
            using var pisarz = new StreamWriter(klient, leaveOpen: true) { AutoFlush = true };
            using var czytnik = new StreamReader(klient, leaveOpen: true);
            pisarz.WriteLine(polecenie);
            var odpowiedz = czytnik.ReadLineAsync().WaitAsync(TimeSpan.FromMilliseconds(limitMs)).GetAwaiter().GetResult();
            return odpowiedz;
        }
        catch (Exception e) when (e is TimeoutException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Jak <see cref="Wyslij"/>, ale ponawia próbę do skutku przez <paramref name="limit"/>. Działająca kopia
    /// może jeszcze budować okno (np. kilka plików PDF otwartych naraz) — potok pojawia się dopiero potem.
    /// </summary>
    public static string? WyslijZPonowieniem(string polecenie, TimeSpan limit)
    {
        var koniec = Environment.TickCount64 + (long)limit.TotalMilliseconds;
        while (true)
        {
            var odpowiedz = Wyslij(polecenie);
            if (odpowiedz != null || Environment.TickCount64 >= koniec)
                return odpowiedz;
            Thread.Sleep(500);
        }
    }

    /// <summary>
    /// Obsługuje polecenia od kolejnych uruchomień. <paramref name="obsluz"/> jest wywoływane
    /// w wątku tła i zwraca odpowiedź.
    /// </summary>
    public void Nasluchuj(Func<string, string> obsluz)
    {
        Task.Run(async () =>
        {
            while (!_koniec.IsCancellationRequested)
            {
                try
                {
                    await using var serwer = new NamedPipeServerStream(NazwaPotoku, PipeDirection.InOut, 1,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await serwer.WaitForConnectionAsync(_koniec.Token);
                    using var czytnik = new StreamReader(serwer, leaveOpen: true);
                    await using var pisarz = new StreamWriter(serwer, leaveOpen: true) { AutoFlush = true };
                    var polecenie = await czytnik.ReadLineAsync(_koniec.Token);
                    await pisarz.WriteLineAsync(obsluz(polecenie?.Trim() ?? Pokaz));
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
        try
        {
            _mutex.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // Blokada nie należała już do tego wątku.
        }
        _mutex.Dispose();
    }
}
