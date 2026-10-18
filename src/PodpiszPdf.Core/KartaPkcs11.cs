using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using iText.Signatures;
using Net.Pkcs11Interop.Common;
using Net.Pkcs11Interop.HighLevelAPI;
using Net.Pkcs11Interop.HighLevelAPI.Factories;

namespace PodpiszPdf.Core;

/// <summary>Miejsce certyfikatu na karcie: biblioteka PKCS#11 sterownika, token (numer seryjny) i identyfikator klucza (CKA_ID).</summary>
public sealed record ZrodloPkcs11(string Biblioteka, string NumerTokenu, string EtykietaTokenu, byte[] IdKlucza, bool CzytnikZKlawiatura);

/// <summary>Stan PIN-u tokenu (ostrzeżenia przed zablokowaniem karty).</summary>
public enum StanPinu { Zwykly, MaloProb, OstatniaProba, Zablokowany }

/// <summary>
/// Certyfikaty i podpis kartą przez bibliotekę PKCS#11 sterownika (macOS — systemowy pęk kluczy nie udostępnia
/// .NET certyfikatów z kart). PIN wpisuje użytkownik w oknie programu, chyba że czytnik ma własną klawiaturę.
/// </summary>
public static class KartaPkcs11
{
    internal static readonly Pkcs11InteropFactories Fabryki = UtworzFabryki();

    /// <summary>
    /// Pkcs11Interop wczytuje sterowniki przez „libdl”; w nowszych Linuksach jest tylko libdl.so.2 (bez pakietów
    /// deweloperskich) — wskazujemy ją. W macOS „libdl” to część systemu.
    /// </summary>
    private static Pkcs11InteropFactories UtworzFabryki()
    {
        if (OperatingSystem.IsLinux())
        {
            try
            {
                System.Runtime.InteropServices.NativeLibrary.SetDllImportResolver(typeof(Pkcs11InteropFactories).Assembly,
                    (nazwa, _, _) => nazwa == "libdl" && System.Runtime.InteropServices.NativeLibrary.TryLoad("libdl.so.2", out var uchwyt)
                        ? uchwyt
                        : IntPtr.Zero);
            }
            catch (InvalidOperationException)
            {
                // Program ustawił już własne wyszukiwanie bibliotek.
            }
        }
        return new Pkcs11InteropFactories();
    }

    /// <summary>
    /// Znane miejsca bibliotek PKCS#11 na macOS (i w Linuksie): sterowniki Certum (proCertum CardManager),
    /// OpenSC i inne. Do tego pliki z „pkcs11”/„p11” w nazwie w typowych folderach bibliotek.
    /// </summary>
    public static IReadOnlyList<string> ZnajdzBiblioteki()
    {
        var znane = new[]
        {
            "/usr/local/lib/libcrypto3PKCS.dylib",
            "/usr/local/lib/libcryptoCertum3PKCS.dylib",
            "/Library/Frameworks/cryptoCertum3PKCS.framework/cryptoCertum3PKCS",
            "/Applications/proCertumCardManager.app/Contents/Frameworks/libcrypto3PKCS.dylib",
            "/Library/OpenSC/lib/opensc-pkcs11.so",
            "/usr/local/lib/opensc-pkcs11.so",
            "/opt/homebrew/lib/opensc-pkcs11.so",
            "/usr/lib/x86_64-linux-gnu/opensc-pkcs11.so",
        };
        var wynik = new List<string>();
        foreach (var sciezka in znane)
            Dodaj(sciezka);

        foreach (var folder in new[] { "/usr/local/lib", "/opt/homebrew/lib", "/Library/OpenSC/lib" })
        {
            foreach (var plik in Pliki(folder))
            {
                var nazwa = Path.GetFileName(plik).ToLowerInvariant();
                if ((nazwa.Contains("pkcs11") || nazwa.Contains("p11") || nazwa.Contains("3pkcs")) &&
                    (nazwa.EndsWith(".dylib") || nazwa.EndsWith(".so")) && !nazwa.Contains("p11-kit") && !nazwa.Contains("spy"))
                    Dodaj(plik);
            }
        }
        return wynik;

        void Dodaj(string sciezka)
        {
            try
            {
                if (!File.Exists(sciezka))
                    return;
                var pelna = Path.GetFullPath(sciezka);
                // Dowiązania (np. /usr/local/lib/opensc-pkcs11.so → /Library/OpenSC/…) liczymy raz.
                var cel = new FileInfo(pelna).ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? pelna;
                if (!wynik.Any(w => (new FileInfo(w).ResolveLinkTarget(true)?.FullName ?? w) == cel))
                    wynik.Add(pelna);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Pomijamy niedostępny plik.
            }
        }

        static IEnumerable<string> Pliki(string folder)
        {
            try
            {
                return Directory.Exists(folder) ? Directory.GetFiles(folder) : Array.Empty<string>();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return Array.Empty<string>();
            }
        }
    }

