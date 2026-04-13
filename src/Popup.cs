using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using PeakHUD.Monitors;

// Popup window — a single shared HWND shown/hidden on demand when the user clicks
// any monitor's taskbar button. Never destroyed during the session.
//
// Layout (360 × 280):
//   [0..31]   Tab strip:  "Monitor" | "Settings"
//   [32..279] Content:    Monitor tab -or- Settings tab (child controls)

namespace PeakHUD;

internal static unsafe class Popup
{
    // Window dimensions
    private const int Width    = 360;
    private const int Height   = 280;
    private const int TabH     = 32;
    private const int TabW     = 90;
    private const int Padding  = 12;

    // Tab indices
    private const int TabMonitor  = 0;
    private const int TabSettings = 1;

    private static int  _activeTab = TabMonitor;

    // Child control HWNDs (Settings tab)
    private static nint _hEditRate;
    private static nint _hCheckLabel;
    private static nint _hLabelRate;    // "Update Rate" static label
    private static nint _hLabelMs;     // "ms" static label
    private static nint _hLabelIcon;   // "Icon Label" static label
    private static nint _hLabelColor;  // "Bar Color(s)" static label
    private static nint _hBtnColor;    // owner-drawn button: primary color (util)
    private static nint _hBtnColor2;   // owner-drawn button: secondary color (GPU memory only)

    // Label child for the reading text (Monitor tab)
    private static nint _hReadingLabel;

    // Charcoal text color used by all WM_CTLCOLOR* handlers.
    private const uint TextLight = 0x00EEEEEE; // COLORREF (BBGGRR)

    // Registered class atom
    private static bool _classRegistered;

    // ── Registration ─────────────────────────────────────────────────────────

