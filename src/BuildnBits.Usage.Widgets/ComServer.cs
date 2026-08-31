using System.Runtime.InteropServices;
using WinRT;

namespace BuildnBits.Usage.Widgets;

internal static class ComServer
{
    private static readonly ManualResetEvent Exit = new(false);

    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Contains("-RegisterProcessAsComServer", StringComparer.OrdinalIgnoreCase) ||
            args.Contains("/RegisterProcessAsComServer", StringComparer.OrdinalIgnoreCase))
        {
            ComWrappersSupport.InitializeComWrappers();
            var factory = new WidgetProviderFactory();
            var registration = RegistrationServices.RegisterClassAsLocalServer(factory);
            Exit.WaitOne();
            registration.Dispose();
            return;
        }

        Environment.Exit(0);
    }

    private sealed class RegistrationServices : IDisposable
    {
        private readonly uint _cookie;

        private RegistrationServices(uint cookie) => _cookie = cookie;

        public static RegistrationServices RegisterClassAsLocalServer(WidgetProviderFactory factory)
        {
            var clsid = typeof(WidgetProvider).GUID;
            var hr = Ole32.CoRegisterClassObject(
                clsid,
                factory,
                Ole32.CLSCTX_LOCAL_SERVER,
                Ole32.REGCLS_MULTIPLEUSE | Ole32.REGCLS_SUSPENDED,
                out var cookie);
            if (hr < 0)
            {
                Marshal.ThrowExceptionForHR(hr);
            }

            hr = Ole32.CoResumeClassObjects();
            if (hr < 0)
            {
                Marshal.ThrowExceptionForHR(hr);
            }

            return new RegistrationServices(cookie);
        }

        public void Dispose() => Ole32.CoRevokeClassObject(_cookie);
    }

    private static class Ole32
    {
        public const int CLSCTX_LOCAL_SERVER = 0x4;
        public const int REGCLS_MULTIPLEUSE = 1;
        public const int REGCLS_SUSPENDED = 4;

        [DllImport("ole32.dll")]
        public static extern int CoRegisterClassObject(
            [MarshalAs(UnmanagedType.LPStruct)] Guid rclsid,
            [MarshalAs(UnmanagedType.IUnknown)] object pUnk,
            int dwClsContext,
            int flags,
            out uint lpdwRegister);

        [DllImport("ole32.dll")]
        public static extern int CoResumeClassObjects();

        [DllImport("ole32.dll")]
        public static extern int CoRevokeClassObject(uint dwRegister);
    }
}

[ComVisible(true)]
internal sealed class WidgetProviderFactory : IClassFactory
{
    public int CreateInstance(IntPtr pUnkOuter, ref Guid riid, out IntPtr ppvObject)
    {
        ppvObject = IntPtr.Zero;
        if (pUnkOuter != IntPtr.Zero)
        {
            return -2147221232; // CLASS_E_NOAGGREGATION
        }

        var provider = new WidgetProvider();
        ppvObject = Marshal.GetIUnknownForObject(provider);
        return 0;
    }

    public int LockServer(bool fLock) => 0;
}

[ComImport]
[ComVisible(false)]
[Guid("00000001-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IClassFactory
{
    [PreserveSig]
    int CreateInstance(IntPtr pUnkOuter, ref Guid riid, out IntPtr ppvObject);

    [PreserveSig]
    int LockServer(bool fLock);
}
