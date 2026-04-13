using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

// PeakHUD — single-process multi-monitor taskbar widget.
// Pure Win32 + NativeAOT: no WinForms, no System.Drawing.
// One HWND per enabled monitor (taskbar button + custom icon).
// One shared popup HWND shown on demand.

// ── Global state ─────────────────────────────────────────────────────────────

internal static unsafe class App
{
    public const int MonitorCount = Config.COUNT;

    // Per-monitor state (ring buffer, GDI resources, current reading)
    public static MonitorState[] Monitors = new MonitorState[MonitorCount];

    // Icon window handles — one per enabled monitor (0 if disabled)
    public static nint[] IconHwnds = new nint[MonitorCount];

    // Shared popup window (never destroyed; shown/hidden per click)
    public static nint PopupHwnd;

    // Which monitor the popup is currently showing (-1 = hidden)
    public static int PopupTarget = -1;

    // One CreateCompatibleDC shared across all icon renders (single-threaded)
    public static nint SharedDC;

    // Display names for popup reading label
    public static readonly string[] MonitorNames = ["CPU", "RAM", "Disk", "Net", "GPU"];

    // Timer ID
    public const nuint TimerId = 1;

    // Index of the first enabled monitor (owns the timer)
    public static int TimerOwner = -1;

    // Last tick timestamp per monitor — used to gate per-monitor update rates.
    public static ulong[] LastTickMs = new ulong[MonitorCount];

    // Re-compute the minimum enabled UpdateRateMs and re-arm the shared timer.
    // Call this whenever any monitor's UpdateRateMs changes at runtime.
    public static void RearmTimer()
    {
        if (TimerOwner < 0) return;
        uint interval = uint.MaxValue;
        for (int j = 0; j < MonitorCount; j++)
            if (Config.Monitors[j].Enabled && (uint)Config.Monitors[j].UpdateRateMs < interval)
                interval = (uint)Config.Monitors[j].UpdateRateMs;
        Win32.SetTimer(IconHwnds[TimerOwner], TimerId, interval, 0);
    }
}

// ── Entry point ───────────────────────────────────────────────────────────────

internal static unsafe class Program
{
    [STAThread]
    static int Main()
    {
        Config.Load();

        nint hInstance = Win32.GetModuleHandleW(0);

        // Shared GDI resources used by all icon renders
        App.SharedDC = Win32.CreateCompatibleDC(0);
        Brushes.Init();

        // Initialize monitors
        InitMonitors();

        // Register window classes and create windows
        RegisterIconClass(hInstance);
        Popup.RegisterClass(hInstance);
        App.PopupHwnd = Popup.Create(hInstance);

        // Create an icon HWND for each enabled monitor
        for (int i = 0; i < App.MonitorCount; i++)
        {
            if (!Config.Monitors[i].Enabled) continue;

            App.IconHwnds[i] = CreateIconWindow(hInstance, i);
            Win32.ApplyWin11Theme(App.IconHwnds[i]);
            // Unique AppUserModelID per window — prevents Windows 11 from grouping
            // all monitors into one taskbar slot. Must be set before ShowWindow.
            Win32.SetWindowAppId(App.IconHwnds[i], "PeakHUD." + App.MonitorNames[i]);
            Win32.ShowWindow(App.IconHwnds[i], Win32.SW_SHOWMINIMIZED);

            // First enabled monitor owns the timer
            if (App.TimerOwner < 0)
            {
                App.TimerOwner = i;
                uint interval = uint.MaxValue;
                for (int j = 0; j < App.MonitorCount; j++)
                    if (Config.Monitors[j].Enabled && (uint)Config.Monitors[j].UpdateRateMs < interval)
                        interval = (uint)Config.Monitors[j].UpdateRateMs;
                Win32.SetTimer(App.IconHwnds[i], App.TimerId, interval, 0);
            }

            // Push initial icon immediately (handle not valid yet — done in WM_CREATE)
        }

        // Message loop
        Win32.MSG msg;
        while (Win32.GetMessageW(out msg, 0, 0, 0) > 0)
        {
            Win32.TranslateMessage(msg);
            Win32.DispatchMessageW(msg);
        }

        // Cleanup
        Cleanup();
        return (int)msg.wParam;
    }

    // ── Monitor initialization ────────────────────────────────────────────────

    private static void InitMonitors()
    {
        MonitorRenderer.Init(ref App.Monitors[Config.CPU],     Config.CPU,     "CPU");
        MonitorRenderer.Init(ref App.Monitors[Config.RAM],     Config.RAM,     "RAM");
        MonitorRenderer.Init(ref App.Monitors[Config.DISK],    Config.DISK,    "DSK");
        MonitorRenderer.Init(ref App.Monitors[Config.NETWORK], Config.NETWORK, "NET");
        MonitorRenderer.Init(ref App.Monitors[Config.GPU],     Config.GPU,     "GPU");

        CpuMonitor.Init();
        RamMonitor.Init();
        DiskMonitor.Init();
        NetworkMonitor.Init();
        GpuMonitor.Init();
    }

    // ── Icon window class ─────────────────────────────────────────────────────

    private static void RegisterIconClass(nint hInstance)
    {
        fixed (char* className = "PeakHUDIcon")
        {
            var wc = new Win32.WNDCLASSEX
            {
                cbSize        = (uint)sizeof(Win32.WNDCLASSEX),
                style         = Win32.CS_HREDRAW | Win32.CS_VREDRAW,
                lpfnWndProc   = &IconWndProc,
                hInstance     = hInstance,
                hCursor       = Win32.LoadCursorW(0, Win32.IDC_ARROW),
                hbrBackground = (nint)(Win32.COLOR_WINDOW + 1),
                lpszClassName = className
            };
            Win32.RegisterClassExW(&wc);
        }
    }

