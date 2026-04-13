using System.Runtime.CompilerServices;

namespace PeakHUD;
// MonitorState holds per-monitor GDI resources, ring buffer, and current reading.
// Icon rendering uses a single shared DC (App.SharedDC) — all monitors render sequentially
// in the WM_TIMER handler, so there is no contention.

internal unsafe struct MonitorState
{
    public const int HistoryLen  = 10;
    public const int IconSize    = 32;

    // Ring buffer (primary)
    public fixed float History[HistoryLen];
    public int         Head;          // next write position
    public float       Current;       // latest reading (0–100)

    // Ring buffer (secondary — GPU memory, same Head index)
    public fixed float History2[HistoryLen];
    public float       Current2;

    // GDI resources (per-monitor, allocated once in Init)
    public nint HBitmap;             // DIBSection 32×32 32bpp
    public nint HMask;               // 1bpp mask (all zeros = fully opaque)
    public nint HPrevBitmap;         // SelectObject return value (to restore DC)
    public nint HIcon;               // current live HICON (destroyed next tick)

    // Label text ("CPU", "RAM", "DSK", "NET", "GPU") as fixed char array
    public fixed char Label[4];      // max 3 chars + null
    public int LabelLen;

    // Config index (into Config.Monitors[])
    public int ConfigIndex;
}

// ── Global shared GDI brushes ────────────────────────────────────────────────

internal static class Brushes
{
    public static nint Bg;       // RGB(28,28,28) — icon background

    // Per-monitor fixed color, indexed by Config.CPU / RAM / DISK / NETWORK / GPU.
    // GPU's slot holds the primary (util) color; the memory bar uses GpuMem.
    public static readonly nint[] ByMonitor = new nint[Config.COUNT];

    // GPU dual-bar palette — light blue × purple, blending to periwinkle.
    public static nint GpuUtil;  // RGB( 80, 190, 255) — light blue (same as CPU)
    public static nint GpuMem;   // RGB(180,  70, 220) — purple
    public static nint GpuBlend; // blended overlap

    // Disk dual-bar palette — green (read) × teal (write).
    public static nint DiskWrite; // RGB( 80, 200, 200) — teal
    public static nint DiskBlend; // blended overlap

    // Network dual-bar palette — red (receive) × orange (send).
    public static nint NetSend;  // RGB(255, 158,  60) — orange
    public static nint NetBlend; // blended overlap

    public static nint Font;     // shared HFONT for icon labels + popup text

    // Popup chrome (charcoal palette)
    public static nint TabBg;    // inactive tab background
    public static nint TabActive;// active tab background / content separator
    public static nint Accent;   // 2px underline on active tab

    public static void Init()
    {
        Bg = Win32.CreateSolidBrush(Win32.RGB(28, 28, 28));

        for (int i = 0; i < Config.COUNT; i++)
            ByMonitor[i] = Win32.CreateSolidBrush(RgbToColorRef(Config.Monitors[i].Color));

        // GPU palette — util is the ByMonitor[GPU] alias; memory comes from
        // Config.Monitors[GPU].ColorSecondary; blend is the computed RGB midpoint.
        GpuUtil  = ByMonitor[Config.GPU];
        GpuMem   = Win32.CreateSolidBrush(RgbToColorRef(Config.Monitors[Config.GPU].ColorSecondary));
        GpuBlend = Win32.CreateSolidBrush(RgbToColorRef(
            BlendRgb(Config.Monitors[Config.GPU].Color,
                Config.Monitors[Config.GPU].ColorSecondary)));

        // Disk palette — read (primary) × write (secondary).
        DiskWrite = Win32.CreateSolidBrush(RgbToColorRef(Config.Monitors[Config.DISK].ColorSecondary));
        DiskBlend = Win32.CreateSolidBrush(RgbToColorRef(
            BlendRgb(Config.Monitors[Config.DISK].Color,
                Config.Monitors[Config.DISK].ColorSecondary)));

        // Network palette — receive (primary) × send (secondary).
        NetSend  = Win32.CreateSolidBrush(RgbToColorRef(Config.Monitors[Config.NETWORK].ColorSecondary));
        NetBlend = Win32.CreateSolidBrush(RgbToColorRef(
            BlendRgb(Config.Monitors[Config.NETWORK].Color,
                Config.Monitors[Config.NETWORK].ColorSecondary)));

        // 13px "Trebuchet MS" — same face as the original WinForms label bitmap
        Font = Win32.CreateFontW(
            -13, 0, 0, 0,
            Win32.FW_NORMAL,
            0, 0, 0,
            0,   // ANSI_CHARSET
            0, 0,
            5,   // CLEARTYPE_QUALITY
            0,
            "Trebuchet MS");

        // Popup chrome — charcoal palette (matches icon background).
        TabBg     = Win32.CreateSolidBrush(Win32.RGB(20,  20,  20));   // inactive tab
        TabActive = Win32.CreateSolidBrush(Win32.RGB(40,  40,  40));   // active tab / content
        Accent    = Win32.CreateSolidBrush(Win32.RGB(80, 190, 255));   // underline on active tab
    }

