using System.Runtime.InteropServices;

// Network throughput via GetIfTable2.
// Sums InOctets and OutOctets separately across all active non-loopback interfaces.
// Primary value (History) = receive (download); secondary (History2) = send (upload).
// Uses 64-bit counters (GetIfTable2, not GetIfTable) to avoid overflow on fast links.
// FreeMibTable MUST be called after each GetIfTable2 call — it's kernel-allocated.

internal static unsafe class NetworkMonitor
{
    private static ulong _prevIn;
    private static ulong _prevOut;
    private static ulong _prevTick;
    private static float _sendPercent;

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

        double inBps  = deltaIn  * 1000.0 / deltaTick;
        double outBps = deltaOut * 1000.0 / deltaTick;

        long maxBps = Config.Monitors[Config.NETWORK].MaxBytesPerSec;
        if (maxBps <= 0) maxBps = 131_072_000L;

        CurrentReceiveMBps = (float)(inBps  / (1024 * 1024));
        CurrentSendMBps    = (float)(outBps / (1024 * 1024));

        _sendPercent = (float)Math.Clamp(outBps / maxBps * 100.0, 0.0, 100.0);
        return (float)Math.Clamp(inBps / maxBps * 100.0, 0.0, 100.0);
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
