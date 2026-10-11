using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using iText.Kernel.Pdf;

namespace PodpiszPdf.Core;

/// <summary>Wymiary strony: widoczna część (CropBox ∩ MediaBox) w punktach (układ współrzędnych PDF) i obrót.</summary>
public sealed record StronaPdf(int Numer, float Lewo, float Dol, float Szerokosc, float Wysokosc, int Obrot)
{
    public float Prawo => Lewo + Szerokosc;
    public float Gora => Dol + Wysokosc;
    public iText.Kernel.Geom.Rectangle Prostokat => new(Lewo, Dol, Szerokosc, Wysokosc);
}

/// <summary>Obraz strony w formacie BGRA (4 bajty na piksel), wiersz po wierszu od góry.</summary>
public sealed record ObrazStrony(byte[] Bgra, int Szerokosc, int Wysokosc);

/// <summary>Odczyt stron i renderowanie podglądu dokumentu PDF (silnik PDFium).</summary>
public static class DokumentPdf
{
    // PDFium nie jest bezpieczny wątkowo i źle znosi wywołania z różnych wątków (nawet kolejno, pod blokadą),
    // dlatego wszystkie wywołania wykonuje jeden stały wątek.
    private static readonly BlockingCollection<Action> Kolejka = new();

    private static readonly Thread WatekPdfium = UtworzWatek();

    private static Thread UtworzWatek()
    {
        var watek = new Thread(() =>
        {
            foreach (var zadanie in Kolejka.GetConsumingEnumerable())
                zadanie();
        })
        {
            IsBackground = true,
            Name = "PDFium",
        };
        watek.Start();
        return watek;
    }

    /// <summary>Wykonuje funkcję na wątku PDFium i czeka na wynik (wyjątki przechodzą do wywołującego).</summary>
    private static T NaWatkuPdfium<T>(Func<T> funkcja)
    {
        if (Thread.CurrentThread == WatekPdfium)
            return funkcja();
        var wynik = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Kolejka.Add(() =>
        {
            try
            {
                wynik.SetResult(funkcja());
            }
            catch (Exception e)
            {
                wynik.SetException(e);
            }
        });
        return wynik.Task.GetAwaiter().GetResult();
    }

    /// <summary>
    /// Czy autor ograniczył uprawnienia do zmian (plik zaszyfrowany samym hasłem właściciela): dokument można
    /// obejrzeć, ale nie podpisać. Plik z hasłem otwarcia kończy się wyjątkiem <c>BadPasswordException</c>.
    /// </summary>
    public static bool CzyZablokowanyDoZmian(string sciezka)
    {
        using var czytnik = new PdfReader(sciezka);
        using var dokument = new PdfDocument(czytnik);
        return czytnik.IsEncrypted() && !czytnik.IsOpenedWithFullPermission();
    }

    /// <summary>
    /// Widoczna część strony: CropBox przycięty do MediaBox — tak samo jak w PDFium, żeby położenie
    /// na podglądzie odpowiadało położeniu w podpisie.
    /// </summary>
    internal static iText.Kernel.Geom.Rectangle ObszarStrony(PdfPage strona)
    {
        var media = strona.GetMediaBox();
        var wspolna = strona.GetCropBox().GetIntersection(media);
        return wspolna != null && wspolna.GetWidth() > 0 && wspolna.GetHeight() > 0 ? wspolna : media;
    }

    public static IReadOnlyList<StronaPdf> Strony(string sciezka)
    {
        using var dokument = new PdfDocument(new PdfReader(sciezka));
        var wynik = new List<StronaPdf>();
        for (var i = 1; i <= dokument.GetNumberOfPages(); i++)
        {
            var strona = dokument.GetPage(i);
            var c = ObszarStrony(strona);
            wynik.Add(new StronaPdf(i, c.GetLeft(), c.GetBottom(), c.GetWidth(), c.GetHeight(), ((strona.GetRotation() % 360) + 360) % 360));
        }
        return wynik;
    }

