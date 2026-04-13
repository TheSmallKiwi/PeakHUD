# PeakHUD

A lightweight Windows taskbar widget that displays live system metrics — CPU, RAM, Disk, Network, and GPU — as miniature sparkline charts pinned to your taskbar.

Built with pure Win32 and NativeAOT: no WinForms, no WPF, no Electron. The resulting binary is under 2 MB and uses minimal memory.

## Features

- Live sparkline charts rendered directly into taskbar icons
- Monitors: CPU usage, RAM usage, Disk I/O, Network throughput, GPU utilization + VRAM
- Click any taskbar icon to open a larger popup chart
- Per-monitor configuration: enable/disable, update rate, bar colors, max scale
- Settings persist to `%APPDATA%\Roaming\PeakHUD\settings.ini` (written on first run)
- Windows 11 dark theme support

## Requirements

- Windows 10 / 11 (x64)
- No runtime required — self-contained NativeAOT binary

## Installation

Download `PeakHUD.exe` from the [latest release](../../releases/latest) and run it. No installer needed.

To start PeakHUD automatically with Windows, add a shortcut to `%APPDATA%\Roaming\Microsoft\Windows\Start Menu\Programs\Startup`.

**Note for first-time users:** Windows SmartScreen may warn that the file is unrecognized. This is normal for new
unsigned binaries with no download history. Click More info → Run anyway to proceed.

## Configuration

On first launch, PeakHUD writes default settings to `%APPDATA%\PeakHUD\settings.ini`. Edit this file to customize each monitor:

```ini
[cpu]
enabled=true
update_rate_ms=1000
show_label=true
color=0x50BEFF

[ram]
enabled=true
update_rate_ms=1000
show_label=true
color=0x2859DC

[disk]
enabled=true
update_rate_ms=1000
show_label=true
max_bytes_per_sec=524288000
color=0x50C864

[network]
enabled=true
update_rate_ms=1000
show_label=true
max_bytes_per_sec=131072000
color=0xFF6E6E

[gpu]
enabled=true
update_rate_ms=1000
show_label=true
color=0x50BEFF
color_secondary=0xB446DC
```

**Keys:**

| Key | Description |
|-----|-------------|
| `enabled` | Show or hide this monitor's taskbar icon |
| `update_rate_ms` | Refresh interval in milliseconds (100–10000) |
| `show_label` | Display the metric name inside the icon |
| `max_bytes_per_sec` | Scale ceiling for Disk and Network (bytes/sec) |
| `color` | Bar color as `0xRRGGBB` hex |
| `color_secondary` | Secondary bar color (GPU memory only) |

Restart PeakHUD after editing the file.

## Building from source

Requires [.NET 10 SDK](https://dotnet.microsoft.com/download) and the Visual C++ build tools (for NativeAOT linking).

```
dotnet publish -c Release -r win-x64
```

Output: `bin\Release\net10.0-windows\win-x64\publish\PeakHUD.exe`

## Tech

- **Language:** C# 13 / .NET 10
- **Runtime:** NativeAOT (self-contained, no CLR)
- **UI:** Raw Win32 — `CreateWindowExW`, `SetTimer`, GDI for icon rendering
- **No dependencies** beyond the Windows SDK
