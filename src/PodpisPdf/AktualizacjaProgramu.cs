using System.Diagnostics;
using PodpisPdf.Core;

namespace PodpisPdf;

/// <summary>
/// Aktualizacja programu z publicznego repozytorium wydań: pobranie, podmiana kopii w %APPDATA%\PodpisPdf
/// i uruchomienie nowej wersji. Działający plik .exe można przemianować (nie można go nadpisać),
/// więc stara wersja zostaje jako PodpisPdf.exe.stary i jest usuwana przy następnym starcie.
/// </summary>
internal static class AktualizacjaProgramu
{
    private static readonly HttpClient Klient = Aktualizacje.UtworzKlienta();

    private static string StaryPlik => Instalacja.SciezkaDocelowa + ".stary";

    public static Task<DostepnaAktualizacja?> SprawdzAsync() =>
        Aktualizacje.SprawdzAsync(Klient, Instalacja.BiezacaWersja);

    /// <summary>Pobiera nową wersję (z kontrolą SHA-256) i podmienia kopię programu w %APPDATA%.</summary>
    public static async Task PobierzIZainstalujAsync(DostepnaAktualizacja aktualizacja, IProgress<double>? postep = null)
    {
        Directory.CreateDirectory(Ustawienia.Folder);
        var pobrany = Instalacja.SciezkaDocelowa + ".pobrany";
        await Aktualizacje.PobierzAsync(Klient, aktualizacja, pobrany, postep);
        await Task.Run(() => Podmien(pobrany));
    }

    private static void Podmien(string pobrany)
    {
        var cel = Instalacja.SciezkaDocelowa;
        var przeniesiono = false;
        if (File.Exists(cel))
        {
            File.Move(cel, StaryPlik, overwrite: true);
            przeniesiono = true;
        }
        try
        {
            File.Move(pobrany, cel);
        }
        catch (Exception) when (przeniesiono)
        {
            if (!File.Exists(cel))
                File.Move(StaryPlik, cel); // przywrócenie poprzedniej wersji
            throw;
        }
    }

    /// <summary>Uruchamia zaktualizowaną kopię; poczeka ona, aż ta kopia się zamknie.</summary>
    public static void UruchomNowaWersje() =>
        Process.Start(new ProcessStartInfo(Instalacja.SciezkaDocelowa, Program.ParametrPoAktualizacji) { UseShellExecute = false });

    /// <summary>Usuwa pozostawioną poprzednią wersję (wywoływane w tle przy starcie).</summary>
    public static void UsunPoprzedniaWersje()
    {
        try
        {
            if (File.Exists(StaryPlik))
                File.Delete(StaryPlik);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Poprzednia wersja może jeszcze kończyć pracę — usuniemy ją następnym razem.
        }
    }
}
