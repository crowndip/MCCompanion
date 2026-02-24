// Platform/Windows/ShellContextMenu.cs
// Full Windows shell context menu via IContextMenu / IContextMenu2 / IContextMenu3.
// No WinForms dependency – uses a raw Win32 message-only window for owner-draw menus.
// This file is ONLY compiled on Windows (see .csproj ItemGroup condition).

using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace MCCompanion.Platform.Windows;

[SupportedOSPlatform("windows")]
internal static class ShellContextMenu
{
    public static void Show(string path) => ShowAt(path, 200, 200);

    public static void ShowAt(string path, int screenX, int screenY)
    {
        SHParseDisplayName(path, IntPtr.Zero, out IntPtr pidl, 0, out _);
        if (pidl == IntPtr.Zero) return;

        IntPtr pidlParent = ILClone(pidl);
        ILRemoveLastID(pidlParent);

        try
        {
            SHGetDesktopFolder(out IShellFolder desktop);
            IShellFolder parent;
            if (Marshal.ReadInt16(pidlParent) == 0)
            {
                parent = desktop;
            }
            else
            {
                Guid sfGuid = new("000214F2-0000-0000-C000-000000000046");
                desktop.BindToObject(pidlParent, IntPtr.Zero, ref sfGuid, out IntPtr ppv);
                parent = (IShellFolder)Marshal.GetObjectForIUnknown(ppv);
            }

            IntPtr[] children = [ILFindLastID(pidl)];
            Guid icmGuid = new("000214e4-0000-0000-c000-000000000046");
            parent.GetUIObjectOf(IntPtr.Zero, 1, children, ref icmGuid, IntPtr.Zero, out IntPtr pCM);
            if (pCM == IntPtr.Zero) return;

            var ctx  = (IContextMenu)Marshal.GetObjectForIUnknown(pCM);
            IContextMenu2? ctx2 = null;
            IContextMenu3? ctx3 = null;
            try { ctx3 = (IContextMenu3)ctx; } catch { }
            if (ctx3 == null) try { ctx2 = (IContextMenu2)ctx; } catch { }

            IntPtr hMenu = CreatePopupMenu();
            try
            {
                ctx.QueryContextMenu(hMenu, 0, CMD_FIRST, CMD_LAST, CMF_NORMAL | CMF_EXPLORE);

                IntPtr hwnd = CreateMessageWindow(ctx2, ctx3);
                SetForegroundWindow(hwnd);

                uint sel = TrackPopupMenuEx(hMenu, TPM_RETURNCMD | TPM_LEFTBUTTON,
                    screenX, screenY, hwnd, IntPtr.Zero);

                DestroyWindow(hwnd);

                if (sel >= CMD_FIRST)
                {
                    var info = new CMINVOKECOMMANDINFOEX
                    {
                        cbSize  = (uint)Marshal.SizeOf<CMINVOKECOMMANDINFOEX>(),
                        fMask   = CMIC_MASK_UNICODE | CMIC_MASK_ASYNCOK,
                        lpVerb  = new IntPtr(sel - CMD_FIRST),
                        lpVerbW = new IntPtr(sel - CMD_FIRST),
                        nShow   = 1
                    };
                    ctx.InvokeCommand(ref info);
                }
            }
            finally
            {
                DestroyMenu(hMenu);
                Marshal.ReleaseComObject(ctx);
            }
        }
        finally
        {
            ILFree(pidlParent);
            ILFree(pidl);
        }
    }

    // ── Minimal Win32 message window for owner-draw menu messages ─────────────

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate IntPtr WndProcDelegate(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    private static readonly WndProcDelegate _wndProcDelegate = WndProc; // keep GC-alive
    private static IContextMenu2? _ctx2;
    private static IContextMenu3? _ctx3;

    private static IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (_ctx3 != null && msg == WM_MENUCHAR)
        {
            _ctx3.HandleMenuMsg2(msg, wParam, lParam, out IntPtr r);
            return r;
        }
        if (_ctx2 != null && (msg == WM_INITMENUPOPUP || msg == WM_MEASUREITEM || msg == WM_DRAWITEM))
        {
            _ctx2.HandleMenuMsg(msg, wParam, lParam);
            return IntPtr.Zero;
        }
        return DefWindowProc(hwnd, msg, wParam, lParam);
    }

    private static bool _classRegistered;

    private static IntPtr CreateMessageWindow(IContextMenu2? ctx2, IContextMenu3? ctx3)
    {
        _ctx2 = ctx2;
        _ctx3 = ctx3;

        const string cls = "MCCompMenuHost";

        if (!_classRegistered)
        {
            var wc = new WNDCLASS
            {
                lpfnWndProc   = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate),
                hInstance     = GetModuleHandle(null),
                lpszClassName = cls
            };
            RegisterClass(ref wc);
            _classRegistered = true;
        }

