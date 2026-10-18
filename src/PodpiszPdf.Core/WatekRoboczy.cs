namespace PodpiszPdf.Core;

/// <summary>
/// Praca w tle na wątku z dużym stosem. Wątki w macOS mają domyślnie tylko 512 KB stosu (Linux 8 MB, Windows 1,5 MB),
/// a odczyt nietypowych lub uszkodzonych plików PDF (iText, PDFium) bywa głęboko rekurencyjny — przepełnienie stosu
/// kończy cały program bez komunikatu.
/// </summary>
public static class WatekRoboczy
{
    public const int RozmiarStosu = 16 * 1024 * 1024;

    public static Task<T> Uruchom<T>(Func<T> praca)
    {
        var wynik = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var watek = new Thread(() =>
        {
            try
            {
                wynik.SetResult(praca());
            }
            catch (Exception e)
            {
                wynik.SetException(e);
            }
        }, RozmiarStosu)
        {
            IsBackground = true,
            Name = "PodpiszPdf — praca w tle",
        };
        watek.Start();
        return wynik.Task;
    }

    public static Task Uruchom(Action praca) => Uruchom(() =>
    {
        praca();
        return true;
    });
}
