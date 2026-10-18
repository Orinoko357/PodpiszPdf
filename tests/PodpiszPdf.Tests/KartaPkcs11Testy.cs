using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using iText.Kernel.Pdf;
using iText.Signatures;
using Net.Pkcs11Interop.Common;
using Net.Pkcs11Interop.HighLevelAPI;
using PodpiszPdf.Core;

namespace PodpiszPdf.Tests;

/// <summary>
/// Karta programowa SoftHSM2 (biblioteka PKCS#11) z kluczami certyfikatów testowych. Gdy SoftHSM nie jest
/// zainstalowany (apt install softhsm2 / brew install softhsm), testy karty kończą się bez sprawdzania.
/// </summary>
public sealed class KartaTestowa
{
    public const string Pin = "123456";
    public const string EtykietaTokenu = "Karta testowa";
    private const string PinAdministratora = "87654321";

    private static readonly Pkcs11InteropFactories Fabryki = KartaPkcs11.Fabryki;
    private int _nastepneId;

    public KartaTestowa()
    {
        Biblioteka = new[]
        {
            "/usr/lib/softhsm/libsofthsm2.so",
            "/usr/lib/x86_64-linux-gnu/softhsm/libsofthsm2.so",
            "/opt/homebrew/lib/softhsm/libsofthsm2.so",
            "/usr/local/lib/softhsm/libsofthsm2.so",
        }.FirstOrDefault(File.Exists);
        if (Biblioteka == null)
            return;

        // SoftHSM czyta konfigurację ze zmiennej środowiskowej procesu (natywnej — stąd setenv z libc).
        var folder = PlikiTestowe.Folder();
        Directory.CreateDirectory(Path.Combine(folder, "tokeny"));
        var konfiguracja = Path.Combine(folder, "softhsm2.conf");
        File.WriteAllText(konfiguracja, $"directories.tokendir = {Path.Combine(folder, "tokeny")}\nobjectstore.backend = file\nlog.level = ERROR\n");
        if (OperatingSystem.IsMacOS())
            setenvMacOS("SOFTHSM2_CONF", konfiguracja, 1);
        else
            setenv("SOFTHSM2_CONF", konfiguracja, 1);

        var lib = KartaPkcs11.Wczytaj(Biblioteka); // ta sama biblioteka co w programie (jedno C_Initialize)
        var slot = lib.GetSlotList(SlotsType.WithTokenPresent).First();
        slot.InitToken(PinAdministratora, EtykietaTokenu);
        slot = lib.GetSlotList(SlotsType.WithTokenPresent).First(s => s.GetTokenInfo().Label.Trim() == EtykietaTokenu);
        using var sesja = slot.OpenSession(SessionType.ReadWrite);
        sesja.Login(CKU.CKU_SO, PinAdministratora);
        sesja.InitPin(Pin);
        sesja.Logout();
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int setenv(string nazwa, string wartosc, int zastap);

    [DllImport("/usr/lib/libSystem.dylib", EntryPoint = "setenv", SetLastError = true)]
    private static extern int setenvMacOS(string nazwa, string wartosc, int zastap);

    /// <summary>Ścieżka biblioteki SoftHSM albo null (brak — test pomijany).</summary>
    public string? Biblioteka { get; }

    /// <summary>Zapisuje na karcie certyfikat i jego klucz prywatny (zwraca CKA_ID).</summary>
    public byte[] Zapisz(X509Certificate2 certyfikat, bool pinPrzyKazdymPodpisie = false)
    {
        var id = new[] { (byte)Interlocked.Increment(ref _nastepneId) };
        var lib = KartaPkcs11.Wczytaj(Biblioteka!);
        var slot = lib.GetSlotList(SlotsType.WithTokenPresent).First(s => s.GetTokenInfo().Label.Trim() == EtykietaTokenu);
        using var sesja = slot.OpenSession(SessionType.ReadWrite);
        sesja.Login(CKU.CKU_USER, Pin);
        var a = Fabryki.ObjectAttributeFactory;

        sesja.CreateObject(new List<IObjectAttribute>
        {
            a.Create(CKA.CKA_CLASS, CKO.CKO_CERTIFICATE),
            a.Create(CKA.CKA_CERTIFICATE_TYPE, CKC.CKC_X_509),
            a.Create(CKA.CKA_TOKEN, true),
            a.Create(CKA.CKA_ID, id),
            a.Create(CKA.CKA_SUBJECT, certyfikat.SubjectName.RawData),
            a.Create(CKA.CKA_VALUE, certyfikat.RawData),
        });

        var klucz = new List<IObjectAttribute>
        {
            a.Create(CKA.CKA_CLASS, CKO.CKO_PRIVATE_KEY),
            a.Create(CKA.CKA_TOKEN, true),
            a.Create(CKA.CKA_PRIVATE, true),
            a.Create(CKA.CKA_SIGN, true),
            a.Create(CKA.CKA_ID, id),
            a.Create(CKA.CKA_ALWAYS_AUTHENTICATE, pinPrzyKazdymPodpisie),
        };
        if (certyfikat.GetRSAPrivateKey() is { } rsa)
        {
            var p = rsa.ExportParameters(true);
            klucz.AddRange(new[]
            {
                a.Create(CKA.CKA_KEY_TYPE, CKK.CKK_RSA),
                a.Create(CKA.CKA_MODULUS, p.Modulus!),
                a.Create(CKA.CKA_PUBLIC_EXPONENT, p.Exponent!),
                a.Create(CKA.CKA_PRIVATE_EXPONENT, p.D!),
                a.Create(CKA.CKA_PRIME_1, p.P!),
                a.Create(CKA.CKA_PRIME_2, p.Q!),
                a.Create(CKA.CKA_EXPONENT_1, p.DP!),
                a.Create(CKA.CKA_EXPONENT_2, p.DQ!),
                a.Create(CKA.CKA_COEFFICIENT, p.InverseQ!),
            });
        }
        else
        {
            var p = certyfikat.GetECDsaPrivateKey()!.ExportParameters(true);
            klucz.AddRange(new[]
            {
                a.Create(CKA.CKA_KEY_TYPE, CKK.CKK_EC),
                a.Create(CKA.CKA_EC_PARAMS, new byte[] { 0x06, 0x08, 0x2A, 0x86, 0x48, 0xCE, 0x3D, 0x03, 0x01, 0x07 }), // P-256
                a.Create(CKA.CKA_VALUE, p.D!),
            });
        }
        sesja.CreateObject(klucz);
        sesja.Logout();
        return id;
    }
}

public class KartaPkcs11Testy : IClassFixture<KartaTestowa>
{
    private readonly KartaTestowa _karta;