    // Replace a single monitor's primary brush with a new RGB color. Caller must
    // update Config.Monitors[index].Color and invalidate any windows that depend
    // on the brush afterwards.
    public static void SetMonitorColor(int index, uint rgb)
    {
        if (index < 0 || index >= ByMonitor.Length) return;

        nint old = ByMonitor[index];
        nint fresh = Win32.CreateSolidBrush(RgbToColorRef(rgb));
        ByMonitor[index] = fresh;

        // GPU slot aliases GpuUtil — keep the alias in sync and recompute the blend.
        if (index == Config.GPU)     { GpuUtil = fresh; RebuildGpuBlend();  }
        if (index == Config.DISK)      RebuildDiskBlend();
        if (index == Config.NETWORK)   RebuildNetBlend();

        if (old != 0) Win32.DeleteObject(old);
    }

    // Replace the secondary brush for any dual-bar monitor.
    public static void SetSecondaryColor(int index, uint rgb)
    {
        switch (index)
        {
            case Config.GPU:
            {
                nint old = GpuMem;
                GpuMem = Win32.CreateSolidBrush(RgbToColorRef(rgb));
                RebuildGpuBlend();
                if (old != 0) Win32.DeleteObject(old);
                break;
            }
            case Config.DISK:
            {
                nint old = DiskWrite;
                DiskWrite = Win32.CreateSolidBrush(RgbToColorRef(rgb));
                RebuildDiskBlend();
                if (old != 0) Win32.DeleteObject(old);
                break;
            }
            case Config.NETWORK:
            {
                nint old = NetSend;
                NetSend = Win32.CreateSolidBrush(RgbToColorRef(rgb));
                RebuildNetBlend();
                if (old != 0) Win32.DeleteObject(old);
                break;
            }
        }
    }

    // Return the secondary brush for the color-picker swatch.
    public static nint GetSecondaryBrush(int configIndex) => configIndex switch
    {
        Config.GPU     => GpuMem,
        Config.DISK    => DiskWrite,
        Config.NETWORK => NetSend,
        _              => 0
    };

    // Return (primary, secondary, blend) brushes for a dual-bar monitor.
    public static (nint primary, nint secondary, nint blend) GetDualBrushes(int configIndex) =>
        configIndex switch
        {
            Config.GPU     => (GpuUtil,                   GpuMem,    GpuBlend),
            Config.DISK    => (ByMonitor[Config.DISK],    DiskWrite, DiskBlend),
            Config.NETWORK => (ByMonitor[Config.NETWORK], NetSend,   NetBlend),
            _              => (ByMonitor[configIndex],    0,         0)
        };

    private static void RebuildGpuBlend()
    {
        uint blend = BlendRgb(
            Config.Monitors[Config.GPU].Color,
            Config.Monitors[Config.GPU].ColorSecondary);
        nint old = GpuBlend;
        GpuBlend = Win32.CreateSolidBrush(RgbToColorRef(blend));
        if (old != 0) Win32.DeleteObject(old);
    }

    private static void RebuildDiskBlend()
    {
        uint blend = BlendRgb(
            Config.Monitors[Config.DISK].Color,
            Config.Monitors[Config.DISK].ColorSecondary);
        nint old = DiskBlend;
        DiskBlend = Win32.CreateSolidBrush(RgbToColorRef(blend));
        if (old != 0) Win32.DeleteObject(old);
    }