    public static void RegisterClass(nint hInstance)
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
                hbrBackground = Brushes.Bg,   // charcoal
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
        ActivateTab(popupHwnd, TabMonitor);
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
            hwnd, 1001, hInstance, 0);
        Win32.SendMessageW(_hLabelRate, Win32.WM_SETFONT, hFont, 1);
        sy += 22;

        // EDIT (rate value) — plain edit box, no spinner
        _hEditRate = Win32.CreateWindowExW(Win32.WS_EX_CLIENTEDGE, "EDIT", "1000",
            Win32.WS_CHILD | Win32.ES_NUMBER,
            Padding, sy, 80, 24,
            hwnd, 1002, hInstance, 0);
        Win32.SendMessageW(_hEditRate, Win32.WM_SETFONT, hFont, 1);
        sy += 30;

        // ms label
        _hLabelMs = Win32.CreateWindowExW(0, "STATIC", "ms",
            Win32.WS_CHILD,
            Padding + 86, sy - 28, 30, 24,
            hwnd, 1004, hInstance, 0);
        Win32.SendMessageW(_hLabelMs, Win32.WM_SETFONT, hFont, 1);

        sy += 12;

        // Section label — "Icon Label"
        _hLabelIcon = Win32.CreateWindowExW(0, "STATIC", "Icon Label",
            Win32.WS_CHILD,
            Padding, sy, 200, 20,
            hwnd, 1005, hInstance, 0);
        Win32.SendMessageW(_hLabelIcon, Win32.WM_SETFONT, hFont, 1);
        sy += 22;

        // Checkbox
        _hCheckLabel = Win32.CreateWindowExW(0, "BUTTON", "Show label on icon",
            Win32.WS_CHILD | Win32.BS_AUTOCHECKBOX,
            Padding, sy, 240, 24,
            hwnd, 1006, hInstance, 0);
        Win32.SendMessageW(_hCheckLabel, Win32.WM_SETFONT, hFont, 1);
        sy += 34;

        // Section label — "Bar Color"
        _hLabelColor = Win32.CreateWindowExW(0, "STATIC", "Bar Color",
            Win32.WS_CHILD,
            Padding, sy, 200, 20,
            hwnd, 1007, hInstance, 0);
        Win32.SendMessageW(_hLabelColor, Win32.WM_SETFONT, hFont, 1);
        sy += 22;

        // Owner-drawn color-picker buttons — primary (always) + secondary (GPU only)
        _hBtnColor = Win32.CreateWindowExW(0, "BUTTON", "",
            Win32.WS_CHILD | Win32.BS_OWNERDRAW,
            Padding, sy, 80, 28,
            hwnd, 1008, hInstance, 0);
        Win32.SendMessageW(_hBtnColor, Win32.WM_SETFONT, hFont, 1);

        _hBtnColor2 = Win32.CreateWindowExW(0, "BUTTON", "",
            Win32.WS_CHILD | Win32.BS_OWNERDRAW,
            Padding + 92, sy, 80, 28,
            hwnd, 1009, hInstance, 0);
        Win32.SendMessageW(_hBtnColor2, Win32.WM_SETFONT, hFont, 1);
    }

    // ── Tab management ───────────────────────────────────────────────────────

    private static void ActivateTab(nint hwnd, int tab)
    {
        _activeTab = tab;

        // Show/hide reading label (Monitor tab)
        Win32.ShowWindow(_hReadingLabel, tab == TabMonitor ? Win32.SW_SHOW : Win32.SW_HIDE);

        // Show/hide all settings controls
        int settingsVis = tab == TabSettings ? Win32.SW_SHOW : Win32.SW_HIDE;
        Win32.ShowWindow(_hLabelRate,  settingsVis);
        Win32.ShowWindow(_hEditRate,   settingsVis);
        Win32.ShowWindow(_hLabelMs,    settingsVis);
        Win32.ShowWindow(_hLabelIcon,  settingsVis);
        Win32.ShowWindow(_hCheckLabel, settingsVis);
        Win32.ShowWindow(_hLabelColor, settingsVis);
        Win32.ShowWindow(_hBtnColor,   settingsVis);

        // Second swatch visible for all dual-bar monitors (GPU, Disk, Network).
        bool showSecondary = tab == TabSettings &&
                             App.PopupTarget is Config.GPU or Config.DISK or Config.NETWORK;
        Win32.ShowWindow(_hBtnColor2, showSecondary ? Win32.SW_SHOW : Win32.SW_HIDE);

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
        string text = monitorIndex switch
        {
            Config.GPU     => $"GPU: {m.Current:F1}%  Mem: {m.Current2:F1}%",
            Config.DISK    => $"Read: {DiskMonitor.CurrentReadMBps:F1} MB/s  Write: {DiskMonitor.CurrentWriteMBps:F1} MB/s",
            Config.NETWORK => $"Down: {NetworkMonitor.CurrentReceiveMBps:F1} MB/s  Up: {NetworkMonitor.CurrentSendMBps:F1} MB/s",
            _              => $"{App.MonitorNames[monitorIndex]}: {m.Current:F1}%"
        };
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

            // Charcoal background for all child controls. Returning Brushes.Bg and
            // setting matching SetBkColor makes STATIC/EDIT/BUTTON labels blend in.
            case Win32.WM_CTLCOLORSTATIC:
            case Win32.WM_CTLCOLOREDIT:
            case Win32.WM_CTLCOLORBTN:
                Win32.SetTextColor(wParam, TextLight);
                Win32.SetBkColor(wParam, Win32.RGB(28, 28, 28));
                return Brushes.Bg;

            case Win32.WM_DRAWITEM:
                PaintColorSwatch((Win32.DRAWITEMSTRUCT*)lParam);
                return 1;

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
                        App.RearmTimer();
                    }
                }
                else if (ctrl == _hCheckLabel && notif == Win32.BN_CLICKED)
                {
                    // BST_CHECKED = 1
                    nint state = Win32.SendMessageW(_hCheckLabel, 0x00F0, 0, 0); // BM_GETCHECK
                    Config.Monitors[idx].ShowLabel = state == 1;
                    Config.Save();
                }
                else if (ctrl == _hBtnColor && notif == Win32.BN_CLICKED)
                {
                    OpenColorPicker(hwnd, idx, secondary: false);
                }
                else if (ctrl == _hBtnColor2 && notif == Win32.BN_CLICKED)
                {
                    OpenColorPicker(hwnd, idx, secondary: true);
                }
                break;
            }

            case Win32.WM_CLOSE:
                Win32.ShowWindow(hwnd, Win32.SW_HIDE);
                return 0;
        }

        return Win32.DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    // ── Color picker ─────────────────────────────────────────────────────────

    // 16 custom colors used by ChooseColor; retained across invocations.
    private static readonly uint[] CustomColors = new uint[16];

    private static void OpenColorPicker(nint ownerHwnd, int monitorIndex, bool secondary)
    {
        ref var cfg = ref Config.Monitors[monitorIndex];
        uint current = secondary ? cfg.ColorSecondary : cfg.Color;

        // ChooseColor takes a COLORREF (BBGGRR); config stores RRGGBB. Convert.
        byte r = (byte)((current >> 16) & 0xFF);
        byte g = (byte)((current >>  8) & 0xFF);
        byte b = (byte)( current        & 0xFF);

        uint chosen;
        fixed (uint* custom = CustomColors)
        {
            var cc = new Win32.CHOOSECOLOR
            {
                lStructSize  = (uint)sizeof(Win32.CHOOSECOLOR),
                hwndOwner    = ownerHwnd,
                rgbResult    = Win32.RGB(r, g, b),
                lpCustColors = custom,
                Flags        = Win32.CC_RGBINIT | Win32.CC_FULLOPEN,
            };

            if (!Win32.ChooseColorW(&cc)) return;

            // Convert COLORREF (BBGGRR) back to config storage format (RRGGBB).
            byte rr = (byte)( cc.rgbResult        & 0xFF);
            byte gg = (byte)((cc.rgbResult >>  8) & 0xFF);
            byte bb = (byte)((cc.rgbResult >> 16) & 0xFF);
            chosen = (uint)((rr << 16) | (gg << 8) | bb);
        }

        if (secondary)
        {
            cfg.ColorSecondary = chosen;
            Config.Save();
            Brushes.SetSecondaryColor(monitorIndex, chosen);
        }
        else
        {
            cfg.Color = chosen;
            Config.Save();
            Brushes.SetMonitorColor(monitorIndex, chosen);
        }

        // Force a repaint of the swatch buttons and the chart behind them.
        Win32.InvalidateRect(_hBtnColor,  0, true);
        Win32.InvalidateRect(_hBtnColor2, 0, true);
        Win32.InvalidateRect(ownerHwnd,   0, false);
    }

    // Owner-draw paint for the color-picker buttons (WM_DRAWITEM).
    private static void PaintColorSwatch(Win32.DRAWITEMSTRUCT* dis)
    {
        int idx = App.PopupTarget;
        if (idx < 0) return;

        nint hdc  = dis->hDC;
        var  rect = dis->rcItem;

        // Primary button shows ByMonitor[idx]; secondary button shows the monitor's
        // secondary brush (write for disk, send for network, memory for GPU).
        nint fill = dis->hwndItem == _hBtnColor2
            ? Brushes.GetSecondaryBrush(idx)
            : Brushes.ByMonitor[idx];
        Win32.FillRect(hdc, rect, fill);

        // Thin light border so the swatch reads on charcoal
        nint border = Win32.CreateSolidBrush(Win32.RGB(90, 90, 90));
        Win32.FrameRect(hdc, rect, border);
        Win32.DeleteObject(border);
    }

    // ── Painting ─────────────────────────────────────────────────────────────

    private static void Paint(nint hwnd)
    {
        nint hdc = Win32.BeginPaint(hwnd, out var ps);

        // Fill the full client area with charcoal (covers the settings tab and
        // any area not overwritten by the tab strip / chart).
        var fullRect = new Win32.RECT(0, 0, Width, Height);
        Win32.FillRect(hdc, fullRect, Brushes.Bg);

        // ── Tab strip ────────────────────────────────────────────────────────
        PaintTabs(hdc);

        // ── Content area separator ────────────────────────────────────────────
        var sepRect = new Win32.RECT(0, TabH - 1, Width, TabH);
        Win32.FillRect(hdc, sepRect, Brushes.TabActive);

        // ── Monitor tab: bar chart ────────────────────────────────────────────
        if (_activeTab == TabMonitor && App.PopupTarget >= 0)
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
            Win32.SetTextColor(hdc, TextLight);
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

        bool isDual = monitorIndex is Config.GPU or Config.DISK or Config.NETWORK;
        nint primary = Brushes.ByMonitor[monitorIndex];
        var (dualPrimary, dualSecondary, dualBlend) = isDual
            ? Brushes.GetDualBrushes(monitorIndex)
            : (0, 0, 0);
        float barW = chartW / (float)MonitorState.HistoryLen;
        for (int i = 0; i < MonitorState.HistoryLen; i++)
        {
            float val  = MonitorRenderer.HistoryAt(ref m, i);
            float barH = chartH * (val / 100f);
            if (barH < 1f) barH = 1f;

            int bx = (int)(i * barW) + 1;
            int bw = (int)((i + 1) * barW) - bx - 1;

            if (isDual)
            {
                // Both bars full-width from the bottom. Overlap (bottom → shorter
                // bar's top) uses the blended color; taller bar's exclusive top
                // uses its own color.
                float val2  = MonitorRenderer.HistoryAt2(ref m, i);
                float barH2 = chartH * (val2 / 100f);
                if (barH2 < 1f) barH2 = 1f;

                int by1     = chartBot - (int)barH;
                int by2     = chartBot - (int)barH2;
                int byBlend = Math.Max(by1, by2);

                Win32.FillRect(hdc, new Win32.RECT(bx, byBlend, bx + bw, chartBot), dualBlend);

                if (by1 < by2)
                    Win32.FillRect(hdc, new Win32.RECT(bx, by1, bx + bw, byBlend), dualPrimary);
                else if (by2 < by1)
                    Win32.FillRect(hdc, new Win32.RECT(bx, by2, bx + bw, byBlend), dualSecondary);
            }
            else
            {
                int by = chartBot - (int)barH;
                Win32.FillRect(hdc, new Win32.RECT(bx, by, bx + bw, chartBot), primary);
            }
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