        return CreateWindowEx(0, cls, null, 0,
            0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, GetModuleHandle(null), IntPtr.Zero);
    }

    // ── Constants ─────────────────────────────────────────────────────────────

    private const uint WM_DRAWITEM      = 0x002B;
    private const uint WM_MEASUREITEM   = 0x002C;
    private const uint WM_INITMENUPOPUP = 0x0117;
    private const uint WM_MENUCHAR      = 0x0120;
    private const uint CMF_NORMAL       = 0x0000;
    private const uint CMF_EXPLORE      = 0x0001;
    private const uint TPM_RETURNCMD    = 0x0100;
    private const uint TPM_LEFTBUTTON   = 0x0000;
    private const uint CMIC_MASK_UNICODE = 0x4000;
    private const uint CMIC_MASK_ASYNCOK = 0x00100000;
    private const uint CMD_FIRST         = 1;
    private const uint CMD_LAST          = 0x7FFF;

    // ── COM interfaces ────────────────────────────────────────────────────────

    [ComImport, Guid("000214e4-0000-0000-c000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu
    {
        [PreserveSig] int QueryContextMenu(IntPtr hMenu, uint idx, uint first, uint last, uint flags);
        [PreserveSig] int InvokeCommand(ref CMINVOKECOMMANDINFOEX pici);
        [PreserveSig] int GetCommandString(IntPtr id, uint type, IntPtr res, System.Text.StringBuilder name, uint cch);
    }

    [ComImport, Guid("000214f4-0000-0000-c000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu2 : IContextMenu
    {
        [PreserveSig] new int QueryContextMenu(IntPtr hMenu, uint idx, uint first, uint last, uint flags);
        [PreserveSig] new int InvokeCommand(ref CMINVOKECOMMANDINFOEX pici);
        [PreserveSig] new int GetCommandString(IntPtr id, uint type, IntPtr res, System.Text.StringBuilder name, uint cch);
        [PreserveSig] int HandleMenuMsg(uint msg, IntPtr wp, IntPtr lp);
    }

    [ComImport, Guid("bcfce0a0-ec17-11d0-8d10-00a0c90f2719"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu3 : IContextMenu2
    {
        [PreserveSig] new int QueryContextMenu(IntPtr hMenu, uint idx, uint first, uint last, uint flags);
        [PreserveSig] new int InvokeCommand(ref CMINVOKECOMMANDINFOEX pici);
        [PreserveSig] new int GetCommandString(IntPtr id, uint type, IntPtr res, System.Text.StringBuilder name, uint cch);
        [PreserveSig] new int HandleMenuMsg(uint msg, IntPtr wp, IntPtr lp);
        [PreserveSig] int HandleMenuMsg2(uint msg, IntPtr wp, IntPtr lp, out IntPtr result);
    }

    [ComImport, Guid("000214F2-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellFolder
    {
        void ParseDisplayName(IntPtr h, IntPtr pbc, [MarshalAs(UnmanagedType.LPWStr)] string name, out uint eaten, out IntPtr pidl, ref uint attrs);
        void EnumObjects(IntPtr h, uint f, out IntPtr e);
        void BindToObject(IntPtr pidl, IntPtr pbc, ref Guid riid, out IntPtr ppv);
        void BindToStorage(IntPtr pidl, IntPtr pbc, ref Guid riid, out IntPtr ppv);
        void CompareIDs(IntPtr p, IntPtr p1, IntPtr p2);
        void CreateViewObject(IntPtr h, ref Guid riid, out IntPtr ppv);
        void GetAttributesOf(uint c, IntPtr[] pidls, ref uint attrs);
        [PreserveSig] int GetUIObjectOf(IntPtr h, uint c, IntPtr[] pidls, ref Guid riid, IntPtr r, out IntPtr ppv);
        void GetDisplayNameOf(IntPtr pidl, uint f, out IntPtr name);
        void SetNameOf(IntPtr h, IntPtr pidl, [MarshalAs(UnmanagedType.LPWStr)] string name, uint f, out IntPtr pidlOut);
    }

    // ── Structs ───────────────────────────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    private struct CMINVOKECOMMANDINFOEX
    {
        public uint   cbSize;
        public uint   fMask;
        public IntPtr hwnd;
        public IntPtr lpVerb;
        public IntPtr lpParameters;
        public IntPtr lpDirectory;
        public int    nShow;
        public uint   dwHotKey;
        public IntPtr hIcon, lpTitle, lpVerbW, lpParametersW, lpDirectoryW, lpTitleW;
        public int    ptX, ptY;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASS
    {
        public uint   style;
        public IntPtr lpfnWndProc;
        public int    cbClsExtra, cbWndExtra;
        public IntPtr hInstance, hIcon, hCursor, hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)] public string  lpszClassName;
    }

    // ── P/Invoke ──────────────────────────────────────────────────────────────

    [DllImport("shell32.dll")]
    private static extern int SHGetDesktopFolder(out IShellFolder f);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHParseDisplayName(string n, IntPtr pbc, out IntPtr pidl, uint sf, out uint so);

    [DllImport("shell32.dll")] private static extern void   ILFree(IntPtr p);
    [DllImport("shell32.dll")] private static extern IntPtr ILFindLastID(IntPtr p);
    [DllImport("shell32.dll")] private static extern bool   ILRemoveLastID(IntPtr p);
    [DllImport("shell32.dll")] private static extern IntPtr ILClone(IntPtr p);

    [DllImport("user32.dll")] private static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll")] private static extern bool   DestroyMenu(IntPtr h);
    [DllImport("user32.dll")] private static extern uint   TrackPopupMenuEx(IntPtr h, uint f, int x, int y, IntPtr wnd, IntPtr tpm);
    [DllImport("user32.dll")] private static extern bool   SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] private static extern IntPtr DefWindowProc(IntPtr h, uint m, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] private static extern bool   DestroyWindow(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern ushort RegisterClass(ref WNDCLASS wc);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowEx(uint ex, string cls, string? title, uint style,
        int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? n);
}
