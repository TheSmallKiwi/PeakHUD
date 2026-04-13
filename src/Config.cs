using System.Runtime.InteropServices;
using System.Text;

// Per-monitor configuration, persisted to %APPDATA%\PeakHUD\settings.ini.
// Hand-rolled INI — no external JSON library (System.Text.Json source-gen adds ~300 KB to AOT binary).

internal struct MonitorConfig
{
    public bool   Enabled;
    public int    UpdateRateMs;
    public bool   ShowLabel;
    public long   MaxBytesPerSec;  // disk and network only; 0 = not applicable
    public uint   Color;           // 0x00RRGGBB, primary bar (util for GPU)
    public uint   ColorSecondary;  // 0x00RRGGBB, secondary bar (memory for GPU; unused elsewhere)
}

internal static class Config
{
    // Monitor indices (must match App.Monitors array order)
    public const int CPU     = 0;
    public const int RAM     = 1;
    public const int DISK    = 2;
    public const int NETWORK = 3;
    public const int GPU     = 4;
    public const int COUNT   = 5;

    private static readonly string[] Sections = ["cpu", "ram", "disk", "network", "gpu"];

    public static MonitorConfig[] Monitors = new MonitorConfig[COUNT];

    private static string? _path;

    // ── Defaults ─────────────────────────────────────────────────────────────

    // Default per-monitor primary colors (0x00RRGGBB).
    public static readonly uint[] DefaultColors =
    [
        0x00_50BEFF, // CPU     — light blue (80, 190, 255)
        0x00_2859DC, // RAM     — dark blue  (40,  89, 220)
        0x00_50C864, // DISK    — green      (80, 200, 100)
        0x00_FF6E6E, // NETWORK — light red  (255,110, 110)
        0x00_50BEFF, // GPU     — light blue util (same as CPU)
    ];

    // Default secondary colors; only GPU uses this slot (memory bar).
    public const uint DefaultGpuMemColor = 0x00_B446DC; // purple (180, 70, 220)

    private static void ApplyDefaults()
    {
        for (int i = 0; i < COUNT; i++)
        {
            Monitors[i].Enabled      = true;
            Monitors[i].UpdateRateMs = 1000;
            Monitors[i].ShowLabel    = true;
            Monitors[i].MaxBytesPerSec = 0;
            Monitors[i].Color        = DefaultColors[i];
            Monitors[i].ColorSecondary = 0;
        }
        Monitors[DISK   ].MaxBytesPerSec = 524_288_000L;  // 500 MB/s
        Monitors[NETWORK].MaxBytesPerSec = 131_072_000L;  // 125 MB/s (1 Gb link)
        Monitors[GPU    ].ColorSecondary = DefaultGpuMemColor;
    }

    // ── Path resolution ───────────────────────────────────────────────────────

    private static string GetConfigPath()
    {
        if (_path is not null) return _path;

        Win32.SHGetKnownFolderPath(Win32.FOLDERID_RoamingAppData, 0, 0, out nint pszPath);
        string appData = Marshal.PtrToStringUni(pszPath) ?? "";
        Win32.CoTaskMemFree(pszPath);

        string dir = Path.Combine(appData, "PeakHUD");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "settings.ini");
        return _path;
    }

    // ── Load ─────────────────────────────────────────────────────────────────

    public static void Load()
    {
        ApplyDefaults();

        string path = GetConfigPath();
        if (!File.Exists(path))
        {
            Save();  // write defaults on first run
            return;
        }

        string[] lines = File.ReadAllLines(path);
        int section = -1;

        foreach (string raw in lines)
        {
            string line = raw.Trim();
            if (line.Length == 0 || line[0] == ';') continue;

            if (line[0] == '[')
            {
                // Section header
                string name = line.TrimStart('[').TrimEnd(']').Trim().ToLowerInvariant();
                section = Array.IndexOf(Sections, name);
                continue;
            }

            if (section < 0 || section >= COUNT) continue;

            int eq = line.IndexOf('=');
            if (eq < 0) continue;

            string key   = line[..eq].Trim().ToLowerInvariant();
            string value = line[(eq + 1)..].Trim();

            switch (key)
            {
                case "enabled":
                    Monitors[section].Enabled = value.Equals("true", StringComparison.OrdinalIgnoreCase);
                    break;
                case "update_rate_ms":
                    if (int.TryParse(value, out int rate))
                        Monitors[section].UpdateRateMs = Math.Clamp(rate, 100, 10_000);
                    break;
                case "show_label":
                    Monitors[section].ShowLabel = value.Equals("true", StringComparison.OrdinalIgnoreCase);
                    break;
                case "max_bytes_per_sec":
                    if (long.TryParse(value, out long max))
                        Monitors[section].MaxBytesPerSec = max;
                    break;
                case "color":
                {
                    string hex = value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? value[2..] : value;
                    if (uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber,
                                      System.Globalization.CultureInfo.InvariantCulture, out uint rgb))
                        Monitors[section].Color = rgb & 0x00FFFFFFu;
                    break;
                }
                case "color_secondary":
                {
                    string hex = value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? value[2..] : value;
                    if (uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber,
                                      System.Globalization.CultureInfo.InvariantCulture, out uint rgb))
                        Monitors[section].ColorSecondary = rgb & 0x00FFFFFFu;
                    break;
                }
            }
        }
    }

    // ── Save ─────────────────────────────────────────────────────────────────

    public static void Save()
    {
        var sb = new StringBuilder();
        for (int i = 0; i < COUNT; i++)
        {
            sb.AppendLine($"[{Sections[i]}]");
            sb.AppendLine($"enabled={Monitors[i].Enabled.ToString().ToLowerInvariant()}");
            sb.AppendLine($"update_rate_ms={Monitors[i].UpdateRateMs}");
            sb.AppendLine($"show_label={Monitors[i].ShowLabel.ToString().ToLowerInvariant()}");
            if (Monitors[i].MaxBytesPerSec > 0)
                sb.AppendLine($"max_bytes_per_sec={Monitors[i].MaxBytesPerSec}");
            sb.AppendLine($"color=0x{Monitors[i].Color:X6}");
            if (Monitors[i].ColorSecondary != 0)
                sb.AppendLine($"color_secondary=0x{Monitors[i].ColorSecondary:X6}");
            sb.AppendLine();
        }
        File.WriteAllText(GetConfigPath(), sb.ToString());
    }
}
