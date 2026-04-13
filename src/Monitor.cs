using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

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
    public static nint Green;    // CPU < 50%
    public static nint Yellow;   // CPU 50–74%
    public static nint Red;      // CPU >= 75%
    public static nint Blue;     // GPU memory
    public static nint Font;     // shared HFONT for icon labels + popup text

    // Popup-specific
    public static nint TabBg;    // inactive tab background  (COLOR_BTNFACE)
    public static nint TabActive;// active tab background    (COLOR_WINDOW)
    public static nint Accent;   // 2px underline on active tab (COLOR_HIGHLIGHT)
    public static nint TextBg;   // popup content background (COLOR_WINDOW)

    public static void Init()
    {
        Bg     = Win32.CreateSolidBrush(Win32.RGB(28,  28,  28));
        Green  = Win32.CreateSolidBrush(Win32.RGB(0,   205, 0));
        Yellow = Win32.CreateSolidBrush(Win32.RGB(255, 220, 0));
        Red    = Win32.CreateSolidBrush(Win32.RGB(255, 69,  0));
        Blue   = Win32.CreateSolidBrush(Win32.RGB(30,  144, 255));

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

        // Popup brushes — system colors so they respect light/dark mode
        TabBg    = Win32.GetSysColorBrush(15); // COLOR_BTNFACE
        TabActive= Win32.GetSysColorBrush(Win32.COLOR_WINDOW);
        Accent   = Win32.GetSysColorBrush(Win32.COLOR_HIGHLIGHT);
        TextBg   = Win32.GetSysColorBrush(Win32.COLOR_WINDOW);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static nint ForValue(float pct) => pct switch
    {
        < 50f => Green,
        < 75f => Yellow,
        _     => Red
    };
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
                biSizeImage   = (uint)(MonitorState.IconSize * MonitorState.IconSize * 4)
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
        bool isDual = m.ConfigIndex == Config.GPU;
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
                // Left half: GPU utilization; right half: GPU memory
                int leftW = Math.Max(1, bw / 2);

                int by = MonitorState.IconSize - (int)barH;
                Win32.FillRect(sharedDC, new Win32.RECT(bx, by, bx + leftW, MonitorState.IconSize), Brushes.ForValue(val));

                float val2  = m.History2[(m.Head + i) % MonitorState.HistoryLen];
                float barH2 = MonitorState.IconSize * (val2 / 100f);
                if (barH2 < 1f) barH2 = 1f;
                int by2 = MonitorState.IconSize - (int)barH2;
                Win32.FillRect(sharedDC, new Win32.RECT(bx + leftW, by2, bx + bw, MonitorState.IconSize), Brushes.Blue);
            }
            else
            {
                int by = MonitorState.IconSize - (int)barH;
                Win32.FillRect(sharedDC, new Win32.RECT(bx, by, bx + bw, MonitorState.IconSize), Brushes.ForValue(val));
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
