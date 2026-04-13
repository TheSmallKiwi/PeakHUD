using System.Runtime.InteropServices;

// Network throughput via GetIfTable2.
// Sums InOctets + OutOctets across all active non-loopback interfaces.
// Uses 64-bit counters (GetIfTable2, not GetIfTable) to avoid overflow on fast links.
// FreeMibTable MUST be called after each GetIfTable2 call — it's kernel-allocated.

internal static unsafe class NetworkMonitor
{
    private static ulong _prevBytes;
    private static ulong _prevTick;

    public static void Init()
    {
        _prevBytes = SumBytes();
        _prevTick  = Win32.GetTickCount64();
    }

    public static float Read()
    {
        ulong now       = Win32.GetTickCount64();
        ulong curBytes  = SumBytes();
        ulong deltaTick = now - _prevTick;

        ulong deltaBytes = curBytes >= _prevBytes ? curBytes - _prevBytes : 0;
        _prevBytes = curBytes;
        _prevTick  = now;

        if (deltaTick == 0) return 0f;

        double bytesPerSec = deltaBytes * 1000.0 / deltaTick;
        long   maxBps      = Config.Monitors[Config.NETWORK].MaxBytesPerSec;
        if (maxBps <= 0) maxBps = 131_072_000L;

        return (float)Math.Clamp(bytesPerSec / maxBps * 100.0, 0.0, 100.0);
    }

    private static ulong SumBytes()
    {
        if (Win32.GetIfTable2(out nint pTable) != 0) return 0;

        ulong total   = 0;
        uint  count   = (uint)Marshal.ReadInt32(pTable);   // MIB_IF_TABLE2.NumEntries
        int   rowSize = sizeof(Win32.MIB_IF_ROW2);
        nint  rowPtr  = pTable + sizeof(uint) + (sizeof(uint) * 0); // align after NumEntries

        // NumEntries is followed by the array; struct alignment pads to 8 bytes
        // The real offset of Table[] is sizeof(ULONG) padded to pointer-size = 8 bytes on x64
        rowPtr = pTable + 8;

        for (uint i = 0; i < count; i++)
        {
            var row = (Win32.MIB_IF_ROW2*)rowPtr;
            if (row->Type != Win32.IF_TYPE_SOFTWARE_LOOPBACK &&
                row->OperStatus == Win32.IfOperStatusUp)
            {
                total += row->InOctets + row->OutOctets;
            }
            rowPtr += rowSize;
        }

        Win32.FreeMibTable(pTable);
        return total;
    }
}
