// Disk I/O via NtQuerySystemInformation(SystemPerformanceInformation=2).
// IoReadTransferCount (offset 8) + IoWriteTransferCount (offset 16) are
// system-wide cumulative byte counters covering all physical drives.
// No file handles, no elevation needed.

internal static unsafe class DiskMonitor
{
    private const int SystemPerformanceInformation = 2;

    private static long  _prevBytes;
    private static ulong _prevTick;
    private static uint  _bufLen;   // probed once at init

    public static void Init()
    {
        // Probe the required buffer size (NtQuerySystemInformation fills ReturnLength
        // with the required size even when it returns STATUS_INFO_LENGTH_MISMATCH).
        Win32.NtQuerySystemInformation(SystemPerformanceInformation, null, 0, out _bufLen);
        if (_bufLen == 0) _bufLen = 312; // fallback for Windows 10/11 x64

        _prevBytes = QueryBytes();
        _prevTick  = Win32.GetTickCount64();
    }

    public static float Read()
    {
        ulong now       = Win32.GetTickCount64();
        long  cur       = QueryBytes();
        ulong deltaTick = now - _prevTick;
        long  delta     = cur - _prevBytes;
        _prevBytes = cur;
        _prevTick  = now;

        if (deltaTick == 0 || delta <= 0) return 0f;

        double bytesPerSec = delta * 1000.0 / deltaTick;
        long   maxBps      = Config.Monitors[Config.DISK].MaxBytesPerSec;
        if (maxBps <= 0) maxBps = 524_288_000L;

        return (float)Math.Clamp(bytesPerSec / maxBps * 100.0, 0.0, 100.0);
    }

    private static long QueryBytes()
    {
        byte* buf = stackalloc byte[(int)_bufLen];
        if (Win32.NtQuerySystemInformation(
                SystemPerformanceInformation, buf, _bufLen, out _) != 0)
            return _prevBytes; // failed — report zero delta this tick
        return *(long*)(buf + 8) + *(long*)(buf + 16);
    }

    public static void Dispose() { } // no handles to release
}