    /// <summary>Wczytanie biblioteki sterownika z opisem błędu dla użytkownika (zła architektura, brak pliku).</summary>
    private static IPkcs11Library Wczytaj(string biblioteka)
    {
        try
        {
            return Fabryki.Pkcs11LibraryFactory.LoadPkcs11Library(Fabryki, biblioteka, AppType.MultiThreaded);
        }
        catch (Exception e) when (e is not BladPodpisu)
        {
            var rada = OperatingSystem.IsMacOS() && System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture ==
                System.Runtime.InteropServices.Architecture.Arm64
                ? " Jeśli sterownik karty jest tylko dla procesorów Intel, użyj wersji PodpiszPdf dla Intela (działa przez Rosettę)."
                : "";
            throw new BladPodpisu($"Nie udało się wczytać sterownika karty {Path.GetFileName(biblioteka)}: {e.Message}.{rada}", e);
        }
    }

    /// <summary>
    /// Certyfikaty do podpisu ze wszystkich tokenów (kart) widocznych przez bibliotekę. Klucze prywatne są zwykle
    /// niewidoczne przed podaniem PIN-u — do listy trafiają certyfikaty ważne, z użyciem „niezaprzeczalność”.
    /// </summary>
    public static IReadOnlyList<CertyfikatInfo> PobierzCertyfikaty(string biblioteka, DateTime teraz)
    {
        using var lib = Wczytaj(biblioteka);
        var wynik = new List<CertyfikatInfo>();
        foreach (var slot in lib.GetSlotList(SlotsType.WithTokenPresent))
        {
            ITokenInfo token;
            ISession sesja;
            try
            {
                token = slot.GetTokenInfo();
                if (!token.TokenFlags.TokenInitialized)
                    continue; // pusta (niezainicjowana) karta
                sesja = slot.OpenSession(SessionType.ReadOnly);
            }
            catch (Pkcs11Exception)
            {
                continue; // czytnik bez działającej karty
            }
            using var _ = sesja;
            foreach (var (certyfikat, id) in Certyfikaty(sesja))
            {
                try
                {
                    if (CertyfikatInfo.PowodNiewaznosci(certyfikat, teraz) != null)
                        continue;
                    var zrodlo = new ZrodloPkcs11(biblioteka, token.SerialNumber.Trim(), token.Label.Trim(), id,
                        token.TokenFlags.ProtectedAuthenticationPath);
                    wynik.Add(new CertyfikatInfo(certyfikat, zrodlo));
                }
                catch (Exception)
                {
                    // Nietypowy certyfikat pomijamy — nie blokuje listy.
                }
            }
        }
        return wynik;
    }

