using System.Security.Cryptography.X509Certificates;
using PodpiszPdf.Core;
using Xunit;

namespace PodpiszPdf.Tests;

public class CertyfikatInfoTesty
{
    [Fact]
    public void ImieINazwiskoZPolGivenNameISurname()
    {
        using var c = CertyfikatyTestowe.Utworz(imie: "Anna", nazwisko: "Nowak", cn: "Inna Nazwa");
        Assert.Equal("Anna Nowak", CertyfikatInfo.PobierzImieNazwisko(c));
    }

    [Fact]
    public void ImieINazwiskoZCnGdyBrakPol()
    {
        using var c = CertyfikatyTestowe.Utworz(imie: null, nazwisko: null, cn: "Łucja Żak");
        Assert.Equal("Łucja Żak", CertyfikatInfo.PobierzImieNazwisko(c));
    }

    [Fact]
    public void RozpoznajeCertyfikatKwalifikowany()
    {
        using var kw = CertyfikatyTestowe.Utworz(kwalifikowany: true);
        using var zw = CertyfikatyTestowe.Utworz(kwalifikowany: false);
        Assert.True(CertyfikatInfo.CzyKwalifikowany(kw));
        Assert.False(CertyfikatInfo.CzyKwalifikowany(zw));
    }

    [Fact]
    public void FiltrWymagaNonRepudiation()
    {
        using var dobry = CertyfikatyTestowe.Utworz(uzycie: X509KeyUsageFlags.NonRepudiation | X509KeyUsageFlags.DigitalSignature);
        using var tylkoUwierzytelnianie = CertyfikatyTestowe.Utworz(uzycie: X509KeyUsageFlags.DigitalSignature);
        Assert.True(CertyfikatInfo.CzyNadajeSieDoPodpisu(dobry, DateTime.Now));
        Assert.False(CertyfikatInfo.CzyNadajeSieDoPodpisu(tylkoUwierzytelnianie, DateTime.Now));
    }

    [Fact]
    public void FiltrOdrzucaCertyfikatNiewazny()
    {
        using var przeterminowany = CertyfikatyTestowe.Utworz(
            od: DateTimeOffset.UtcNow.AddYears(-3), doDnia: DateTimeOffset.UtcNow.AddDays(-1));
        Assert.False(CertyfikatInfo.CzyNadajeSieDoPodpisu(przeterminowany, DateTime.Now));
    }

    [Fact]
    public void FiltrOdrzucaCertyfikatBezKluczaPrywatnego()
    {
        using var c = CertyfikatyTestowe.Utworz();
        using var publiczny = new X509Certificate2(c.RawData);
        Assert.False(CertyfikatInfo.CzyNadajeSieDoPodpisu(publiczny, DateTime.Now));
    }
}
