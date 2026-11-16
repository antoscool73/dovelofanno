using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace DoveLoFanno.Recording;

/// <summary>
/// Blocco della sospensione automatica su Windows finché c'è almeno una registrazione attiva. RegistraTV usava
/// SetThreadExecutionState, che vale per il singolo thread: con il codice asincrono i thread cambiano, quindi qui
/// si usa una richiesta di alimentazione (PowerCreateRequest + PowerRequestSystemRequired), legata a un handle.
/// Lo schermo può spegnersi; non impedisce la sospensione da coperchio o tasto. Verifica: powercfg /requests.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsKeepAwake : IKeepAwake, IDisposable
{
    const int PowerRequestSystemRequired = 1;
    const uint PowerRequestContextVersion = 0;
    const uint PowerRequestContextSimpleString = 0x1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct ReasonContext
    {
        public uint Version;
        public uint Flags;
        [MarshalAs(UnmanagedType.LPWStr)] public string SimpleReasonString;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr PowerCreateRequest(ref ReasonContext context);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool PowerSetRequest(IntPtr handle, int type);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool PowerClearRequest(IntPtr handle, int type);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool CloseHandle(IntPtr handle);

    readonly Lock sync = new();
    IntPtr handle;
    int holders;

    /// <summary>Una registrazione inizia: la prima attiva la richiesta.</summary>
    public void Hold()
    {
        lock (sync)
        {
            if (holders++ > 0) return;
            if (handle == IntPtr.Zero)
            {
                var ctx = new ReasonContext { Version = PowerRequestContextVersion, Flags = PowerRequestContextSimpleString, SimpleReasonString = "DoveLoFanno: registrazione in corso" };
                handle = PowerCreateRequest(ref ctx);
                if (handle == new IntPtr(-1)) handle = IntPtr.Zero;
            }
            if (handle != IntPtr.Zero) PowerSetRequest(handle, PowerRequestSystemRequired);
        }
    }

    /// <summary>Una registrazione finisce: l'ultima toglie la richiesta.</summary>
    public void Release()
    {
        lock (sync)
        {
            if (holders == 0 || --holders > 0) return;
            if (handle != IntPtr.Zero) PowerClearRequest(handle, PowerRequestSystemRequired);
        }
    }

    public void Dispose()
    {
        lock (sync)
        {
            if (handle == IntPtr.Zero) return;
            if (holders > 0) PowerClearRequest(handle, PowerRequestSystemRequired);
            CloseHandle(handle);
            handle = IntPtr.Zero;
            holders = 0;
        }
    }
}