    private static nint CreateIconWindow(nint hInstance, int monitorIndex)
    {
        nint hwnd = Win32.CreateWindowExW(
            Win32.WS_EX_APPWINDOW,
            "PeakHUDIcon",
            App.MonitorNames[monitorIndex],
            Win32.WS_OVERLAPPEDWINDOW | Win32.WS_MINIMIZE | Win32.WS_SYSMENU | Win32.WS_MINIMIZEBOX,
            0, 0, 300, 200,
            0, 0, hInstance, 0);

        // Store monitor index in USERDATA for retrieval in WndProc
        Win32.SetWindowLongPtrW(hwnd, Win32.GWLP_USERDATA, (nint)monitorIndex);
        // ShowWindow is intentionally deferred — caller sets AppUserModelID first.
        return hwnd;
    }

    // ── Icon WndProc (shared by all icon windows) ─────────────────────────────

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    static nint IconWndProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        switch (msg)
        {
            case Win32.WM_TIMER:
                if ((nuint)wParam == App.TimerId)
                    OnTick();
                break;

            case Win32.WM_SYSCOMMAND:
                // User clicked the minimized taskbar button → show popup, don't restore
                if ((uint)(wParam & 0xFFF0) == Win32.SC_RESTORE)
                {
                    int idx = (int)Win32.GetWindowLongPtrW(hwnd, Win32.GWLP_USERDATA);
                    Popup.Show(App.PopupHwnd, idx);
                    return 0;  // suppress default restore
                }
                break;

            case Win32.WM_CLOSE:
                // Closing one icon window exits the whole app
                Win32.PostQuitMessage(0);
                return 0;

            case Win32.WM_DESTROY:
                Win32.PostQuitMessage(0);
                break;
        }

        return Win32.DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    // ── Tick: read all monitors → render icons → update popup ─────────────────

    private static void OnTick()
    {
        ulong now = Win32.GetTickCount64();

        for (int i = 0; i < App.MonitorCount; i++)
        {
            if (!Config.Monitors[i].Enabled) continue;

            // Per-monitor rate gating — all monitors share one timer fired at the
            // fastest enabled rate; each monitor only updates when its own interval
            // has elapsed.
            if (now - App.LastTickMs[i] < (ulong)Config.Monitors[i].UpdateRateMs)
                continue;
            App.LastTickMs[i] = now;

            float value = i switch
            {
                Config.CPU     => CpuMonitor.Read(),
                Config.RAM     => RamMonitor.Read(),
                Config.DISK    => DiskMonitor.Read(),
                Config.NETWORK => NetworkMonitor.Read(),
                Config.GPU     => GpuMonitor.Read(),
                _              => 0f
            };

            MonitorRenderer.Push(ref App.Monitors[i], value);
            if (i == Config.GPU)     MonitorRenderer.Push2(ref App.Monitors[i], GpuMonitor.ReadMemory());
            if (i == Config.DISK)    MonitorRenderer.Push2(ref App.Monitors[i], DiskMonitor.ReadSecondary());
            if (i == Config.NETWORK) MonitorRenderer.Push2(ref App.Monitors[i], NetworkMonitor.ReadSecondary());
            MonitorRenderer.RenderAndPush(ref App.Monitors[i], App.IconHwnds[i], App.SharedDC);
        }

        // Repaint popup chart if it's visible
        if (App.PopupTarget >= 0 && App.PopupHwnd != 0)
            Win32.InvalidateRect(App.PopupHwnd, 0, false);
    }

    // ── Cleanup ───────────────────────────────────────────────────────────────

    private static void Cleanup()
    {
        for (int i = 0; i < App.MonitorCount; i++)
            MonitorRenderer.Dispose(ref App.Monitors[i]);

        DiskMonitor.Dispose();

        if (App.SharedDC != 0)
        {
            Win32.DeleteDC(App.SharedDC);
            App.SharedDC = 0;
        }

        // GPU slot in ByMonitor aliases GpuUtil — skip it to avoid a double-free.
        for (int i = 0; i < Brushes.ByMonitor.Length; i++)
            if (i != Config.GPU && Brushes.ByMonitor[i] != 0)
                Win32.DeleteObject(Brushes.ByMonitor[i]);
        if (Brushes.GpuUtil   != 0) Win32.DeleteObject(Brushes.GpuUtil);
        if (Brushes.GpuMem    != 0) Win32.DeleteObject(Brushes.GpuMem);
        if (Brushes.GpuBlend  != 0) Win32.DeleteObject(Brushes.GpuBlend);
        if (Brushes.DiskWrite != 0) Win32.DeleteObject(Brushes.DiskWrite);
        if (Brushes.DiskBlend != 0) Win32.DeleteObject(Brushes.DiskBlend);
        if (Brushes.NetSend   != 0) Win32.DeleteObject(Brushes.NetSend);
        if (Brushes.NetBlend  != 0) Win32.DeleteObject(Brushes.NetBlend);
        if (Brushes.TabBg     != 0) Win32.DeleteObject(Brushes.TabBg);
        if (Brushes.TabActive != 0) Win32.DeleteObject(Brushes.TabActive);
        if (Brushes.Accent    != 0) Win32.DeleteObject(Brushes.Accent);
        if (Brushes.Bg        != 0) Win32.DeleteObject(Brushes.Bg);
        if (Brushes.Font      != 0) Win32.DeleteObject(Brushes.Font);
    }
}
