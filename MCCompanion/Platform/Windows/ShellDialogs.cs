// Platform/Windows/ShellDialogs.cs
// Native Windows Properties and Open-With dialogs.
// ONLY compiled on Windows.

using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace MCCompanion.Platform.Windows;

[SupportedOSPlatform("windows")]
internal static class ShellDialogs
{
    // ── Properties ────────────────────────────────────────────────────────────

    public static void ShowProperties(string path)
    {
        var info = new SHELLEXECUTEINFO
        {
            cbSize  = Marshal.SizeOf<SHELLEXECUTEINFO>(),
            fMask   = SEE_MASK_INVOKEIDLIST,
            lpVerb  = "properties",
            lpFile  = path,
            nShow   = 1
        };
        ShellExecuteEx(ref info);
    }

    // ── Open With ─────────────────────────────────────────────────────────────

    public static void ShowOpenWith(string path)
    {
        var oai = new OPENASINFO
        {
            pcszFile    = path,
            oaifInFlags = OAIF_ALLOW_REGISTRATION | OAIF_REGISTER_EXT | OAIF_EXEC
        };
        SHOpenWithDialog(IntPtr.Zero, ref oai);
    }

    // ── Structs & imports ──────────────────────────────────────────────────────

    private const uint SEE_MASK_INVOKEIDLIST    = 0x0C;
    private const uint OAIF_ALLOW_REGISTRATION = 0x01;
    private const uint OAIF_REGISTER_EXT       = 0x02;
    private const uint OAIF_EXEC               = 0x04;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHELLEXECUTEINFO
    {
        public int    cbSize;
        public uint   fMask;
        public IntPtr hwnd;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpVerb;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpFile;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpParameters;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpDirectory;
        public int    nShow;
        public IntPtr hInstApp, lpIDList;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpClass;
        public IntPtr hkeyClass;
        public uint   dwHotKey;
        public IntPtr hIcon, hProcess;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OPENASINFO
    {
        [MarshalAs(UnmanagedType.LPWStr)] public string  pcszFile;
        [MarshalAs(UnmanagedType.LPWStr)] public string? pcszClass;
        public uint oaifInFlags;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool ShellExecuteEx(ref SHELLEXECUTEINFO info);

    [DllImport("shell32.dll", EntryPoint = "SHOpenWithDialog", CharSet = CharSet.Unicode)]
    private static extern int SHOpenWithDialog(IntPtr hwnd, ref OPENASINFO oai);
}