    /// <summary>
    /// Renderuje stronę (indeks od 0) w skali: 1 = 1 piksel na punkt PDF. Domyślnie na białym tle;
    /// bez niego puste miejsca są przezroczyste (np. do nałożenia wizualizacji na podgląd strony).
    /// </summary>
    public static ObrazStrony Renderuj(byte[] pdf, int indeks, double skala, bool bialeTlo = true)
    {
        var (bgra, w, h) = NaWatkuPdfium(() => Pdfium.Renderuj(pdf, indeks, skala));
        if (bialeTlo)
            NaBialymTle(bgra);
        return new ObrazStrony(bgra, w, h);
    }

    /// <summary>PDFium zwraca przezroczyste tło — nakładamy obraz na białą kartkę.</summary>
    private static void NaBialymTle(byte[] bgra)
    {
        for (var i = 0; i < bgra.Length; i += 4)
        {
            var a = bgra[i + 3];
            if (a == 255)
                continue;
            var tlo = 255 - a;
            bgra[i] = (byte)(bgra[i] * a / 255 + tlo);
            bgra[i + 1] = (byte)(bgra[i + 1] * a / 255 + tlo);
            bgra[i + 2] = (byte)(bgra[i + 2] * a / 255 + tlo);
            bgra[i + 3] = 255;
        }
    }

    /// <summary>
    /// Bezpośrednie wywołania PDFium (biblioteka natywna z pakietów bblanchon.PDFium). Wcześniejsza warstwa Docnet do
    /// rysowania pól formularza (m.in. wizualizacji podpisów) przekazywała do PDFium tymczasową kopię
    /// struktury FPDF_FORMFILLINFO, do której PDFium sięgało po jej zwolnieniu (awarie 0xC0000005 przy
    /// zamykaniu dokumentu). Tu struktura leży w pamięci natywnej do końca pracy z dokumentem.
    /// Wywoływane wyłącznie z wątku PDFium.
    /// </summary>
    private static class Pdfium
    {
        private const string Biblioteka = "pdfium";
        private const int FlagaAdnotacji = 0x01; // FPDF_ANNOT — wyglądy adnotacji, m.in. wizualizacje podpisów
        // Z zapasem: wersja 1 struktury to liczba i kilkanaście wskaźników.
        private const int RozmiarFormFillInfo = 1024;
        private static bool _zainicjowano;

        public static (byte[] Bgra, int Szerokosc, int Wysokosc) Renderuj(byte[] pdf, int indeks, double skala)
        {
            if (!_zainicjowano)
            {
                FPDF_InitLibrary();
                _zainicjowano = true;
            }

            var dane = Marshal.AllocHGlobal(pdf.Length); // PDFium czyta z tej pamięci aż do zamknięcia dokumentu
            // FPDF_FORMFILLINFO w wersji 1: numer wersji, a wszystkie wywołania zwrotne puste (zera).
            var formularz = Marshal.AllocHGlobal(RozmiarFormFillInfo);
            Marshal.Copy(new byte[RozmiarFormFillInfo], 0, formularz, RozmiarFormFillInfo);
            Marshal.WriteInt32(formularz, 1);
            var srodowisko = IntPtr.Zero;
            var dokument = IntPtr.Zero;
            var strona = IntPtr.Zero;
            var bitmapa = IntPtr.Zero;
            try
            {
                Marshal.Copy(pdf, 0, dane, pdf.Length);
                dokument = FPDF_LoadMemDocument(dane, pdf.Length, null);
                if (dokument == IntPtr.Zero)
                    throw new BladPodpisu($"Nie udało się otworzyć dokumentu PDF do podglądu (błąd PDFium {FPDF_GetLastError()}).");
                srodowisko = FPDFDOC_InitFormFillEnvironment(dokument, formularz);
                strona = FPDF_LoadPage(dokument, indeks);
                if (strona == IntPtr.Zero)
                    throw new ArgumentOutOfRangeException(nameof(indeks), indeks, "Dokument nie ma strony o tym numerze.");

                var w = Math.Max(1, (int)(FPDF_GetPageWidth(strona) * skala));
                var h = Math.Max(1, (int)(FPDF_GetPageHeight(strona) * skala));
                bitmapa = FPDFBitmap_Create(w, h, 1);
                if (bitmapa == IntPtr.Zero)
                    throw new OutOfMemoryException("Za mało pamięci na obraz strony.");
                FPDFBitmap_FillRect(bitmapa, 0, 0, w, h, 0); // przezroczyste tło
                FPDF_RenderPageBitmap(bitmapa, strona, 0, 0, w, h, 0, FlagaAdnotacji);
                if (srodowisko != IntPtr.Zero)
                    FPDF_FFLDraw(srodowisko, bitmapa, strona, 0, 0, w, h, 0, FlagaAdnotacji); // pola formularza i podpisów

                var wiersz = w * 4;
                var krok = FPDFBitmap_GetStride(bitmapa);
                var bufor = FPDFBitmap_GetBuffer(bitmapa);
                var bgra = new byte[wiersz * h];
                for (var y = 0; y < h; y++)
                    Marshal.Copy(bufor + y * krok, bgra, y * wiersz, wiersz);
                return (bgra, w, h);
            }
            finally
            {
                if (bitmapa != IntPtr.Zero)
                    FPDFBitmap_Destroy(bitmapa);
                if (strona != IntPtr.Zero)
                    FPDF_ClosePage(strona);
                if (srodowisko != IntPtr.Zero)
                    FPDFDOC_ExitFormFillEnvironment(srodowisko);
                if (dokument != IntPtr.Zero)
                    FPDF_CloseDocument(dokument);
                Marshal.FreeHGlobal(formularz);
                Marshal.FreeHGlobal(dane);
            }
        }

