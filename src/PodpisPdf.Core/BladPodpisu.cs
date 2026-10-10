namespace PodpisPdf.Core;

/// <summary>Błąd z komunikatem przeznaczonym dla użytkownika.</summary>
public class BladPodpisu : Exception
{
    public BladPodpisu(string komunikat, Exception? przyczyna = null) : base(komunikat, przyczyna) { }
}

/// <summary>Serwer znacznika czasu nie odpowiedział albo zwrócił błąd.</summary>
public sealed class BladZnacznikaCzasu : BladPodpisu
{
    public BladZnacznikaCzasu(string komunikat, Exception? przyczyna = null) : base(komunikat, przyczyna) { }
}
