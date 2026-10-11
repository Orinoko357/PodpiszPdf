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
        return ZbudujListe(magazyn.Certificates, DateTime.Now);
    }

    /// <summary>
    /// Lista certyfikatów do podpisu. Certyfikat, którego nie da się odczytać (nietypowa budowa, uszkodzenie),
    /// jest pomijany — nie może zablokować całej listy.
    /// </summary>
    internal static IReadOnlyList<CertyfikatInfo> ZbudujListe(IEnumerable<X509Certificate2> certyfikaty, DateTime teraz)
    {
        var wynik = new List<CertyfikatInfo>();
        foreach (var c in certyfikaty)
        {
            try
            {
                if (CertyfikatInfo.CzyNadajeSieDoPodpisu(c, teraz))
                    wynik.Add(new CertyfikatInfo(c));
            }
            catch (Exception)
            {
                // Pomijamy tylko ten certyfikat.
            }
        }
        return wynik
            .OrderByDescending(c => c.Kwalifikowany)
            .ThenBy(c => c.ImieNazwisko, StringComparer.CurrentCultureIgnoreCase)
            .ThenByDescending(c => c.WaznyDo)
            .ToList();
    }
}