    private static void RebuildNetBlend()
    {
        uint blend = BlendRgb(
            Config.Monitors[Config.NETWORK].Color,
            Config.Monitors[Config.NETWORK].ColorSecondary);
        nint old = NetBlend;
        NetBlend = Win32.CreateSolidBrush(RgbToColorRef(blend));
        if (old != 0) Win32.DeleteObject(old);
    }

    // RGB midpoint of two 0x00RRGGBB values.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint BlendRgb(uint a, uint b)
    {
        uint ar = (a >> 16) & 0xFF, ag = (a >> 8) & 0xFF, ab = a & 0xFF;
        uint br = (b >> 16) & 0xFF, bg = (b >> 8) & 0xFF, bb = b & 0xFF;
        return (((ar + br) >> 1) << 16) | (((ag + bg) >> 1) << 8) | ((ab + bb) >> 1);
    }

    // Convert 0x00RRGGBB (stored) to GDI COLORREF 0x00BBGGRR (byte-order-swapped).
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint RgbToColorRef(uint rgb)
    {
        byte r = (byte)((rgb >> 16) & 0xFF);
        byte g = (byte)((rgb >>  8) & 0xFF);
        byte b = (byte)( rgb        & 0xFF);
        return Win32.RGB(r, g, b);
    }

}

// ── MonitorRenderer ──────────────────────────────────────────────────────────

internal static unsafe class MonitorRenderer
{
    // Allocate GDI resources for one monitor. Called once per monitor at startup.
    public static void Init(ref MonitorState m, int configIndex, ReadOnlySpan<char> label)
    {
        m.ConfigIndex = configIndex;

        // Copy label (up to 3 chars)
        int len = Math.Min(label.Length, 3);
        for (int i = 0; i < len; i++) m.Label[i] = label[i];
        m.Label[len] = '\0';
        m.LabelLen = len;

        // 32×32 32bpp DIBSection (top-down, BI_RGB)
        var bmi = new Win32.BITMAPINFO
        {
            bmiHeader = new Win32.BITMAPINFOHEADER
            {
                biSize        = (uint)sizeof(Win32.BITMAPINFOHEADER),
                biWidth       = MonitorState.IconSize,
                biHeight      = -MonitorState.IconSize, // negative = top-down
                biPlanes      = 1,
                biBitCount    = 32,
                biCompression = Win32.BI_RGB,
                biSizeImage   = MonitorState.IconSize * MonitorState.IconSize * 4
            }
        };
        m.HBitmap = Win32.CreateDIBSection(0, &bmi, Win32.DIB_RGB_COLORS, out _, 0, 0);

        // 1bpp monochrome mask — all zeros means fully opaque for CreateIconIndirect
        m.HMask = Win32.CreateBitmap(MonitorState.IconSize, MonitorState.IconSize, 1, 1, 0);
    }

    // Push a new reading into the ring buffer.
    public static void Push(ref MonitorState m, float value)
    {
        m.Current = value;
        m.History[m.Head] = value;
        m.Head = (m.Head + 1) % MonitorState.HistoryLen;
    }

    // Push a secondary reading (e.g. GPU memory) — shares the same Head index.
    // Must be called after Push() so the same slot is written.
    public static void Push2(ref MonitorState m, float value)
    {
        m.Current2 = value;
        int prev = (m.Head - 1 + MonitorState.HistoryLen) % MonitorState.HistoryLen;
        m.History2[prev] = value;
    }

