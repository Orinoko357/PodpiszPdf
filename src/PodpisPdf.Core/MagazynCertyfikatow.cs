using System.Security.Cryptography.X509Certificates;

namespace PodpisPdf.Core;

/// <summary>Odczyt certyfikatów z osobistego magazynu Windows („Osobisty”).</summary>
public static class MagazynCertyfikatow
{
    /// <summary>Certyfikaty nadające się do podpisu; kwalifikowane na początku listy.</summary>
    public static IReadOnlyList<CertyfikatInfo> PobierzCertyfikatyDoPodpisu()
    {
        using var magazyn = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        magazyn.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
        var teraz = DateTime.Now;
        return magazyn.Certificates
            .Where(c => CertyfikatInfo.CzyNadajeSieDoPodpisu(c, teraz))
            .Select(c => new CertyfikatInfo(c))
            .OrderByDescending(c => c.Kwalifikowany)
            .ThenBy(c => c.ImieNazwisko, StringComparer.CurrentCultureIgnoreCase)
            .ThenByDescending(c => c.WaznyDo)
            .ToList();
    }
}