    /// <summary>Wszystkie certyfikaty X.509 na tokenie (także wystawców — do łańcucha w podpisie).</summary>
    private static IEnumerable<(X509Certificate2 Certyfikat, byte[] Id)> Certyfikaty(ISession sesja)
    {
        var szablon = new List<IObjectAttribute>
        {
            Fabryki.ObjectAttributeFactory.Create(CKA.CKA_CLASS, CKO.CKO_CERTIFICATE),
            Fabryki.ObjectAttributeFactory.Create(CKA.CKA_CERTIFICATE_TYPE, CKC.CKC_X_509),
        };
        foreach (var obiekt in sesja.FindAllObjects(szablon))
        {
            X509Certificate2 cert;
            byte[] id;
            try
            {
                var wartosci = sesja.GetAttributeValue(obiekt, new List<CKA> { CKA.CKA_VALUE, CKA.CKA_ID });
                cert = new X509Certificate2(wartosci[0].GetValueAsByteArray());
                id = wartosci[1].GetValueAsByteArray() ?? Array.Empty<byte>();
            }
            catch (Exception)
            {
                continue;
            }
            yield return (cert, id);
        }
    }

    /// <summary>
    /// Otwiera sesję z kartą i loguje użytkownika. <paramref name="pin"/> null — czytnik z klawiaturą (PIN wpisywany
    /// na czytniku). Błędny PIN kończy się wyjątkiem <see cref="BladPinu"/> z liczbą pozostałych prób, o ile karta ją podaje.
    /// </summary>
    public static SesjaKarty Otworz(ZrodloPkcs11 zrodlo, X509Certificate2 certyfikat, string? pin)
    {
        var lib = Wczytaj(zrodlo.Biblioteka);
        try
        {
            var slot = lib.GetSlotList(SlotsType.WithTokenPresent)
                .FirstOrDefault(s => TokenInfo(s)?.SerialNumber.Trim() == zrodlo.NumerTokenu)
                ?? throw new BladPodpisu($"Nie znaleziono karty „{zrodlo.EtykietaTokenu}”. Sprawdź, czy karta jest w czytniku.");
            var sesja = slot.OpenSession(SessionType.ReadOnly);
            try
            {
                Zaloguj(sesja, CKU.CKU_USER, pin, slot);
                var klucz = ZnajdzKlucz(sesja, zrodlo.IdKlucza)
                            ?? throw new BladPodpisu("Na karcie nie ma klucza prywatnego dla wybranego certyfikatu.");
                var zawszePin = false;
                try
                {
                    zawszePin = sesja.GetAttributeValue(klucz, new List<CKA> { CKA.CKA_ALWAYS_AUTHENTICATE })[0].GetValueAsBool();
                }
                catch (Exception)
                {
                    // Atrybut nieobsługiwany — PIN tylko przy logowaniu.
                }
                return new SesjaKarty(lib, sesja, klucz, certyfikat, zawszePin ? pin ?? "" : null, zrodlo.CzytnikZKlawiatura,
                    Fabryki.MechanismFactory);
            }
            catch
            {
                sesja.Dispose();
                throw;
            }
        }
        catch
        {
            lib.Dispose();
            throw;
        }
    }

    /// <summary>Stan licznika prób PIN-u tokenu (przed zapytaniem o PIN).</summary>
    public static StanPinu SprawdzPin(ZrodloPkcs11 zrodlo)
    {
        using var lib = Wczytaj(zrodlo.Biblioteka);
        var token = lib.GetSlotList(SlotsType.WithTokenPresent).Select(TokenInfo)
            .FirstOrDefault(t => t?.SerialNumber.Trim() == zrodlo.NumerTokenu);
        return token == null ? StanPinu.Zwykly : Stan(token);
    }

    private static StanPinu Stan(ITokenInfo t) =>
        t.TokenFlags.UserPinLocked ? StanPinu.Zablokowany
        : t.TokenFlags.UserPinFinalTry ? StanPinu.OstatniaProba
        : t.TokenFlags.UserPinCountLow ? StanPinu.MaloProb
        : StanPinu.Zwykly;

    private static ITokenInfo? TokenInfo(ISlot slot)
    {
        try
        {
            return slot.GetTokenInfo();
        }
        catch (Pkcs11Exception)
        {
            return null;
        }
    }

