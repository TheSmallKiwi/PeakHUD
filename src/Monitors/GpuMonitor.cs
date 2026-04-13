using System.Runtime.InteropServices;

// GPU utilization via D3DKMTQueryStatistics — the same API Task Manager uses.
// Vendor-neutral: works on NVIDIA, AMD, and Intel GPUs without driver SDKs.
// Queries engine running time delta per tick across all nodes on all adapters.
//
// Fallback: if D3DKMT enumeration fails (e.g., no GPU, virtualized environment),
// Read() returns 0 silently.

internal static unsafe class GpuMonitor
{
    private struct NodeState
    {
        public Win32.LUID AdapterLuid;
        public uint       NodeId;
        public ulong      PrevRunningTime;
    }

    private static NodeState[] _nodes = [];
    private static ulong       _prevTick;
    private static bool        _available;

    public static void Init()
    {
        _nodes     = EnumerateNodes();
        _available = _nodes.Length > 0;
        _prevTick  = Win32.GetTickCount64();

        // Warm-up: record initial running times
        for (int i = 0; i < _nodes.Length; i++)
            _nodes[i].PrevRunningTime = QueryRunningTime(ref _nodes[i]);
    }

    public static float Read()
    {
        if (!_available) return 0f;

        ulong now       = Win32.GetTickCount64();
        ulong deltaTick = now - _prevTick;
        _prevTick = now;
        if (deltaTick == 0) return 0f;

        // 100-ns ticks available in the interval (per node)
        ulong ticksPerNode = deltaTick * 10_000UL; // ms → 100-ns units

        ulong totalBusy     = 0;
        ulong totalCapacity = 0;

        for (int i = 0; i < _nodes.Length; i++)
        {
            ulong cur  = QueryRunningTime(ref _nodes[i]);
            ulong busy = cur >= _nodes[i].PrevRunningTime
                ? cur - _nodes[i].PrevRunningTime
                : 0;
            _nodes[i].PrevRunningTime = cur;

            totalBusy     += busy;
            totalCapacity += ticksPerNode;
        }

        if (totalCapacity == 0) return 0f;
        return (float)Math.Clamp((double)totalBusy / totalCapacity * 100.0, 0.0, 100.0);
    }

    // ── Internals ─────────────────────────────────────────────────────────────

    private static ulong QueryRunningTime(ref NodeState node)
    {
        var q = new Win32.D3DKMT_QUERYSTATISTICS
        {
            Type        = Win32.D3DKMT_QUERYSTATISTICS_NODE,
            AdapterLuid = node.AdapterLuid,
            QueryNode   = new Win32.D3DKMT_QUERYSTATISTICS_QUERY_NODE { NodeId = node.NodeId }
        };
        int hr = Win32.D3DKMTQueryStatistics(ref q);
        return hr == 0 ? q.Result.NodeInformation.RunningTime : 0;
    }

    private static NodeState[] EnumerateNodes()
    {
        // First call: get adapter count
        var e = new Win32.D3DKMT_ENUMADAPTERS2 { NumAdapters = 0, pAdapters = null };
        Win32.D3DKMTEnumAdapters2(&e);
        if (e.NumAdapters == 0) return [];

        // Second call: fill adapter array
        var adapters = new Win32.D3DKMT_ADAPTERINFO[e.NumAdapters];
        fixed (Win32.D3DKMT_ADAPTERINFO* p = adapters)
        {
            e.pAdapters = p;
            if (Win32.D3DKMTEnumAdapters2(&e) != 0) return [];
        }

        // For each adapter, probe nodes 0..63 until QueryStatistics fails
        var nodes = new System.Collections.Generic.List<NodeState>();
        foreach (var adapter in adapters)
        {
            for (uint nodeId = 0; nodeId < 64; nodeId++)
            {
                var q = new Win32.D3DKMT_QUERYSTATISTICS
                {
                    Type        = Win32.D3DKMT_QUERYSTATISTICS_NODE,
                    AdapterLuid = adapter.AdapterLuid,
                    QueryNode   = new Win32.D3DKMT_QUERYSTATISTICS_QUERY_NODE { NodeId = nodeId }
                };
                if (Win32.D3DKMTQueryStatistics(ref q) != 0) break;

                nodes.Add(new NodeState
                {
                    AdapterLuid = adapter.AdapterLuid,
                    NodeId      = nodeId
                });
            }
        }

        return [.. nodes];
    }
}
