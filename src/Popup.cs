using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

// Popup window — a single shared HWND shown/hidden on demand when the user clicks
// any monitor's taskbar button. Never destroyed during the session.
//
// Layout (360 × 280):
//   [0..31]   Tab strip:  "Monitor" | "Settings"
//   [32..279] Content:    Monitor tab -or- Settings tab (child controls)

internal static unsafe class Popup
{
    // Window dimensions
    public  const int Width    = 360;
    public  const int Height   = 280;
    private const int TabH     = 32;
    private const int TabW     = 90;
    private const int Padding  = 12;

    // Tab indices
    private const int TAB_MONITOR  = 0;
    private const int TAB_SETTINGS = 1;

    private static int  _activeTab = TAB_MONITOR;

    // Child control HWNDs (Settings tab)
    private static nint _hEditRate;
    private static nint _hCheckLabel;
    private static nint _hLabelRate;    // "Update Rate" static label
    private static nint _hLabelMs;     // "ms" static label
    private static nint _hLabelIcon;   // "Icon Label" static label

    // Label child for the reading text (Monitor tab)
    private static nint _hReadingLabel;

    // Registered class atom
    private static bool _classRegistered;

    // ── Registration ─────────────────────────────────────────────────────────

    public static unsafe void RegisterClass(nint hInstance)
    {
        if (_classRegistered) return;
        _classRegistered = true;

        fixed (char* className = "PeakHUDPopup")
        {
            var wc = new Win32.WNDCLASSEX
            {
                cbSize        = (uint)sizeof(Win32.WNDCLASSEX),
                style         = 0,
                lpfnWndProc   = &PopupWndProc,
                hInstance     = hInstance,
                hbrBackground = (nint)(Win32.COLOR_WINDOW + 1),
                lpszClassName = className
            };
            Win32.RegisterClassExW(&wc);
        }
    }

    // ── Creation ─────────────────────────────────────────────────────────────

    public static nint Create(nint hInstance)
    {
        RegisterClass(hInstance);

        nint hwnd = Win32.CreateWindowExW(
            Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_TOPMOST,
            "PeakHUDPopup", null,
            Win32.WS_POPUP | Win32.WS_BORDER,
            0, 0, Width, Height,
            0, 0, hInstance, 0);

        Win32.ApplyWin11Theme(hwnd);
        CreateChildControls(hwnd, hInstance);
        Win32.ShowWindow(hwnd, Win32.SW_HIDE);
        return hwnd;
    }

    // ── Show / Hide ──────────────────────────────────────────────────────────

    // Called from icon WndProc when user clicks the minimized taskbar button.
    public static void Show(nint popupHwnd, int monitorIndex)
    {
        App.PopupTarget = monitorIndex;

        // Position near cursor, clamped to screen
        Win32.GetCursorPos(out var pt);
        int sx = Win32.GetSystemMetrics(Win32.SM_CXSCREEN);
        int sy = Win32.GetSystemMetrics(Win32.SM_CYSCREEN);

        int x = Math.Clamp(pt.X - Width / 2, 0, sx - Width);
        int y = Math.Clamp(pt.Y - Height - 8, 0, sy - Height);

        // Update reading label before showing to avoid flash of stale data
        UpdateReadingLabel(monitorIndex);
        ActivateTab(popupHwnd, TAB_MONITOR);
        SyncSettingsControls(monitorIndex);

        Win32.SetWindowPos(popupHwnd, -1 /*HWND_TOPMOST*/,
            x, y, Width, Height, Win32.SWP_SHOWWINDOW);
        Win32.SetForegroundWindow(popupHwnd);
        Win32.InvalidateRect(popupHwnd, 0, false);
    }

    // ── Child controls ───────────────────────────────────────────────────────