    private static void Zaloguj(ISession sesja, CKU uzytkownik, string? pin, ISlot slot)
    {
        try
        {
            if (pin == null)
                sesja.Login(uzytkownik, (byte[]?)null);
            else
                sesja.Login(uzytkownik, pin);
        }
        catch (Pkcs11Exception e) when (e.RV == CKR.CKR_USER_ALREADY_LOGGED_IN)
        {
            // Inny program zalogował się już do karty — sesja jest dostępna.
        }
        catch (Pkcs11Exception e)
        {
            throw BladLogowania(e, TokenInfo(slot));
        }
    }

    internal static Exception BladLogowania(Pkcs11Exception e, ITokenInfo? token)
    {
        var stan = token == null ? StanPinu.Zwykly : Stan(token);
        return e.RV switch
        {
            CKR.CKR_PIN_INCORRECT => new BladPinu(stan switch
            {
                StanPinu.OstatniaProba => "Błędny PIN. Została ostatnia próba — kolejny błąd zablokuje kartę.",
                StanPinu.MaloProb => "Błędny PIN. Zostało niewiele prób przed zablokowaniem karty.",
                StanPinu.Zablokowany => "Błędny PIN. Karta jest zablokowana — odblokuj ją kodem PUK w programie producenta karty.",
                _ => "Błędny PIN.",
            }, stan),
            CKR.CKR_PIN_LOCKED => new BladPinu("PIN karty jest zablokowany. Odblokuj go kodem PUK w programie producenta karty.", StanPinu.Zablokowany),
            CKR.CKR_PIN_LEN_RANGE or CKR.CKR_PIN_INVALID => new BladPinu("PIN ma niewłaściwą długość lub znaki.", stan),
            CKR.CKR_FUNCTION_CANCELED => new OperationCanceledException("Anulowano wpisywanie PIN-u na czytniku."),
            _ => new BladPodpisu($"Karta odrzuciła logowanie ({e.RV}).", e),
        };
    }

    private static IObjectHandle? ZnajdzKlucz(ISession sesja, byte[] id)
    {
        var szablon = new List<IObjectAttribute>
        {
            Fabryki.ObjectAttributeFactory.Create(CKA.CKA_CLASS, CKO.CKO_PRIVATE_KEY),
            Fabryki.ObjectAttributeFactory.Create(CKA.CKA_ID, id),
        };
        return sesja.FindAllObjects(szablon).FirstOrDefault();
    }

    /// <summary>Certyfikaty wystawców zapisane na karcie (do łańcucha w podpisie, gdy system ich nie zna).</summary>
    internal static X509Certificate2Collection PozostaleCertyfikaty(ISession sesja, X509Certificate2 wlasny)
    {
        var wynik = new X509Certificate2Collection();
        foreach (var (c, _) in Certyfikaty(sesja))
        {
            if (c.Thumbprint != wlasny.Thumbprint)
                wynik.Add(c);
        }
        return wynik;
    }
}

/// <summary>Błędny lub zablokowany PIN.</summary>
public sealed class BladPinu(string komunikat, StanPinu stan) : BladPodpisu(komunikat)
{
    public StanPinu Stan { get; } = stan;
}

/// <summary>
/// Zalogowana sesja z kartą i podpis jej kluczem (dla iText). Zamknięcie wylogowuje i zwalnia bibliotekę sterownika.
/// </summary>
public sealed class SesjaKarty : IExternalSignature, IDisposable
{
    // DigestInfo dla SHA-256 (RFC 8017, 9.2) — CKM_RSA_PKCS podpisuje gotowy skrót z tym nagłówkiem.
    private static readonly byte[] NaglowekSha256 =
        { 0x30, 0x31, 0x30, 0x0d, 0x06, 0x09, 0x60, 0x86, 0x48, 0x01, 0x65, 0x03, 0x04, 0x02, 0x01, 0x05, 0x00, 0x04, 0x20 };

    private readonly IPkcs11Library _lib;
    private readonly ISession _sesja;
    private readonly IObjectHandle _klucz;
    private readonly string? _pinKontekstowy;
    private readonly bool _czytnikZKlawiatura;
    private readonly IMechanismFactory _mechanizmy;
    private readonly string _algorytm;

