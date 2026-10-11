using iText.IO.Image;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Xobject;
using PodpiszPdf.Core;
using Xunit;

namespace PodpiszPdf.Tests;

/// <summary>Nietypowe pliki logo (audyt 2026-10).</summary>
public class LogoNietypoweTesty
{
    /// <summary>Renderuje logo dopasowane do strony o jego proporcjach; zwraca udział czerwonych pikseli.</summary>
    private static double UdzialCzerwonego((PdfXObject Obraz, float Proporcje) logo)
    {
        var (w, h) = (100f * logo.Proporcje, 100f);
        using var ms = new MemoryStream();
        using (var pdf = new PdfDocument(new PdfWriter(ms)))
        {
            var strona = pdf.AddNewPage(new iText.Kernel.Geom.PageSize(w, h));
            new iText.Kernel.Pdf.Canvas.PdfCanvas(strona).AddXObjectFittedIntoRectangle(logo.Obraz, new iText.Kernel.Geom.Rectangle(0, 0, w, h));
        }
        var o = DokumentPdf.Renderuj(ms.ToArray(), 0, 2);
        var czerwone = 0;
        for (var i = 0; i < o.Bgra.Length; i += 4)
            if (o.Bgra[i + 2] > 200 && o.Bgra[i + 1] < 60 && o.Bgra[i] < 60)
                czerwone++;
        return (double)czerwone / (o.Szerokosc * o.Wysokosc);
    }

    /// <summary>Logo PDF 200 × 100 pt z czerwonym prostokątem 100 × 50 pt w lewym dolnym rogu.</summary>
    private static string LogoPdf(string folder, int obrot = 0, bool zasobyWStronach = false)
    {
        var sciezka = Path.Combine(folder, "logo.pdf");
        using (var pdf = new PdfDocument(new PdfWriter(sciezka)))
        {
            var strona = pdf.AddNewPage(new iText.Kernel.Geom.PageSize(200, 100));
            var plotno = new iText.Kernel.Pdf.Canvas.PdfCanvas(strona);
            if (zasobyWStronach)
            {
                // Czerwony obraz (XObject), żeby treść wymagała zasobów.
                var piksele = new byte[10 * 5 * 3];
                for (var i = 0; i < piksele.Length; i += 3)
                    piksele[i] = 255;
                plotno.AddImageFittedIntoRectangle(ImageDataFactory.Create(10, 5, 3, 8, piksele, null),
                    new iText.Kernel.Geom.Rectangle(0, 0, 100, 50), false);
            }
            else
            {
                plotno.SetFillColorRgb(1, 0, 0).Rectangle(0, 0, 100, 50).Fill();
            }
            strona.SetRotation(obrot);
        }
        if (!zasobyWStronach)
            return sciezka;

        // Zasoby dziedziczone z węzła /Pages (tak zapisują niektóre programy).
        var dziedziczone = Path.Combine(folder, "logo-dziedziczone.pdf");
        using (var pdf = new PdfDocument(new PdfReader(sciezka), new PdfWriter(dziedziczone)))
        {
            var slownik = pdf.GetPage(1).GetPdfObject();
            var zasoby = slownik.Get(PdfName.Resources);
            slownik.Remove(PdfName.Resources);
            pdf.GetCatalog().GetPdfObject().GetAsDictionary(PdfName.Pages).Put(PdfName.Resources, zasoby);
        }
        return dziedziczone;
    }

    // C6
    [Fact]
    public void LogoPdfZZasobamiDziedziczonymi()
    {
        var logo = Logo.Wczytaj(LogoPdf(PlikiTestowe.Folder(), zasobyWStronach: true));
        Assert.Equal(2f, logo.Proporcje, 0.1f);
        Assert.True(UdzialCzerwonego(logo) > 0.9, "logo puste");
    }

    // C7
    [Theory]
    [InlineData(0, 2f)]
    [InlineData(90, 0.5f)]
    [InlineData(180, 2f)]
    [InlineData(270, 0.5f)]
    public void LogoPdfZObroconaStrona(int obrot, float proporcje)
    {
        var logo = Logo.Wczytaj(LogoPdf(PlikiTestowe.Folder(), obrot));
        Assert.Equal(proporcje, logo.Proporcje, 0.1f);
        Assert.True(UdzialCzerwonego(logo) > 0.9, "logo przycięte lub przesunięte");
    }

    // S5: logo SVG nie może sięgać do sieci ani do plików (np. \\serwer\udział — wyciek skrótu hasła NTLM).
    [Fact]
    public void LogoSvgNiePobieraZasobowZZewnatrz()
    {
        var nasluch = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        nasluch.Start();
        var polaczenia = 0;
        var odbior = Task.Run(async () =>
        {
            try
            {
                while (true)
                {
                    using var klient = await nasluch.AcceptTcpClientAsync();
                    Interlocked.Increment(ref polaczenia);
                }
            }
            catch (Exception)
            {
                // Zatrzymany nasłuch.
            }
        });
        try
        {
            var port = ((System.Net.IPEndPoint)nasluch.LocalEndpoint).Port;
            var folder = PlikiTestowe.Folder();
            var sciezka = Path.Combine(folder, "logo.svg");
            File.WriteAllText(sciezka, $"""
                <svg xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink" width="60" height="30">
                  <rect width="60" height="30" fill="#c00"/>
                  <image x="0" y="0" width="30" height="30" href="http://127.0.0.1:{port}/logo.png"/>
                  <image x="30" y="0" width="30" height="30" xlink:href="http://127.0.0.1:{port}/logo2.png"/>
                </svg>
                """);

            var (_, proporcje) = Logo.Wczytaj(sciezka);

            Thread.Sleep(200);
            Assert.Equal(0, Volatile.Read(ref polaczenia));
            Assert.Equal(2f, proporcje, 0.1f);
        }
        finally
        {
            nasluch.Stop();
            odbior.Wait(1000);
        }
    }

    [Fact]
    public void LogoSvgZObrazemOsadzonymDziala()
    {
        var folder = PlikiTestowe.Folder();
        var png = Convert.ToBase64String(File.ReadAllBytes(PlikiTestowe.UtworzLogo(folder)));
        var sciezka = Path.Combine(folder, "logo.svg");
        File.WriteAllText(sciezka, $"""
            <svg xmlns="http://www.w3.org/2000/svg" width="200" height="200">
              <image x="0" y="50" width="200" height="100" href="data:image/png;base64,{png}"/>
            </svg>
            """);

        var (_, proporcje) = Logo.Wczytaj(sciezka);

        Assert.Equal(2f, proporcje, 0.1f); // obraz narysowany i przycięty do treści
    }
}