    private static void CreateChildControls(nint hwnd, nint hInstance)
    {
        int contentY = TabH;
        nint hFont = Brushes.Font; // shared HFONT (Trebuchet MS / Segoe UI fallback)

        // ── Monitor tab: reading label (STATIC child, centered)
        _hReadingLabel = Win32.CreateWindowExW(0, "STATIC", "---",
            Win32.WS_CHILD | Win32.WS_VISIBLE | Win32.SS_CENTER,
            0, contentY, Width, 36,
            hwnd, 0, hInstance, 0);
        Win32.SendMessageW(_hReadingLabel, Win32.WM_SETFONT, hFont, 1);

        // ── Settings tab controls (hidden initially) ──────────────────────────
        int sy = contentY + 36;

        // Section label — "Update Rate" (plain STATIC)
        _hLabelRate = Win32.CreateWindowExW(0, "STATIC", "Update Rate",
            Win32.WS_CHILD,   // hidden initially
            Padding, sy, 200, 20,
            hwnd, (nint)1001, hInstance, 0);
        Win32.SendMessageW(_hLabelRate, Win32.WM_SETFONT, hFont, 1);
        sy += 22;

        // EDIT (rate value) — plain edit box, no spinner
        _hEditRate = Win32.CreateWindowExW(Win32.WS_EX_CLIENTEDGE, "EDIT", "1000",
            Win32.WS_CHILD | Win32.ES_NUMBER,
            Padding, sy, 80, 24,
            hwnd, (nint)1002, hInstance, 0);
        Win32.SendMessageW(_hEditRate, Win32.WM_SETFONT, hFont, 1);
        sy += 30;

        // ms label
        _hLabelMs = Win32.CreateWindowExW(0, "STATIC", "ms",
            Win32.WS_CHILD,
            Padding + 86, sy - 28, 30, 24,
            hwnd, (nint)1004, hInstance, 0);
        Win32.SendMessageW(_hLabelMs, Win32.WM_SETFONT, hFont, 1);

        sy += 12;

        // Section label — "Icon Label"
        _hLabelIcon = Win32.CreateWindowExW(0, "STATIC", "Icon Label",
            Win32.WS_CHILD,
            Padding, sy, 200, 20,
            hwnd, (nint)1005, hInstance, 0);
        Win32.SendMessageW(_hLabelIcon, Win32.WM_SETFONT, hFont, 1);
        sy += 22;

        // Checkbox
        _hCheckLabel = Win32.CreateWindowExW(0, "BUTTON", "Show label on icon",
            Win32.WS_CHILD | Win32.BS_AUTOCHECKBOX,
            Padding, sy, 240, 24,
            hwnd, (nint)1006, hInstance, 0);
        Win32.SendMessageW(_hCheckLabel, Win32.WM_SETFONT, hFont, 1);
    }

    // ── Tab management ───────────────────────────────────────────────────────

    private static void ActivateTab(nint hwnd, int tab)
    {
        _activeTab = tab;

        // Show/hide reading label (Monitor tab)
        Win32.ShowWindow(_hReadingLabel, tab == TAB_MONITOR ? Win32.SW_SHOW : Win32.SW_HIDE);

        // Show/hide all settings controls
        int settingsVis = tab == TAB_SETTINGS ? Win32.SW_SHOW : Win32.SW_HIDE;
        Win32.ShowWindow(_hLabelRate,  settingsVis);
        Win32.ShowWindow(_hEditRate,   settingsVis);
        Win32.ShowWindow(_hLabelMs,    settingsVis);
        Win32.ShowWindow(_hLabelIcon,  settingsVis);
        Win32.ShowWindow(_hCheckLabel, settingsVis);

        Win32.InvalidateRect(hwnd, 0, true);
    }

    // ── Settings sync ─────────────────────────────────────────────────────────

    private static void SyncSettingsControls(int monitorIndex)
    {
        ref var cfg = ref Config.Monitors[monitorIndex];
        Win32.SetWindowTextW(_hEditRate, cfg.UpdateRateMs.ToString());
        // Set checkbox state: BM_SETCHECK = 0x00F1
        Win32.SendMessageW(_hCheckLabel, 0x00F1, cfg.ShowLabel ? 1 : 0, 0);
    }

    private static void UpdateReadingLabel(int monitorIndex)
    {
        ref var m = ref App.Monitors[monitorIndex];
        string text = $"{App.MonitorNames[monitorIndex]}: {m.Current:F1}%";
        Win32.SetWindowTextW(_hReadingLabel, text);
    }

