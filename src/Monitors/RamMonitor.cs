// RAM utilization via GlobalMemoryStatusEx.
// dwMemoryLoad is already 0–100; no delta calculation needed.

internal static class RamMonitor
{
    public static void Init() { }  // no warm-up needed

    public static float Read()
    {
        var status = Win32.MEMORYSTATUSEX.Create();
        Win32.GlobalMemoryStatusEx(ref status);
        return (float)status.dwMemoryLoad;
    }
}