    public KartaPkcs11Testy(KartaTestowa karta) => _karta = karta;

    private CertyfikatInfo NaKarcie(X509Certificate2 cert, bool pinPrzyKazdymPodpisie = false)
    {
        var id = _karta.Zapisz(cert, pinPrzyKazdymPodpisie);
        var lista = KartaPkcs11.PobierzCertyfikaty(_karta.Biblioteka!, DateTime.Now);
        return Assert.Single(lista, c => c.Karta!.IdKlucza.SequenceEqual(id));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void PodpisKartaPkcs11(bool ecdsa, bool pinPrzyKazdymPodpisie)
    {
        if (_karta.Biblioteka == null)
            return;
        using var cert = CertyfikatyTestowe.Utworz(ecdsa: ecdsa);
        var info = NaKarcie(cert, pinPrzyKazdymPodpisie);
        Assert.Equal(KartaTestowa.EtykietaTokenu, info.Karta!.EtykietaTokenu);
        Assert.True(info.Kwalifikowany);
        Assert.False(info.Certyfikat.HasPrivateKey); // klucz zostaje na karcie

        var folder = PlikiTestowe.Folder();
        var wejscie = PlikiTestowe.UtworzPdf(folder);
        var wyjscie = Path.Combine(folder, "podpisany.pdf");
        using (var sesja = KartaPkcs11.Otworz(info.Karta, info.Certyfikat, KartaTestowa.Pin))
        {
            var opcje = new OpcjePodpisu { PodpisZewnetrzny = sesja, CertyfikatyWystawcow = sesja.CertyfikatyWystawcow };
            new PodpisywaczPdf(new TestowyTsa()).Podpisz(wejscie, wyjscie, info.Certyfikat, opcje);
        }

        using var pdf = new PdfDocument(new PdfReader(wyjscie));
        var su = new SignatureUtil(pdf);
        var pkcs7 = su.ReadSignatureData(Assert.Single(su.GetSignatureNames()));
        Assert.True(pkcs7.VerifySignatureIntegrityAndAuthenticity());
        Assert.Equal(ecdsa ? "SHA256withECDSA" : "SHA256withRSA", pkcs7.GetSignatureMechanismName());
        Assert.Equal(cert.Thumbprint, new X509Certificate2(pkcs7.GetSigningCertificate().GetEncoded()).Thumbprint);
    }

    [Fact]
    public void BlednyPinDajeZrozumialyBlad()
    {
        if (_karta.Biblioteka == null)
            return;
        using var cert = CertyfikatyTestowe.Utworz();
        var info = NaKarcie(cert);
        var blad = Assert.Throws<BladPinu>(() => KartaPkcs11.Otworz(info.Karta!, info.Certyfikat, "000000"));
        Assert.StartsWith("Błędny PIN", blad.Message);
        // Poprawny PIN po błędnym nadal działa.
        using var sesja = KartaPkcs11.Otworz(info.Karta!, info.Certyfikat, KartaTestowa.Pin);
    }

    [Fact]
    public void BrakBibliotekiDajeZrozumialyBlad()
    {
        var blad = Assert.Throws<BladPodpisu>(() => KartaPkcs11.PobierzCertyfikaty("/nie/ma/takiej/biblioteki.dylib", DateTime.Now));
        Assert.Contains("sterownika karty", blad.Message);
    }

    [Fact]
    public void PodpisEcdsaZKartyWPostaciDer()
    {
        // r z zerem na początku (krótsza liczba), s z najstarszym bitem 1 (wymaga zera w DER).
        var surowy = new byte[64];
        surowy[1] = 0x12;
        surowy[31] = 0x34;
        surowy[32] = 0x80;
        surowy[63] = 0x01;
        var der = SesjaKarty.PrzeksztalcEcdsa(surowy);
        var czytnik = new System.Formats.Asn1.AsnReader(der, System.Formats.Asn1.AsnEncodingRules.DER).ReadSequence();
        var r = czytnik.ReadIntegerBytes().ToArray();
        var s = czytnik.ReadIntegerBytes().ToArray();
        Assert.Equal(surowy.Skip(1).Take(31).ToArray(), r);
        Assert.Equal(new byte[] { 0 }.Concat(surowy.Skip(32)).ToArray(), s);
        // Ten sam wynik co .NET dla podpisu w postaci IEEE P1363.
        using var ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var p1363 = ec.SignData(new byte[] { 1, 2, 3 }, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        Assert.True(ec.VerifyData(new byte[] { 1, 2, 3 }, SesjaKarty.PrzeksztalcEcdsa(p1363), HashAlgorithmName.SHA256,
            DSASignatureFormat.Rfc3279DerSequence));
    }
}