    // ── WndProc ───────────────────────────────────────────────────────────────

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    public static nint PopupWndProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        switch (msg)
        {
            case Win32.WM_ACTIVATE:
                if (Win32.LOWORD(wParam) == Win32.WA_INACTIVE)
                    Win32.ShowWindow(hwnd, Win32.SW_HIDE);
                break;

            case Win32.WM_PAINT:
                Paint(hwnd);
                return 0;

            case Win32.WM_LBUTTONDOWN:
            {
                int mx = Win32.LOWORD(lParam);
                int my = Win32.HIWORD(lParam);
                HandleTabClick(hwnd, mx, my);
                break;
            }

            case Win32.WM_COMMAND:
            {
                int notif = Win32.HIWORD(wParam);
                nint ctrl = lParam;
                int  idx  = App.PopupTarget;
                if (idx < 0) break;

                if (ctrl == _hEditRate && notif == Win32.EN_CHANGE)
                {
                    // Read the edit text and parse the integer
                    char* buf = stackalloc char[8];
                    int len = Win32.GetWindowTextW(_hEditRate, buf, 8);
                    if (len > 0 && int.TryParse(new ReadOnlySpan<char>(buf, len), out int rate)
                        && rate >= 100 && rate <= 10_000)
                    {
                        Config.Monitors[idx].UpdateRateMs = rate;
                        Config.Save();
                    }
                }
                else if (ctrl == _hCheckLabel && notif == Win32.BN_CLICKED)
                {
                    // BST_CHECKED = 1
                    nint state = Win32.SendMessageW(_hCheckLabel, 0x00F0, 0, 0); // BM_GETCHECK
                    Config.Monitors[idx].ShowLabel = state == 1;
                    Config.Save();
                }
                break;
            }

            case Win32.WM_CLOSE:
                Win32.ShowWindow(hwnd, Win32.SW_HIDE);
                return 0;
        }

        return Win32.DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    // ── Painting ─────────────────────────────────────────────────────────────

    private static void Paint(nint hwnd)
    {
        nint hdc = Win32.BeginPaint(hwnd, out var ps);

        // ── Tab strip ────────────────────────────────────────────────────────
        PaintTabs(hdc);

        // ── Content area separator ────────────────────────────────────────────
        var sepRect = new Win32.RECT(0, TabH - 1, Width, TabH);
        Win32.FillRect(hdc, sepRect, Win32.GetSysColorBrush(15)); // COLOR_BTNFACE

        // ── Monitor tab: bar chart ────────────────────────────────────────────
        if (_activeTab == TAB_MONITOR && App.PopupTarget >= 0)
        {
            PaintChart(hdc, App.PopupTarget);
            // Update the reading label text (it's a STATIC child — repaints itself)
            UpdateReadingLabel(App.PopupTarget);
        }

        Win32.EndPaint(hwnd, ps);
    }

    private static void PaintTabs(nint hdc)
    {
        string[] names = ["Monitor", "Settings"];
        for (int i = 0; i < 2; i++)
        {
            bool active = i == _activeTab;
            var tabRect = new Win32.RECT(i * TabW, 0, (i + 1) * TabW, TabH);

            Win32.FillRect(hdc, tabRect, active ? Brushes.TabActive : Brushes.TabBg);

            Win32.SetBkMode(hdc, Win32.TRANSPARENT);
            Win32.SetTextColor(hdc, Win32.GetSysColor(Win32.COLOR_WINDOWTEXT));
            Win32.DrawTextW(hdc, names[i], -1, ref tabRect,
                Win32.DT_CENTER | Win32.DT_VCENTER | Win32.DT_SINGLELINE);

            // 2px accent underline on active tab
            if (active)
            {
                var underline = new Win32.RECT(i * TabW, TabH - 2, (i + 1) * TabW, TabH);
                Win32.FillRect(hdc, underline, Brushes.Accent);
            }
        }
    }

    private static void PaintChart(nint hdc, int monitorIndex)
    {
        ref var m = ref App.Monitors[monitorIndex];

        int chartTop  = TabH + 40; // below reading label
        int chartBot  = Height;
        int chartH    = chartBot - chartTop;
        int chartW    = Width;

        // Clear chart area
        var chartRect = new Win32.RECT(0, chartTop, chartW, chartBot);
        Win32.FillRect(hdc, chartRect, Brushes.Bg);

        float barW = chartW / (float)MonitorState.HistoryLen;
        for (int i = 0; i < MonitorState.HistoryLen; i++)
        {
            float val  = MonitorRenderer.HistoryAt(ref m, i);
            float barH = chartH * (val / 100f);
            if (barH < 1f) barH = 1f;

            int bx = (int)(i * barW) + 1;
            int bw = (int)((i + 1) * barW) - bx - 1;
            int by = chartBot - (int)barH;
            Win32.FillRect(hdc,
                new Win32.RECT(bx, by, bx + bw, chartBot),
                Brushes.ForValue(val));
        }
    }

    // ── Tab click handling ────────────────────────────────────────────────────

    private static void HandleTabClick(nint hwnd, int x, int y)
    {
        if (y > TabH) return;
        int tab = x / TabW;
        if (tab is 0 or 1) ActivateTab(hwnd, tab);
    }
}
