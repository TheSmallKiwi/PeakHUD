// Physical disk I/O via PDH (Performance Data Helper).
// Uses \PhysicalDisk(_Total)\Disk Read Bytes/sec and \Disk Write Bytes/sec —
// the same counters Task Manager reads. These measure actual hardware transfers
// only, excluding file-cache hits, paging, and virtual memory operations.
// NtQuerySystemInformation(SystemPerformanceInformation) was previously used
// here but its IoReadTransferCount/IoWriteTransferCount fields count ALL logical
// I/O system-wide (cache, paging, etc.), producing values 50-100× higher than
// physical disk activity.

internal static unsafe class DiskMonitor
{
    // 30 MB/s floor — bars never pin at 100% from minor background activity.
    private const double BaselineBps = 30.0 * 1024 * 1024;

    private static nint _hQuery;
    private static nint _hReadCounter;
    private static nint _hWriteCounter;
    private static double _rollingMaxBps = BaselineBps;
    private static float  _writePercent;
    private static ulong  _prevTick;

    // Exposed for the popup label (MB/s, updated each Read() call).
    public static float CurrentReadMBps;
    public static float CurrentWriteMBps;

    public static void Init()
    {
        if (Win32.PdhOpenQueryW(null, 0, out _hQuery) != 0) return;
        Win32.PdhAddCounterW(_hQuery, @"\PhysicalDisk(_Total)\Disk Read Bytes/sec",  0, out _hReadCounter);
        Win32.PdhAddCounterW(_hQuery, @"\PhysicalDisk(_Total)\Disk Write Bytes/sec", 0, out _hWriteCounter);
        // Prime the counters — PDH rate counters need two collects before the
        // first valid reading; the first Read() call will produce the first real sample.
        Win32.PdhCollectQueryData(_hQuery);
        _prevTick = Win32.GetTickCount64();
    }

    public static float Read()
    {
        if (_hQuery == 0) return 0f;

        Win32.PdhCollectQueryData(_hQuery);

        double readBps  = GetCounterDouble(_hReadCounter);
        double writeBps = GetCounterDouble(_hWriteCounter);
        double totalBps = readBps + writeBps;

        CurrentReadMBps  = (float)(readBps  / (1024 * 1024));
        CurrentWriteMBps = (float)(writeBps / (1024 * 1024));

        // Rolling max decays at ~1% per second; floor at baseline so minor idle
        // traffic never pegs the bars.
        ulong now       = Win32.GetTickCount64();
        ulong deltaTick = now - _prevTick;
        _prevTick = now;

        double decayFactor = deltaTick > 0 ? Math.Pow(0.99, deltaTick / 1000.0) : 1.0;
        _rollingMaxBps = Math.Max(_rollingMaxBps * decayFactor, BaselineBps);
        _rollingMaxBps = Math.Max(_rollingMaxBps, totalBps);

        _writePercent = (float)Math.Clamp(writeBps / _rollingMaxBps * 100.0, 0.0, 100.0);
        return (float)Math.Clamp(readBps / _rollingMaxBps * 100.0, 0.0, 100.0);
    }

    // Secondary value for the write bar (History2). Call after Read().
    public static float ReadSecondary() => _writePercent;

    private static double GetCounterDouble(nint hCounter)
    {
        if (hCounter == 0) return 0.0;
        Win32.PDH_FMT_COUNTERVALUE val;
        uint status = Win32.PdhGetFormattedCounterValue(
            hCounter, Win32.PDH_FMT_DOUBLE, out _, &val);
        // PDH_CSTATUS_VALID_DATA = 0, PDH_CSTATUS_NEW_DATA = 1
        return (status == 0 && (val.CStatus == 0 || val.CStatus == 1)) ? val.doubleValue : 0.0;
    }

    public static void Dispose()
    {
        if (_hQuery != 0)
        {
            Win32.PdhCloseQuery(_hQuery);
            _hQuery = 0;
        }
    }
}
