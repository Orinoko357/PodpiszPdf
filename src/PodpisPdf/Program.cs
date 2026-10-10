namespace PodpisPdf;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new OknoGlowne(args.FirstOrDefault()));
    }
}
