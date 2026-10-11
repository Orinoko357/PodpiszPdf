// Narzędzie deweloperskie: podpis certyfikatem testowym z pliku PFX (bez karty),
// żeby sprawdzić format podpisu w Adobe Reader i EU DSS. Nie służy do podpisów produkcyjnych.
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using PodpiszPdf.Core;

const string Pomoc = """
    Użycie:
      PodpisTestowy utworz-pfx <plik.pfx> <hasło> "<Imię Nazwisko>"
      PodpisTestowy podpisz <plik.pdf> <plik.pfx> <hasło> [adres TSA] [stanowisko] [logo]

    Domyślny adres TSA: http://time.certum.pl
    """;

try
{
    switch (args.FirstOrDefault())
    {
        case "utworz-pfx" when args.Length == 4:
            UtworzPfx(args[1], args[2], args[3]);
            Console.WriteLine($"Utworzono certyfikat testowy: {args[1]}");
            return 0;

        case "podpisz" when args.Length >= 4:
            using (var cert = new X509Certificate2(args[2], args[3], X509KeyStorageFlags.EphemeralKeySet))
            {
                var tsa = KlientZnacznikaCzasu.DlaAdresu(args.ElementAtOrDefault(4) ?? "http://time.certum.pl");
                var wyjscie = PodpisywaczPdf.NazwaPlikuWynikowego(args[1], "_podpisany");
                var wynik = new PodpisywaczPdf(tsa).Podpisz(args[1], wyjscie, cert, new OpcjePodpisu
                {
                    Stanowisko = args.ElementAtOrDefault(5),
                    SciezkaLogo = args.ElementAtOrDefault(6),
                });
                Console.WriteLine($"Podpisano: {wynik.SciezkaWyniku}");
                Console.WriteLine($"Czas na wizualizacji: {wynik.CzasNaWizualizacji:O} ({(wynik.CzasZSerwera ? "serwer TSA" : "zegar komputera")})");
            }
            return 0;

        default:
            Console.WriteLine(Pomoc);
            return 1;
    }
}
catch (Exception e)
{
    Console.Error.WriteLine(KomunikatyBledow.Opisz(e));
    Console.Error.WriteLine();
    Console.Error.WriteLine(e);
    return 2;
}

static void UtworzPfx(string sciezka, string haslo, string imieNazwisko)
{
    using var rsa = RSA.Create(2048);
    var czesci = imieNazwisko.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
    var nazwa = new X500DistinguishedNameBuilder();
    nazwa.AddCountryOrRegion("PL");
    nazwa.Add("2.5.4.42", czesci[0]);
    if (czesci.Length > 1)
        nazwa.Add("2.5.4.4", czesci[1]);
    nazwa.AddCommonName(imieNazwisko);

    var zadanie = new CertificateRequest(nazwa.Build(), rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    zadanie.CertificateExtensions.Add(new X509KeyUsageExtension(
        X509KeyUsageFlags.NonRepudiation | X509KeyUsageFlags.DigitalSignature, critical: true));
    using var cert = zadanie.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(2));
    File.WriteAllBytes(sciezka, cert.Export(X509ContentType.Pfx, haslo));
}
