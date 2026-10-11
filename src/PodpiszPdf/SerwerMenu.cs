using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PodpiszPdf;

/// <summary>
/// Pozycja „Podpisz w PodpiszPdf” w nowym menu kontekstowym Windows 11. Eksplorator uruchamia program
/// z parametrem <c>-Embedding</c> (serwer COM zadeklarowany w pakiecie PodpiszPdf) i pyta obiekt
/// <see cref="PolecenieMenu"/> o tytuł, ikonę i wykonanie. Bez interfejsu WinUI — start jest szybki.
/// </summary>
internal static class SerwerMenu
{
    public const string ParametrSerwera = "-Embedding";

    // Identyfikator klasy polecenia — taki sam jak w AppxManifest.xml pakietu.
    public static readonly Guid IdKlasy = new("6E2F4C3A-8B1D-4F5E-9A7C-2D3B4E5F6A71");

    // Serwer kończy pracę po tym czasie bez wywołań (Eksplorator pyta przy każdym otwarciu menu).
    private static readonly TimeSpan CzasBezczynnosci = TimeSpan.FromMinutes(3);
    private static long _ostatnieWywolanie = Environment.TickCount64;

    internal static void Aktywnosc() => Interlocked.Exchange(ref _ostatnieWywolanie, Environment.TickCount64);

    /// <summary>Rejestruje fabrykę klasy i czeka, aż Eksplorator przestanie z niej korzystać.</summary>
    public static int Uruchom()
    {
        var watek = new Thread(() =>
        {
            const uint ClsctxLocalServer = 0x4, RegclsMultipleUse = 1;
            var id = IdKlasy;
            var hr = CoRegisterClassObject(ref id, new FabrykaPolecenia(), ClsctxLocalServer, RegclsMultipleUse, out var cookie);
            if (hr != 0)
                return;
            while (Environment.TickCount64 - Interlocked.Read(ref _ostatnieWywolanie) < CzasBezczynnosci.TotalMilliseconds)
                Thread.Sleep(1000);
            CoRevokeClassObject(cookie);
        });
        // Obiekty .NET są wielowątkowe — serwer w apartamencie MTA nie potrzebuje pętli komunikatów.
        watek.SetApartmentState(ApartmentState.MTA);
        watek.Start();
        watek.Join();
        return 0;
    }

    [DllImport("ole32.dll")]
    private static extern int CoRegisterClassObject(ref Guid rclsid, [MarshalAs(UnmanagedType.IUnknown)] object pUnk,
        uint dwClsContext, uint flags, out uint lpdwRegister);

    [DllImport("ole32.dll")]
    private static extern int CoRevokeClassObject(uint dwRegister);

    // ---- Interfejsy COM powłoki Windows ----

