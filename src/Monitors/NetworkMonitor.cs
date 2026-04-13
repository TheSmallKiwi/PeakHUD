using System.Runtime.InteropServices;

// Network throughput via GetIfTable2.
// Sums InOctets and OutOctets separately across all active non-loopback interfaces.
// Primary value (History) = receive (download); secondary (History2) = send (upload).
// Uses 64-bit counters (GetIfTable2, not GetIfTable) to avoid overflow on fast links.
// FreeMibTable MUST be called after each GetIfTable2 call — it's kernel-allocated.
// Scale uses a rolling maximum (same approach as DiskMonitor) so small bursts remain
// visible — the ceiling decays at ~1% per second back to the 1 MB/s baseline floor.

internal static unsafe class NetworkMonitor
{
    // 1 MB/s floor — bars never peg at 100% from minor idle traffic.
    private const double BaselineBps = 1.0 * 1024 * 1024;

    private static ulong  _prevIn;
    private static ulong  _prevOut;
    private static ulong  _prevTick;
    private static float  _sendPercent;
    private static double _rollingMaxBps = BaselineBps;

    // Exposed for the popup label (MB/s, updated each Read() call).
    public static float CurrentReceiveMBps;
    public static float CurrentSendMBps;

    public static void Init()
    {
        SumInOut(out _prevIn, out _prevOut);
        _prevTick = Win32.GetTickCount64();
    }

    public static float Read()
    {
        ulong now = Win32.GetTickCount64();
        SumInOut(out ulong curIn, out ulong curOut);
        ulong deltaTick = now - _prevTick;

        ulong deltaIn  = curIn  >= _prevIn  ? curIn  - _prevIn  : 0;
        ulong deltaOut = curOut >= _prevOut ? curOut - _prevOut : 0;
        _prevIn   = curIn;
        _prevOut  = curOut;
        _prevTick = now;

        if (deltaTick == 0) { _sendPercent = 0f; return 0f; }

        double inBps    = deltaIn  * 1000.0 / deltaTick;
        double outBps   = deltaOut * 1000.0 / deltaTick;
        double totalBps = inBps + outBps;

        CurrentReceiveMBps = (float)(inBps  / (1024 * 1024));
        CurrentSendMBps    = (float)(outBps / (1024 * 1024));

        // Rolling max decays at ~1% per second; floor at baseline so minor idle
        // traffic never pegs the bars.
        double decayFactor = Math.Pow(0.99, deltaTick / 1000.0);
        _rollingMaxBps = Math.Max(_rollingMaxBps * decayFactor, BaselineBps);
        _rollingMaxBps = Math.Max(_rollingMaxBps, totalBps);

        _sendPercent = (float)Math.Clamp(outBps / _rollingMaxBps * 100.0, 0.0, 100.0);
        return (float)Math.Clamp(inBps / _rollingMaxBps * 100.0, 0.0, 100.0);
    }

    // Secondary value for the send bar (History2). Call after Read().
    public static float ReadSecondary() => _sendPercent;

    private static void SumInOut(out ulong inBytes, out ulong outBytes)
    {
        inBytes = outBytes = 0;
        if (Win32.GetIfTable2(out nint pTable) != 0) return;

        uint  count   = (uint)Marshal.ReadInt32(pTable);
        int   rowSize = sizeof(Win32.MIB_IF_ROW2);
        nint  rowPtr  = pTable + 8; // Table[] is at offset 8 on x64 (ULONG padded to 8)

        for (uint i = 0; i < count; i++)
        {
            var row = (Win32.MIB_IF_ROW2*)rowPtr;
            if (row->Type != Win32.IF_TYPE_SOFTWARE_LOOPBACK &&
                row->OperStatus == Win32.IfOperStatusUp)
            {
                inBytes  += row->InOctets;
                outBytes += row->OutOctets;
            }
            rowPtr += rowSize;
        }

        Win32.FreeMibTable(pTable);
    }
}
