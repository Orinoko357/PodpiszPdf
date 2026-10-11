using System.Security.Cryptography;
using iText.Kernel.Exceptions;

namespace PodpiszPdf.Core;

/// <summary>Zamiana wyjątków na zrozumiałe komunikaty po polsku.</summary>
public static class KomunikatyBledow
{
    // Kody błędów kart inteligentnych i CNG (winerror.h / scarderr.h).
    private const int ScardWWrongChv = unchecked((int)0x8010006B);
    private const int ScardWChvBlocked = unchecked((int)0x8010006C);
    private const int ScardWCancelledByUser = unchecked((int)0x8010006E);
    private const int ScardECancelled = unchecked((int)0x80100002);
    private const int ScardENoSmartcard = unchecked((int)0x8010000C);
    private const int ScardENoReadersAvailable = unchecked((int)0x8010002E);
    private const int ScardWRemovedCard = unchecked((int)0x80100069);
    private const int ScardENoKeyContainer = unchecked((int)0x80100030);
    private const int ErrorCancelled = unchecked((int)0x800704C7);
    private const int NteBadKeyset = unchecked((int)0x80090016);
    private const int NteKeysetNotDef = unchecked((int)0x80090019);
    private const int NteUserCancelled = unchecked((int)0x80090036);
    private const int NteSilentContext = unchecked((int)0x80090022);

    internal const string ZablokowanyDoZmian =
        "Autor pliku ograniczył uprawnienia do zmian w dokumencie (hasło właściciela), więc nie można go podpisać " +
        "bez naruszenia tych ograniczeń. Poproś autora o wersję bez zabezpieczeń.";

    public static string Opisz(Exception wyjatek)
    {
        foreach (var w in Lancuch(wyjatek))
        {
            if (w is BladPodpisu bp)
                return bp.Message;

            switch (w.HResult)
            {
                case ScardWWrongChv:
                    return "Nieprawidłowy PIN. Uwaga: kilka błędnych prób z rzędu zablokuje kartę.";
                case ScardWChvBlocked:
                    return "PIN karty jest zablokowany. Odblokuj kartę kodem PUK w programie producenta karty.";
                case ScardWCancelledByUser:
                case ScardECancelled:
                case ErrorCancelled:
                case NteUserCancelled:
                    return "Podpisywanie zostało anulowane.";
                case ScardENoSmartcard:
                case ScardWRemovedCard:
                    return "Brak karty w czytniku. Włóż kartę kryptograficzną i spróbuj ponownie.";
                case ScardENoReadersAvailable:
                    return "Nie znaleziono czytnika kart. Podłącz czytnik i spróbuj ponownie.";
                case ScardENoKeyContainer:
                case NteBadKeyset:
                case NteKeysetNotDef:
                    return "Nie znaleziono klucza prywatnego dla wybranego certyfikatu. " +
                           "Sprawdź, czy karta jest włożona (lub aplikacja SimplySign zalogowana) i czy jest to właściwa karta.";
                case NteSilentContext:
                    return "Sterownik karty wymaga wpisania PIN-u, ale nie mógł wyświetlić okna. Spróbuj ponownie.";
            }

            if (w is iText.Kernel.Exceptions.BadPasswordException bpe)
            {
                // Bez hasła otwarcia plik da się odczytać, ale zmiany (także podpis) wymagają hasła właściciela.
                return bpe.Message.Contains("owner password", StringComparison.OrdinalIgnoreCase)
                    ? ZablokowanyDoZmian
                    : "Plik PDF jest zabezpieczony hasłem. Usuń zabezpieczenie i spróbuj ponownie.";
            }
            if (w is iText.Pdfa.Exceptions.PdfAConformanceException pdfa)
                return "Dokument jest w formacie PDF/A, a wizualizacja podpisu nie spełnia jego wymagań: " + pdfa.Message;
            if (w is UnauthorizedAccessException)
                return "Brak uprawnień do zapisu w folderze z dokumentem. Skopiuj plik do innego folderu (np. Dokumenty).";
            if (w is FileNotFoundException fnf)
                return $"Nie znaleziono pliku: {fnf.FileName ?? fnf.Message}";
            if (w is IOException io && JestBlokadaPliku(io))
                return "Plik jest otwarty w innym programie. Zamknij go i spróbuj ponownie.";
        }

        foreach (var w in Lancuch(wyjatek))
        {
            if (w is CryptographicException ce)
                return "Błąd operacji kryptograficznej (karta lub sterownik): " + ce.Message;
            if (w is PdfException pe)
                return "Nie udało się przetworzyć pliku PDF: " + pe.Message;
        }

        return "Nieoczekiwany błąd: " + wyjatek.Message;
    }

    private static bool JestBlokadaPliku(IOException io)
    {
        // ERROR_SHARING_VIOLATION = 32, ERROR_LOCK_VIOLATION = 33
        var kod = io.HResult & 0xFFFF;
        return kod == 32 || kod == 33;
    }

    private static IEnumerable<Exception> Lancuch(Exception w)
    {
        for (Exception? e = w; e != null; e = e.InnerException)
        {
            yield return e;
            if (e is AggregateException ae)
                foreach (var inner in ae.InnerExceptions.SelectMany(Lancuch))
                    yield return inner;
        }
    }
}