    [ComImport, Guid("00000001-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IClassFactory
    {
        [PreserveSig] int CreateInstance(IntPtr pUnkOuter, ref Guid riid, out IntPtr ppvObject);
        [PreserveSig] int LockServer([MarshalAs(UnmanagedType.Bool)] bool fLock);
    }

    [ComImport, Guid("a08ce4d0-fa25-44ab-b57c-c7b1c323e0b9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IExplorerCommand
    {
        [PreserveSig] int GetTitle(IShellItemArray? psiItemArray, out IntPtr ppszName);
        [PreserveSig] int GetIcon(IShellItemArray? psiItemArray, out IntPtr ppszIcon);
        [PreserveSig] int GetToolTip(IShellItemArray? psiItemArray, out IntPtr ppszInfotip);
        [PreserveSig] int GetCanonicalName(out Guid pguidCommandName);
        [PreserveSig] int GetState(IShellItemArray? psiItemArray, [MarshalAs(UnmanagedType.Bool)] bool fOkToBeSlow, out uint pCmdState);
        [PreserveSig] int Invoke(IShellItemArray? psiItemArray, IntPtr pbc);
        [PreserveSig] int GetFlags(out uint pFlags);
        [PreserveSig] int EnumSubCommands(out IntPtr ppEnum);
    }

    [ComImport, Guid("b63ea76d-1f85-456f-a19c-48159efa858b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IShellItemArray
    {
        void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppvOut);
        void GetPropertyStore(int flags, ref Guid riid, out IntPtr ppv);
        void GetPropertyDescriptionList(IntPtr keyType, ref Guid riid, out IntPtr ppv);
        void GetAttributes(int attribFlags, uint sfgaoMask, out uint psfgaoAttribs);
        void GetCount(out uint pdwNumItems);
        void GetItemAt(uint dwIndex, out IShellItem ppsi);
        void EnumItems(out IntPtr ppenumShellItems);
    }

    [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IShellItem
    {
        void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
        void GetParent(out IShellItem ppsi);
        void GetDisplayName(uint sigdnName, out IntPtr ppszName);
        void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
        void Compare(IShellItem psi, uint hint, out int piOrder);
    }

    private const int SOk = 0, ENotImpl = unchecked((int)0x80004001), ENoInterface = unchecked((int)0x80004002);
    private const int ClassENoAggregation = unchecked((int)0x80040110);

    [ComVisible(true)]
    private sealed class FabrykaPolecenia : IClassFactory
    {
        private static readonly Guid IdIUnknown = new("00000000-0000-0000-C000-000000000046");

        public int CreateInstance(IntPtr pUnkOuter, ref Guid riid, out IntPtr ppvObject)
        {
            ppvObject = IntPtr.Zero;
            if (pUnkOuter != IntPtr.Zero)
                return ClassENoAggregation;
            Aktywnosc();
            var polecenie = new PolecenieMenu();
            if (riid == typeof(IExplorerCommand).GUID)
            {
                ppvObject = Marshal.GetComInterfaceForObject(polecenie, typeof(IExplorerCommand));
                return SOk;
            }
            if (riid == IdIUnknown)
            {
                ppvObject = Marshal.GetIUnknownForObject(polecenie);
                return SOk;
            }
            return ENoInterface;
        }

        public int LockServer(bool fLock) => SOk;
    }

    [ComVisible(true)]
    internal sealed class PolecenieMenu : IExplorerCommand
    {
        public int GetTitle(IShellItemArray? psiItemArray, out IntPtr ppszName)
        {
            Aktywnosc();
            ppszName = Marshal.StringToCoTaskMemUni(MenuKontekstowe.Etykieta);
            return SOk;
        }

        public int GetIcon(IShellItemArray? psiItemArray, out IntPtr ppszIcon)
        {
            ppszIcon = Marshal.StringToCoTaskMemUni($"{Instalacja.BiezacyPlik},0");
            return SOk;
        }

        public int GetToolTip(IShellItemArray? psiItemArray, out IntPtr ppszInfotip)
        {
            ppszInfotip = IntPtr.Zero;
            return ENotImpl;
        }

        public int GetCanonicalName(out Guid pguidCommandName)
        {
            pguidCommandName = IdKlasy;
            return SOk;
        }

        public int GetState(IShellItemArray? psiItemArray, bool fOkToBeSlow, out uint pCmdState)
        {
            Aktywnosc();
            pCmdState = 0; // ECS_ENABLED
            return SOk;
        }

        public int Invoke(IShellItemArray? psiItemArray, IntPtr pbc)
        {
            Aktywnosc();
            try
            {
                // Ścieżki odczytujemy od razu, przekazanie w tle — Eksplorator czeka na powrót z Invoke.
                var pliki = Pliki(psiItemArray).ToList();
                new Thread(() =>
                {
                    try
                    {
                        foreach (var plik in pliki)
                            PrzekazPlik(plik);
                    }
                    catch (Exception e)
                    {
                        App.Zapisz(e);
                    }
                    Aktywnosc();
                }) { IsBackground = false }.Start();
            }
            catch (Exception e)
            {
                App.Zapisz(e);
            }
            return SOk;
        }

        public int GetFlags(out uint pFlags)
        {
            pFlags = 0; // ECF_DEFAULT
            return SOk;
        }

        public int EnumSubCommands(out IntPtr ppEnum)
        {
            ppEnum = IntPtr.Zero;
            return ENotImpl;
        }

        private static IEnumerable<string> Pliki(IShellItemArray? elementy)
        {
            if (elementy == null)
                yield break;
            const uint SigdnFilesysPath = 0x80058000;
            elementy.GetCount(out var liczba);
            for (uint i = 0; i < liczba; i++)
            {
                elementy.GetItemAt(i, out var element);
                element.GetDisplayName(SigdnFilesysPath, out var wskaznik);
                var sciezka = Marshal.PtrToStringUni(wskaznik);
                Marshal.FreeCoTaskMem(wskaznik);
                if (!string.IsNullOrEmpty(sciezka) && sciezka.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                    yield return sciezka;
            }
        }

        /// <summary>
        /// Plik trafia do działającej kopii programu. Gdy program nie działa, uruchamia go Eksplorator
        /// (proces potomny serwera miałby tożsamość pakietu, z którą okno WinUI nie działa), a plik
        /// przekazujemy przez potok, gdy program będzie gotowy.
        /// </summary>
        private static void PrzekazPlik(string plik)
        {
            var polecenie = JednaInstancja.PrefiksPliku + plik;
            if (JednaInstancja.Wyslij(polecenie, 1500) == JednaInstancja.Ok)
                return;
            Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"),
                $"\"{Instalacja.BiezacyPlik}\"") { UseShellExecute = false });
            var koniec = Environment.TickCount64 + 30000;
            while (Environment.TickCount64 < koniec)
            {
                Thread.Sleep(500);
                if (JednaInstancja.Wyslij(polecenie, 1000) == JednaInstancja.Ok)
                    return;
            }
        }
    }
}