        [DllImport(Biblioteka)] private static extern void FPDF_InitLibrary();
        [DllImport(Biblioteka)] private static extern IntPtr FPDF_LoadMemDocument(IntPtr dane, int rozmiar, [MarshalAs(UnmanagedType.LPStr)] string? haslo);
        [DllImport(Biblioteka)] private static extern void FPDF_CloseDocument(IntPtr dokument);
        [DllImport(Biblioteka)] private static extern uint FPDF_GetLastError();
        [DllImport(Biblioteka)] private static extern IntPtr FPDF_LoadPage(IntPtr dokument, int indeks);
        [DllImport(Biblioteka)] private static extern void FPDF_ClosePage(IntPtr strona);
        [DllImport(Biblioteka)] private static extern double FPDF_GetPageWidth(IntPtr strona);
        [DllImport(Biblioteka)] private static extern double FPDF_GetPageHeight(IntPtr strona);
        [DllImport(Biblioteka)] private static extern IntPtr FPDFBitmap_Create(int szerokosc, int wysokosc, int alfa);
        [DllImport(Biblioteka)] private static extern void FPDFBitmap_Destroy(IntPtr bitmapa);
        [DllImport(Biblioteka)] private static extern int FPDFBitmap_FillRect(IntPtr bitmapa, int x, int y, int szerokosc, int wysokosc, uint kolor);
        [DllImport(Biblioteka)] private static extern IntPtr FPDFBitmap_GetBuffer(IntPtr bitmapa);
        [DllImport(Biblioteka)] private static extern int FPDFBitmap_GetStride(IntPtr bitmapa);
        [DllImport(Biblioteka)] private static extern IntPtr FPDFDOC_InitFormFillEnvironment(IntPtr dokument, IntPtr informacje);
        [DllImport(Biblioteka)] private static extern void FPDFDOC_ExitFormFillEnvironment(IntPtr srodowisko);
        [DllImport(Biblioteka)] private static extern void FPDF_FFLDraw(IntPtr srodowisko, IntPtr bitmapa, IntPtr strona, int x, int y, int szerokosc, int wysokosc, int obrot, int flagi);
        [DllImport(Biblioteka)] private static extern void FPDF_RenderPageBitmap(IntPtr bitmapa, IntPtr strona, int x, int y, int szerokosc, int wysokosc, int obrot, int flagi);
    }
}
