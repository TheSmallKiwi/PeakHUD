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

    private static void ApplyDefaults()
    {
        for (int i = 0; i < COUNT; i++)
        {
            Monitors[i].Enabled      = true;
            Monitors[i].UpdateRateMs = 1000;
            Monitors[i].ShowLabel    = true;
            Monitors[i].MaxBytesPerSec = 0;
        }
        Monitors[DISK   ].MaxBytesPerSec = 524_288_000L;  // 500 MB/s
        Monitors[NETWORK].MaxBytesPerSec = 131_072_000L;  // 125 MB/s (1 Gb link)
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
            sb.AppendLine();
        }
        File.WriteAllText(GetConfigPath(), sb.ToString());
    }
}
