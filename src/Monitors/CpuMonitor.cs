// CPU utilization via GetSystemTimes — ported verbatim from previous implementation.
// Measures (kernel+user - idle) / (kernel+user) delta between ticks.

internal static class CpuMonitor
{
    private static ulong _prevIdle;
    private static ulong _prevTotal;

    public static void Init()
    {
        Win32.GetSystemTimes(out var fi, out var fk, out var fu);
        _prevIdle  = ToU64(fi);
        _prevTotal = ToU64(fk) + ToU64(fu);
    }

    public static float Read()
    {
        Win32.GetSystemTimes(out var idle, out var kernel, out var user);
        ulong curIdle  = ToU64(idle);
        ulong curTotal = ToU64(kernel) + ToU64(user);

        ulong di = curIdle  - _prevIdle;
        ulong dt = curTotal - _prevTotal;

        _prevIdle  = curIdle;
        _prevTotal = curTotal;

        return dt == 0 ? 0f : (1f - (float)di / dt) * 100f;
    }

    private static ulong ToU64(Win32.FILETIME ft)
        => ((ulong)ft.High << 32) | ft.Low;
}
