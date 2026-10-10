using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PodpisPdf.Core;

namespace PodpisPdf;

/// <summary>Ustawienia programu zapisywane w %APPDATA%\PodpisPdf\config.json.</summary>
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

    /// <summary>Hasło do TSA zaszyfrowane DPAPI (tylko bieżący użytkownik Windows może je odczytać).</summary>
    public string? HasloTsaZaszyfrowane { get; set; }

    public PolozenieWizualizacji Polozenie { get; set; } = new();
    public string Przyrostek { get; set; } = DomyslnyPrzyrostek;
    public bool PoziomLT { get; set; }

    /// <summary>Minimalizacja chowa okno do zasobnika systemowego (obok zegara).</summary>
    public bool MinimalizujDoZasobnika { get; set; } = true;

    /// <summary>Uruchamianie przy logowaniu do Windows (z kopii programu w %APPDATA%\PodpisPdf).</summary>
    public bool Autostart { get; set; }

    /// <summary>Pozycja „Podpisz w PodpisPdf” w menu kontekstowym plików PDF.</summary>
    public bool MenuKontekstowe { get; set; } = true;

    /// <summary>Odcisk (thumbprint) ostatnio użytego certyfikatu.</summary>
    public string? OstatniCertyfikat { get; set; }

    public static string Folder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PodpisPdf");

    public static string SciezkaPliku => Path.Combine(Folder, "config.json");

    [JsonIgnore]
    public string? HasloTsa
    {
        get
        {
            if (string.IsNullOrEmpty(HasloTsaZaszyfrowane))
                return null;
            try
            {
                var dane = ProtectedData.Unprotect(Convert.FromBase64String(HasloTsaZaszyfrowane), null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(dane);
            }
            catch (Exception e) when (e is CryptographicException or FormatException)
            {
                return null;
            }
        }
        set
        {
            HasloTsaZaszyfrowane = string.IsNullOrEmpty(value)
                ? null
                : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser));
        }
    }

    public static Ustawienia Wczytaj()
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
                    return u;
                }
            }
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            MessageBox.Show(
                $"Nie udało się odczytać ustawień z pliku:\n{SciezkaPliku}\n\nUżyte zostaną ustawienia domyślne.\n\nSzczegóły: {e.Message}",
                "PodpisPdf", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        return new Ustawienia();
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

    public KlientZnacznikaCzasu UtworzKlientaTsa() =>
        KlientZnacznikaCzasu.DlaAdresu(AdresTsa, LoginTsa, HasloTsa);

    /// <summary>
    /// Zapisuje kopię logo w folderze ustawień (%APPDATA%\PodpisPdf\logo.*), żeby podpis działał także
    /// po przeniesieniu lub usunięciu oryginalnego pliku. Zwraca ścieżkę kopii (null — bez logo).
    /// </summary>
    public static string? ZachowajKopieLogo(string? zrodlo)
    {
        Directory.CreateDirectory(Folder);
        string? cel = null;
        if (!string.IsNullOrWhiteSpace(zrodlo))
        {
            var pelna = Path.GetFullPath(zrodlo);
            cel = Path.Combine(Folder, "logo" + Path.GetExtension(pelna).ToLowerInvariant());
            if (!string.Equals(pelna, cel, StringComparison.OrdinalIgnoreCase))
            {
                var tymczasowy = cel + ".tmp";
                File.Copy(pelna, tymczasowy, overwrite: true);
                File.Move(tymczasowy, cel, overwrite: true);
            }
        }

        // Poprzednie logo w innym formacie nie jest już potrzebne.
        foreach (var stary in Directory.GetFiles(Folder, "logo.*"))
        {
            if (!string.Equals(stary, cel, StringComparison.OrdinalIgnoreCase))
                File.Delete(stary);
        }
        return cel;
    }

    /// <summary>Ustawienia ze starszej wersji wskazywały oryginalny plik logo — przenosimy je na kopię.</summary>
    public void PrzeniesLogoDoFolderuUstawien()
    {
        if (string.IsNullOrEmpty(SciezkaLogo) || !File.Exists(SciezkaLogo) ||
            string.Equals(Path.GetDirectoryName(Path.GetFullPath(SciezkaLogo)), Folder, StringComparison.OrdinalIgnoreCase))
            return;
        try
        {
            SciezkaLogo = ZachowajKopieLogo(SciezkaLogo);
            Zapisz();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Zostaje oryginalna ścieżka — logo nadal działa, dopóki plik istnieje.
        }
    }

    public static Icon Ikona()
    {
        using var strumien = typeof(Ustawienia).Assembly.GetManifestResourceStream("PodpisPdf.ico")!;
        return new Icon(strumien);
    }

    public OpcjePodpisu UtworzOpcjePodpisu() => new()
    {
        Stanowisko = Stanowisko,
        SciezkaLogo = SciezkaLogo,
        Polozenie = Polozenie,
        PoziomLT = PoziomLT,
    };
}
