using System.Text.Json;
using System.Text.Json.Serialization;
using PodpiszPdf.Core;

namespace PodpiszPdf.Mac;

/// <summary>Ustawienia programu w ~/Library/Application Support/PodpiszPdf/config.json (hasło TSA — w pęku kluczy).</summary>
internal sealed class Ustawienia
{
    public const string DomyslnyAdresTsa = "http://time.certum.pl";
    public const string DomyslnyPrzyrostek = "_podpisany";

    private static readonly JsonSerializerOptions OpcjeJson = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public string? Stanowisko { get; set; }
    public string? SciezkaLogo { get; set; }
    public string AdresTsa { get; set; } = DomyslnyAdresTsa;
    public string? LoginTsa { get; set; }
    public PolozenieWizualizacji Polozenie { get; set; } = new();
    public string Przyrostek { get; set; } = DomyslnyPrzyrostek;

    /// <summary>Zapis pod nową nazwą (z przyrostkiem); domyślnie podpisany plik zastępuje oryginał.</summary>
    public bool DodawajPrzyrostek { get; set; }

    public MotywWizualizacji MotywWizualizacji { get; set; } = MotywWizualizacji.Klasyczny;
    public bool PoziomLT { get; set; }

    /// <summary>Biblioteka PKCS#11 sterownika karty wskazana ręcznie; null — wyszukiwanie w znanych miejscach.</summary>
    public string? BibliotekaPkcs11 { get; set; }

    /// <summary>Odcisk (thumbprint) ostatnio użytego certyfikatu.</summary>
    public string? OstatniCertyfikat { get; set; }

    public bool SprawdzajAktualizacje { get; set; } = true;
    public DateTimeOffset? OstatnieSprawdzenieAktualizacji { get; set; }

    public static string Folder => OperatingSystem.IsMacOS()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "PodpiszPdf")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PodpiszPdf");

    public static string SciezkaPliku => Path.Combine(Folder, "config.json");

    public string PlikWynikowy(string wejscie) =>
        DodawajPrzyrostek ? PodpisywaczPdf.NazwaPlikuWynikowego(wejscie, Przyrostek) : wejscie;

    /// <summary>Hasło do serwera znacznika czasu (pęk kluczy macOS, konto = login).</summary>
    [JsonIgnore]
    public string? HasloTsa
    {
        get => string.IsNullOrEmpty(LoginTsa) ? null : PekKluczy.Odczytaj(LoginTsa);
        set
        {
            if (!string.IsNullOrEmpty(LoginTsa))
                PekKluczy.Zapisz(LoginTsa, value);
        }
    }

    /// <summary>Wczytuje ustawienia; zwraca też opis błędu odczytu (wtedy ustawienia domyślne).</summary>
    public static (Ustawienia Ustawienia, string? Blad) Wczytaj()
    {
        try
        {
            if (File.Exists(SciezkaPliku))
            {
                var u = JsonSerializer.Deserialize<Ustawienia>(File.ReadAllText(SciezkaPliku), OpcjeJson);
                if (u != null)
                {
                    u.Polozenie ??= new PolozenieWizualizacji();
                    if (string.IsNullOrEmpty(u.Przyrostek))
                        u.Przyrostek = DomyslnyPrzyrostek;
                    return (u, null);
                }
            }
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return (new Ustawienia(), $"Nie udało się odczytać ustawień z pliku {SciezkaPliku}. Użyte zostaną ustawienia domyślne.\n\n{e.Message}");
        }
        return (new Ustawienia(), null);
    }

    public void Zapisz()
    {
        Directory.CreateDirectory(Folder);
        var tymczasowy = SciezkaPliku + ".tmp";
        File.WriteAllText(tymczasowy, JsonSerializer.Serialize(this, OpcjeJson));
        File.Move(tymczasowy, SciezkaPliku, overwrite: true);
    }

    public Ustawienia Kopia() =>
        JsonSerializer.Deserialize<Ustawienia>(JsonSerializer.Serialize(this, OpcjeJson), OpcjeJson)!;

    public KlientZnacznikaCzasu UtworzKlientaTsa() => KlientZnacznikaCzasu.DlaAdresu(AdresTsa, LoginTsa, HasloTsa);

    /// <summary>Kopia logo w folderze ustawień — podpis działa także po przeniesieniu oryginału.</summary>
    public static string? ZachowajKopieLogo(string? zrodlo)
    {
        Directory.CreateDirectory(Folder);
        string? cel = null;
        if (!string.IsNullOrWhiteSpace(zrodlo))
        {
            var pelna = Path.GetFullPath(zrodlo);
            cel = Path.Combine(Folder, "logo" + Path.GetExtension(pelna).ToLowerInvariant());
            if (pelna != cel)
            {
                var tymczasowy = cel + ".tmp";
                File.Copy(pelna, tymczasowy, overwrite: true);
                File.Move(tymczasowy, cel, overwrite: true);
            }
        }
        foreach (var stary in Directory.GetFiles(Folder, "logo.*"))
        {
            if (stary != cel)
                File.Delete(stary);
        }
        return cel;
    }

    public OpcjePodpisu UtworzOpcjePodpisu() => new()
    {
        Stanowisko = Stanowisko,
        SciezkaLogo = SciezkaLogo,
        Motyw = MotywWizualizacji,
        Polozenie = Polozenie,
        PoziomLT = PoziomLT,
    };
}
