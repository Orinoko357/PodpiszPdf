using PodpiszPdf.Core;
using Xunit;

namespace PodpiszPdf.Tests;

/// <summary>Treść, której nie da się pokazać na wizualizacji (audyt 2026-10).</summary>
public class WizualizacjaNietypoweTesty
{
    // C9: znaki spoza czcionki znikały z wizualizacji bez ostrzeżenia.
    [Fact]
    public void ZnakiSpozaCzcionkiWNazwiskuDajaZrozumialyBlad()
    {
        var folder = PlikiTestowe.Folder();
        var wyjscie = Path.Combine(folder, "w.pdf");
        using var cert = CertyfikatyTestowe.Utworz(imie: null, nazwisko: null, cn: "Wang 王小明");

        var e = Assert.Throws<BladPodpisu>(() => new PodpisywaczPdf(new TestowyTsa()).Podpisz(
            PlikiTestowe.UtworzPdf(folder), wyjscie, cert, new OpcjePodpisu()));
        Assert.Contains("王", e.Message);
        Assert.Contains("Imię i nazwisko", e.Message);
        Assert.False(File.Exists(wyjscie));
    }

    [Fact]
    public void ZnakiSpozaCzcionkiWStanowiskuDajaZrozumialyBlad()
    {
        var e = Assert.Throws<BladPodpisu>(() => Wizualizacja.Rozmiar(
            new DaneWizualizacji("Jan Kowalski", "Kierownik 😀 działu", null, DateTimeOffset.Now)));
        Assert.Contains("😀", e.Message);
        Assert.Contains("stanowisk", e.Message);
    }

    [Fact]
    public void PolskieZnakiSaObslugiwane()
    {
        var r = Wizualizacja.Rozmiar(new DaneWizualizacji("Zażółć Gęślą-Jaźń", "Główny księgowy\tŁódź", null, DateTimeOffset.Now));
        Assert.True(r.Szerokosc > 0);
    }

    // Wizualizacja szersza niż strona dawała mylące „Wskaż miejsce ponownie”.
    [Fact]
    public void WizualizacjaSzerszaNizStronaDajeZrozumialyBlad()
    {
        var folder = PlikiTestowe.Folder();
        var wejscie = PlikiTestowe.UtworzPdf(folder);
        using var cert = CertyfikatyTestowe.Utworz();
        var stanowisko = string.Join("\n", Enumerable.Repeat(string.Join(" ", Enumerable.Repeat("Kierownik", 30)), 2));

        var e = Assert.Throws<BladPodpisu>(() => new PodpisywaczPdf(new TestowyTsa()).Podpisz(
            wejscie, Path.Combine(folder, "w.pdf"), cert, new OpcjePodpisu { Stanowisko = stanowisko, Miejsce = new MiejscePodpisu(1, 0, 0) }));
        Assert.Contains("szersza niż strona", e.Message);

        e = Assert.Throws<BladPodpisu>(() => new PodpisywaczPdf(new TestowyTsa()).Podpisz(
            wejscie, Path.Combine(folder, "w.pdf"), cert, new OpcjePodpisu { Stanowisko = stanowisko }));
        Assert.Contains("szersza niż strona", e.Message);
    }
}
