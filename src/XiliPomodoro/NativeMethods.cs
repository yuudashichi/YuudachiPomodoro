using System.Runtime.InteropServices;
namespace XiliPomodoro;

internal sealed class NativeMethods : IDisposable
{
    const uint CallbackMessage = 0x8001;
    static readonly uint WakeMessage = RegisterWindowMessage("XiliPomodoro.Pomodoro.Wake");
    readonly uint taskbarCreated = RegisterWindowMessage("TaskbarCreated");
    readonly nint hwnd;
    readonly SubclassProc proc;
    nint icon;
    public event Action? ShowRequested;
    public event Action? ExitRequested;
    public event Action? SuspendRequested;
    public event Action? SessionEnding;
    public NativeMethods(nint handle)
    {
        hwnd = handle; proc = WndProc;
        SetWindowSubclass(hwnd, proc, 1, 0);
        icon = LoadImage(0, Path.Combine(AppContext.BaseDirectory, "Assets", "XiliPomodoro.ico"), 1, 32, 32, 0x10);
        SendMessage(hwnd, 0x80, 0, icon); SendMessage(hwnd, 0x80, 1, icon);
        AddTray();
    }
    NOTIFYICONDATA TrayData() => new() { cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(), hWnd = hwnd, uID = 1, uFlags = 1 | 2 | 4, uCallbackMessage = CallbackMessage, hIcon = icon, szTip = "惜立番茄钟", szInfo = "", szInfoTitle = "" };
    void AddTray() { var d = TrayData(); Shell_NotifyIcon(0, ref d); }
    public void SetTrayText(string text) { var d = TrayData(); d.szTip = text.Length > 127 ? text[..127] : text; Shell_NotifyIcon(1, ref d); }
    nint WndProc(nint h, uint msg, nuint w, nint l, nuint id, nuint data)
    {
        if (msg == WakeMessage) { ShowRequested?.Invoke(); return 0; }
        if (msg == taskbarCreated) AddTray();
        if (msg == 0x218 && w == 4) SuspendRequested?.Invoke();
        if (msg == 0x11) { SessionEnding?.Invoke(); return 1; }
        if (msg == 0x24)
        {
            var m = Marshal.PtrToStructure<MINMAXINFO>(l); var dpi = GetDpiForWindow(h) / 96.0;
            m.minTrack = new POINT { x = (int)(520 * dpi), y = (int)(500 * dpi) }; Marshal.StructureToPtr(m, l, false);
        }
        if (msg == CallbackMessage)
        {
            var code = (uint)((long)l & 0xFFFF);
            if (code is 0x202 or 0x405) ShowRequested?.Invoke();
            if (code == 0x205)
            {
                var menu = CreatePopupMenu(); AppendMenu(menu, 0, 1, "打开惜立番茄钟"); AppendMenu(menu, 0x800, 0, ""); AppendMenu(menu, 0, 2, "退出");
                GetCursorPos(out var point); SetForegroundWindow(hwnd);
                var selected = TrackPopupMenu(menu, 0x100 | 0x2, point.x, point.y, 0, hwnd, 0); DestroyMenu(menu);
                if (selected == 1) ShowRequested?.Invoke(); if (selected == 2) ExitRequested?.Invoke();
            }
        }
        return DefSubclassProc(h, msg, w, l);
    }
    public void BringForward()
    {
        ShowWindow(hwnd, IsMinimized ? 9 : 5);
        if (!SetForegroundWindow(hwnd)) { var f = new FLASHWINFO { cbSize = (uint)Marshal.SizeOf<FLASHWINFO>(), hwnd = hwnd, dwFlags = 3, uCount = 3 }; FlashWindowEx(ref f); }
    }
    public void Hide() => ShowWindow(hwnd, 0);
    public bool IsMinimized => IsIconic(hwnd);
    public static void WakeExisting() { var h = FindWindow(null, "惜立番茄钟"); if (h != 0) PostMessage(h, WakeMessage, 0, 0); }
    public void Dispose() { var d = TrayData(); Shell_NotifyIcon(2, ref d); RemoveWindowSubclass(hwnd, proc, 1); if (icon != 0) DestroyIcon(icon); }
    [StructLayout(LayoutKind.Sequential)] struct POINT { public int x, y; }
    [StructLayout(LayoutKind.Sequential)] struct MINMAXINFO { public POINT reserved, maxSize, maxPosition, minTrack, maxTrack; }
    [StructLayout(LayoutKind.Sequential)] struct FLASHWINFO { public uint cbSize; public nint hwnd; public uint dwFlags, uCount, dwTimeout; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] struct NOTIFYICONDATA
    {
        public uint cbSize; public nint hWnd; public uint uID, uFlags, uCallbackMessage; public nint hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState, dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags; public Guid guidItem; public nint hBalloonIcon;
    }
    delegate nint SubclassProc(nint h, uint msg, nuint w, nint l, nuint id, nuint data);
    [DllImport("comctl32.dll")] static extern bool SetWindowSubclass(nint h, SubclassProc proc, nuint id, nuint data);
    [DllImport("comctl32.dll")] static extern bool RemoveWindowSubclass(nint h, SubclassProc proc, nuint id);
    [DllImport("comctl32.dll")] static extern nint DefSubclassProc(nint h, uint msg, nuint w, nint l);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern bool Shell_NotifyIcon(uint message, ref NOTIFYICONDATA data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern uint RegisterWindowMessage(string message);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern nint FindWindow(string? cls, string title);
    [DllImport("user32.dll")] static extern bool PostMessage(nint h, uint msg, nuint w, nint l);
    [DllImport("user32.dll")] static extern nint SendMessage(nint h, uint msg, nuint w, nint l);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(nint h);
    [DllImport("user32.dll")] static extern bool ShowWindow(nint h, int command);
    [DllImport("user32.dll")] static extern bool IsIconic(nint h);
    [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(nint h);
    [DllImport("user32.dll")] static extern bool FlashWindowEx(ref FLASHWINFO info);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT point);
    [DllImport("user32.dll")] static extern nint CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool AppendMenu(nint menu, uint flags, nuint id, string text);
    [DllImport("user32.dll")] static extern int TrackPopupMenu(nint menu, uint flags, int x, int y, int reserved, nint h, nint rect);
    [DllImport("user32.dll")] static extern bool DestroyMenu(nint menu);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern nint LoadImage(nint instance, string name, uint type, int cx, int cy, uint flags);
    [DllImport("user32.dll")] static extern bool DestroyIcon(nint icon);
}
