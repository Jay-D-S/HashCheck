using System.Runtime.InteropServices;

namespace HashCheck.Tray;

public sealed class TrayIconHost : IDisposable
{
    private const uint WM_APP_TRAY = 0x8000 + 1;
    private const uint WM_COMMAND = 0x0111;
    private const uint WM_DESTROY = 0x0002;
    private const uint NIM_ADD = 0;
    private const uint NIM_DELETE = 2;
    private const uint NIM_SETVERSION = 4;
    private const uint NIF_MESSAGE = 0x01;
    private const uint NIF_ICON = 0x02;
    private const uint NIF_TIP = 0x04;
    private const uint NOTIFYICON_VERSION_4 = 4;
    private const int IDM_DASHBOARD = 1001;
    private const int IDM_CREATE    = 1002;
    private const int IDM_SETTINGS  = 1004;
    private const int IDM_EXIT      = 1005;
    private const int IDM_ABOUT     = 1006;
    private const uint WM_RBUTTONUP = 0x0205;
    private const uint WM_LBUTTONDBLCLK = 0x0203;
    private const uint WM_LBUTTONUP = 0x0202;
    private const uint WM_CONTEXTMENU = 0x007B;
    private const uint TPM_RETURNCMD  = 0x0100;
    private const uint TPM_RIGHTBUTTON = 0x0002;
    private const uint MF_STRING = 0x0;
    private const uint MF_SEPARATOR = 0x0800;

    private readonly nint _hwnd;
    private NOTIFYICONDATAW _nid;
    private bool _disposed;

    // Delegate must be kept alive to prevent GC
    private readonly SUBCLASSPROC _subclassDelegate;

    public event Action? ShowDashboardRequested;
    public event Action? CreateHashRequested;
    public event Action? SettingsRequested;
    public event Action? AboutRequested;
    public event Action? ExitRequested;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATAW
    {
        public uint cbSize;
        public nint hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public nint hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public uint uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public nint hBalloonIcon;
    }

    private delegate nint SUBCLASSPROC(nint hwnd, uint msg, nint wParam, nint lParam, nuint uIdSubclass, nuint dwRefData);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Shell_NotifyIconW(uint dwMessage, ref NOTIFYICONDATAW lpData);

    [DllImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowSubclass(nint hwnd, SUBCLASSPROC pfnSubclass, nuint uIdSubclass, nuint dwRefData);

    [DllImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveWindowSubclass(nint hwnd, SUBCLASSPROC pfnSubclass, nuint uIdSubclass);

    [DllImport("comctl32.dll")]
    private static extern nint DefSubclassProc(nint hwnd, uint uMsg, nint wParam, nint lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint LoadImage(nint hInst, string lpszName, uint uType,
        int cxDesired, int cyDesired, uint fuLoad);

    [DllImport("user32.dll")]
    private static extern nint LoadIcon(nint hInstance, nint lpIconName);

    private const uint  IMAGE_ICON      = 1;
    private const uint  LR_LOADFROMFILE = 0x0010;
    private const uint  LR_DEFAULTSIZE  = 0x0040;
    private const nint  IDI_APPLICATION = 32512;

    [DllImport("user32.dll")]
    private static extern nint CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AppendMenuW(nint hMenu, uint uFlags, nuint uIDNewItem, string? lpNewItem);

    [DllImport("user32.dll")]
    private static extern uint TrackPopupMenu(nint hMenu, uint uFlags, int x, int y, int nReserved, nint hwnd, nint prcRect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyMenu(nint hMenu);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    private static nint LoadAppIcon()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "HashIcon.ico");
        if (File.Exists(path))
            return LoadImage(nint.Zero, path, IMAGE_ICON, 0, 0, LR_LOADFROMFILE | LR_DEFAULTSIZE);

        return LoadIcon(nint.Zero, IDI_APPLICATION);
    }

    public TrayIconHost(nint hwnd)
    {
        _hwnd = hwnd;
        _subclassDelegate = SubclassProc;

        SetWindowSubclass(hwnd, _subclassDelegate, 1, 0);

        _nid = new NOTIFYICONDATAW
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>(),
            hWnd = hwnd,
            uID = 1,
            uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
            uCallbackMessage = WM_APP_TRAY,
            hIcon = LoadAppIcon(),
            szTip = "HashCheck",
            uVersion = NOTIFYICON_VERSION_4
        };

        Shell_NotifyIconW(NIM_ADD, ref _nid);
        Shell_NotifyIconW(NIM_SETVERSION, ref _nid);
    }

    private nint SubclassProc(nint hwnd, uint msg, nint wParam, nint lParam, nuint uIdSubclass, nuint dwRefData)
    {
        if (msg == WM_APP_TRAY)
        {
            var notifyMsg = (uint)(lParam & 0xFFFF);

            if (notifyMsg == WM_LBUTTONDBLCLK || notifyMsg == WM_LBUTTONUP)
            {
                ShowDashboardRequested?.Invoke();
            }
            else if (notifyMsg == WM_RBUTTONUP || notifyMsg == WM_CONTEXTMENU)
            {
                ShowContextMenu();
            }
        }
        else if (msg == WM_COMMAND)
        {
            HandleMenuCommand((int)(wParam & 0xFFFF));
        }

        return DefSubclassProc(hwnd, msg, wParam, lParam);
    }

    private void ShowContextMenu()
    {
        SetForegroundWindow(_hwnd);
        GetCursorPos(out var pt);

        var menu = CreatePopupMenu();
        AppendMenuW(menu, MF_STRING,    IDM_DASHBOARD, "Dashboard");
        AppendMenuW(menu, MF_STRING,    IDM_CREATE,    "Create new hash…");
        AppendMenuW(menu, MF_SEPARATOR, 0,             null);
        AppendMenuW(menu, MF_STRING,    IDM_SETTINGS,  "Settings");
        AppendMenuW(menu, MF_STRING,    IDM_ABOUT,     "About / Donate…");
        AppendMenuW(menu, MF_SEPARATOR, 0,             null);
        AppendMenuW(menu, MF_STRING,    IDM_EXIT,      "Exit");

        // TPM_RETURNCMD: TrackPopupMenu returns the selected item ID directly
        // instead of posting WM_COMMAND — handle it here rather than in SubclassProc.
        var cmd = (int)TrackPopupMenu(menu, TPM_RETURNCMD | TPM_RIGHTBUTTON, pt.X, pt.Y, 0, _hwnd, nint.Zero);
        DestroyMenu(menu);
        if (cmd != 0)
            HandleMenuCommand(cmd);
    }

    private void HandleMenuCommand(int id)
    {
        switch (id)
        {
            case IDM_DASHBOARD: ShowDashboardRequested?.Invoke(); break;
            case IDM_CREATE:    CreateHashRequested?.Invoke();    break;
            case IDM_SETTINGS:  SettingsRequested?.Invoke();      break;
            case IDM_ABOUT:     AboutRequested?.Invoke();         break;
            case IDM_EXIT:      ExitRequested?.Invoke();          break;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Shell_NotifyIconW(NIM_DELETE, ref _nid);
        RemoveWindowSubclass(_hwnd, _subclassDelegate, 1);
    }
}
