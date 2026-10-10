namespace PodpisPdf;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var plik = args.FirstOrDefault();

        using var instancja = JednaInstancja.Przejmij();
        if (instancja == null)
        {
            // Program już działa (np. w zasobniku) — przekazujemy mu plik i kończymy.
            if (JednaInstancja.Przekaz(plik))
                return;
        }

        ApplicationConfiguration.Initialize();
        var okno = new OknoGlowne(plik);
        instancja?.Nasluchuj(okno.PrzyjmijZInnejKopii);
        Application.Run(okno);
    }
}
