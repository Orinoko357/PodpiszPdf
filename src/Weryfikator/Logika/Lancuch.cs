using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;

namespace Weryfikator.Logika
{
    internal enum Kotwica { Brak, ListaUE, System, Uzytkownik }

    internal sealed class WynikLancucha
    {
        public Kotwica Kotwica;
        public WpisListy? Wpis;
        /// <summary>Usługa z listy zaufania miała w czasie podpisu status „granted” (lub dawny odpowiednik).</summary>
        public bool UslugaAktywna;
        public bool Uniewazniony;
        public bool NiewaznyCzasowo;
        public bool BezSprawdzeniaUniewaznienia;
        public List<string> Bledy = new List<string>();
        public string? Korzen;
    }

    /// <summary>Budowa ścieżki certyfikacji do urzędu z listy zaufania UE albo do zaufanego certyfikatu głównego.</summary>
    internal static class Lancuch
    {
        public static WynikLancucha Zbuduj(X509Certificate2 cert, X509Certificate2Collection dodatkowe, DateTime czasUtc,
            ListaZaufania? lista, IList<X509Certificate2> uzytkownika, bool online, string typUslugi)
        {
            var w = new WynikLancucha();
            using var chain = new X509Chain();
            chain.ChainPolicy.RevocationMode = online ? X509RevocationMode.Online : X509RevocationMode.NoCheck;
            chain.ChainPolicy.RevocationFlag = X509RevocationFlag.ExcludeRoot;
            chain.ChainPolicy.UrlRetrievalTimeout = TimeSpan.FromSeconds(15);
            chain.ChainPolicy.VerificationFlags = X509VerificationFlags.AllowUnknownCertificateAuthority;
            chain.ChainPolicy.VerificationTime = czasUtc.ToLocalTime();
            foreach (var c in dodatkowe) chain.ChainPolicy.ExtraStore.Add(c);
            foreach (var c in uzytkownika) chain.ChainPolicy.ExtraStore.Add(c);
            if (lista != null)
            {
                // Urzędy z listy zaufania o nazwie zgodnej z wystawcą (i wyżej, do 6 poziomów).
                var nazwy = new List<X500DistinguishedName> { cert.IssuerName };
                nazwy.AddRange(dodatkowe.Cast<X509Certificate2>().Select(c => c.IssuerName));
                var widziane = new HashSet<string>();
                for (int poziom = 0; poziom < 6 && nazwy.Count > 0; poziom++)
                {
                    var nast = new List<X500DistinguishedName>();
                    foreach (var n in nazwy)
                    {
                        if (!widziane.Add(Hex.Z(n.RawData))) continue;
                        foreach (var c in lista.PoPodmiocie(n)) { chain.ChainPolicy.ExtraStore.Add(c); nast.Add(c.IssuerName); }
                    }
                    nazwy = nast;
                }
            }

            try { chain.Build(cert); }
            catch (Exception e) { w.Bledy.Add("Nie udało się zbudować ścieżki certyfikacji: " + e.Message); return w; }

            var el = chain.ChainElements.Cast<X509ChainElement>().ToList();
            int kotwica = -1;
            if (lista != null)
                for (int i = 0; i < el.Count && kotwica < 0; i++)
                {
                    var wpisy = lista.Znajdz(el[i].Certificate).Where(x => x.Typ == typUslugi).ToList();
                    if (wpisy.Count == 0) continue;
                    kotwica = i;
                    w.Kotwica = Kotwica.ListaUE;
                    w.Wpis = wpisy.FirstOrDefault(x => x.AktywnaW(czasUtc)) ?? wpisy[0];
                    w.UslugaAktywna = w.Wpis.AktywnaW(czasUtc);
                }

            if (kotwica < 0 && el.Count > 0)
            {
                var ost = el[el.Count - 1].Certificate;
                bool samopodpisany = ost.SubjectName.RawData.SequenceEqual(ost.IssuerName.RawData);
                w.Korzen = ost.Subject + " (odcisk " + ost.Thumbprint + ")";
                if (samopodpisany && uzytkownika.Any(u => u.Thumbprint == ost.Thumbprint)) { w.Kotwica = Kotwica.Uzytkownik; kotwica = el.Count - 1; }
                else if (samopodpisany && !chain.ChainStatus.Any(s => (s.Status & (X509ChainStatusFlags.UntrustedRoot | X509ChainStatusFlags.PartialChain)) != 0))
                { w.Kotwica = Kotwica.System; kotwica = el.Count - 1; }
            }

            int do_ = kotwica < 0 ? el.Count : kotwica;
            for (int i = 0; i < do_; i++)
            {
                var c = el[i].Certificate;
                string kto = i == 0 ? "Certyfikat podpisującego" : "Certyfikat urzędu „" + c.GetNameInfo(X509NameType.SimpleName, false) + "”";
                foreach (var s in el[i].ChainElementStatus)
                {
                    if ((s.Status & X509ChainStatusFlags.NotTimeValid) != 0)
                    {
                        w.NiewaznyCzasowo = true;
                        w.Bledy.Add($"{kto} nie był ważny w chwili podpisu (ważny od {c.NotBefore:dd.MM.yyyy} do {c.NotAfter:dd.MM.yyyy}).");
                    }
                    if ((s.Status & X509ChainStatusFlags.Revoked) != 0) { w.Uniewazniony = true; w.Bledy.Add(kto + " został unieważniony."); }
                    if ((s.Status & X509ChainStatusFlags.NotSignatureValid) != 0) w.Bledy.Add(kto + " ma nieprawidłowy podpis wystawcy.");
                    if ((s.Status & (X509ChainStatusFlags.RevocationStatusUnknown | X509ChainStatusFlags.OfflineRevocation)) != 0) w.BezSprawdzeniaUniewaznienia = true;
                }
            }
            if (!online) w.BezSprawdzeniaUniewaznienia = true;
            return w;
        }
    }
}