    // Render the 32×32 icon into HBitmap using the shared DC, then push a new HICON
    // to the taskbar via WM_SETICON. Destroys the previous HICON.
    public static void RenderAndPush(ref MonitorState m, nint iconHwnd, nint sharedDC)
    {
        // Attach this monitor's DIBSection to the shared DC
        m.HPrevBitmap = Win32.SelectObject(sharedDC, m.HBitmap);

        // Clear to dark background
        var fullRect = new Win32.RECT(0, 0, MonitorState.IconSize, MonitorState.IconSize);
        Win32.FillRect(sharedDC, fullRect, Brushes.Bg);

        // Draw bar chart (10 bars across 32px)
        bool isDual = m.ConfigIndex is Config.GPU or Config.DISK or Config.NETWORK;
        nint primary = Brushes.ByMonitor[m.ConfigIndex];
        var (dualPrimary, dualSecondary, dualBlend) = isDual
            ? Brushes.GetDualBrushes(m.ConfigIndex)
            : (0, 0, 0);
        float barW = MonitorState.IconSize / (float)MonitorState.HistoryLen;
        for (int i = 0; i < MonitorState.HistoryLen; i++)
        {
            float val  = m.History[(m.Head + i) % MonitorState.HistoryLen];
            float barH = MonitorState.IconSize * (val / 100f);
            if (barH < 1f) barH = 1f;

            int bx = (int)(i * barW);
            int bw = (int)((i + 1) * barW) - bx;

            if (isDual)
            {
                // Both bars draw at full width from the bottom. Overlap region
                // (bottom up to the shorter bar's top) uses the blended color;
                // the taller bar's exclusive top segment uses its own color.
                float val2  = m.History2[(m.Head + i) % MonitorState.HistoryLen];
                float barH2 = MonitorState.IconSize * (val2 / 100f);
                if (barH2 < 1f) barH2 = 1f;

                int by1     = MonitorState.IconSize - (int)barH;   // primary bar top
                int by2     = MonitorState.IconSize - (int)barH2;  // secondary bar top
                int byBlend = Math.Max(by1, by2);                  // top of blended overlap

                Win32.FillRect(sharedDC, new Win32.RECT(bx, byBlend, bx + bw, MonitorState.IconSize), dualBlend);

                if (by1 < by2)
                    Win32.FillRect(sharedDC, new Win32.RECT(bx, by1, bx + bw, byBlend), dualPrimary);
                else if (by2 < by1)
                    Win32.FillRect(sharedDC, new Win32.RECT(bx, by2, bx + bw, byBlend), dualSecondary);
            }
            else
            {
                int by = MonitorState.IconSize - (int)barH;
                Win32.FillRect(sharedDC, new Win32.RECT(bx, by, bx + bw, MonitorState.IconSize), primary);
            }
        }

        // Optionally draw label text ("CPU", "RAM", etc.)
        if (Config.Monitors[m.ConfigIndex].ShowLabel)
        {
            Win32.SelectObject(sharedDC, Brushes.Font);
            Win32.SetBkMode(sharedDC, Win32.TRANSPARENT);
            Win32.SetTextColor(sharedDC, Win32.RGB(255, 255, 255));
            fixed (char* label = m.Label)
                Win32.TextOutW(sharedDC, 0, 0, label, m.LabelLen);
        }

        // Produce HICON from the DIBSection
        var ii = new Win32.ICONINFO { fIcon = 1, hbmMask = m.HMask, hbmColor = m.HBitmap };
        nint newIcon = Win32.CreateIconIndirect(ref ii);

        // Push to taskbar
        Win32.SendMessageW(iconHwnd, Win32.WM_SETICON, Win32.ICON_SMALL, newIcon);
        Win32.SendMessageW(iconHwnd, Win32.WM_SETICON, Win32.ICON_BIG,   newIcon);

        // Destroy the previous icon (one-deep pool)
        if (m.HIcon != 0) Win32.DestroyIcon(m.HIcon);
        m.HIcon = newIcon;

        // Restore the shared DC
        Win32.SelectObject(sharedDC, m.HPrevBitmap);
    }

    // Free GDI resources. Called on WM_DESTROY.
    public static void Dispose(ref MonitorState m)
    {
        if (m.HIcon   != 0) { Win32.DestroyIcon(m.HIcon);    m.HIcon   = 0; }
        if (m.HBitmap != 0) { Win32.DeleteObject(m.HBitmap); m.HBitmap = 0; }
        if (m.HMask   != 0) { Win32.DeleteObject(m.HMask);   m.HMask   = 0; }
    }

    // Helper: read the i-th history value in chronological order.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float HistoryAt(ref MonitorState m, int i)
        => m.History[(m.Head + i) % MonitorState.HistoryLen];

    // Helper: read the i-th secondary history value in chronological order.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float HistoryAt2(ref MonitorState m, int i)
        => m.History2[(m.Head + i) % MonitorState.HistoryLen];
}