    internal SesjaKarty(IPkcs11Library lib, ISession sesja, IObjectHandle klucz, X509Certificate2 certyfikat, string? pinKontekstowy,
        bool czytnikZKlawiatura, IMechanismFactory mechanizmy)
    {
        _lib = lib;
        _sesja = sesja;
        _klucz = klucz;
        _pinKontekstowy = pinKontekstowy;
        _czytnikZKlawiatura = czytnikZKlawiatura;
        _mechanizmy = mechanizmy;
        _algorytm = certyfikat.GetKeyAlgorithm() switch
        {
            "1.2.840.113549.1.1.1" => "RSA",
            "1.2.840.10045.2.1" => "ECDSA",
            var oid => throw new BladPodpisu($"Nieobsługiwany rodzaj klucza certyfikatu (OID {oid}). Obsługiwane są klucze RSA i ECDSA."),
        };
        CertyfikatyWystawcow = KartaPkcs11.PozostaleCertyfikaty(sesja, certyfikat);
    }

    /// <summary>Certyfikaty z karty poza certyfikatem podpisującego (np. wystawców).</summary>
    public X509Certificate2Collection CertyfikatyWystawcow { get; }

    public string GetDigestAlgorithmName() => "SHA-256";

    public string GetSignatureAlgorithmName() => _algorytm;

    public ISignatureMechanismParams? GetSignatureMechanismParameters() => null;

    public byte[] Sign(byte[] message)
    {
        var skrot = SHA256.HashData(message);
        try
        {
            if (_algorytm == "RSA")
                return Podpisz(CKM.CKM_RSA_PKCS, [.. NaglowekSha256, .. skrot]);

            // CKM_ECDSA zwraca r ‖ s, a CMS wymaga postaci DER (SEQUENCE { r, s }).
            var surowy = Podpisz(CKM.CKM_ECDSA, skrot);
            return PrzeksztalcEcdsa(surowy);
        }
        catch (Pkcs11Exception e)
        {
            throw KartaPkcs11.BladLogowania(e, null);
        }
    }

    private byte[] Podpisz(CKM mechanizm, byte[] dane)
    {
        var m = _mechanizmy.Create(mechanizm);
        // Klucz z CKA_ALWAYS_AUTHENTICATE (częsty przy podpisie kwalifikowanym) wymaga PIN-u przy każdym podpisie.
        if (_pinKontekstowy != null)
            return _czytnikZKlawiatura ? _sesja.Sign(m, _klucz, (byte[]?)null, dane) : _sesja.Sign(m, _klucz, _pinKontekstowy, dane);
        return _sesja.Sign(m, _klucz, dane);
    }

    internal static byte[] PrzeksztalcEcdsa(byte[] surowy)
    {
        if (surowy.Length % 2 != 0)
            throw new BladPodpisu("Karta zwróciła podpis ECDSA w nieznanym formacie.");
        var polowa = surowy.Length / 2;
        var pisarz = new System.Formats.Asn1.AsnWriter(System.Formats.Asn1.AsnEncodingRules.DER);
        using (pisarz.PushSequence())
        {
            pisarz.WriteIntegerUnsigned(BezZer(surowy.AsSpan(0, polowa)));
            pisarz.WriteIntegerUnsigned(BezZer(surowy.AsSpan(polowa)));
        }
        return pisarz.Encode();

        static ReadOnlySpan<byte> BezZer(ReadOnlySpan<byte> liczba)
        {
            var i = 0;
            while (i < liczba.Length - 1 && liczba[i] == 0)
                i++;
            return liczba[i..];
        }
    }

    public void Dispose()
    {
        try
        {
            _sesja.Logout();
        }
        catch (Pkcs11Exception)
        {
            // Sesja mogła już wygasnąć (wyjęta karta).
        }
        _sesja.Dispose();
        _lib.Dispose();
    }
}
