using System.Runtime.InteropServices;

// GPU utilization + system-wide VRAM via D3DKMTQueryStatistics (gdi32.dll) — same API
// Task Manager and System Informer use. Struct layout per ProcessHacker's d3dkmt.h.
//
// VRAM: sum BytesResident across all segments where Aperture == 0 (dedicated VRAM only).
//       Capacity: sum CommitLimit across the same segments.
// Util: sum RunningTime deltas across all nodes of all adapters, divided by elapsed ticks.

internal static unsafe class GpuMonitor
{
    private struct NodeState
    {
        public Win32.LUID AdapterLuid;
        public uint       NodeId;
        public ulong      PrevRunningTime;
    }

    private struct SegmentState
    {
        public Win32.LUID AdapterLuid;
        public uint       SegmentId;
    }

    private static NodeState[]    _nodes             = [];
    private static SegmentState[] _segments          = [];
    private static ulong          _dedicatedCapacity;
    private static ulong          _prevTick;
    private static bool           _available;

    public static void Init()
    {
        var adapters = EnumerateAdapters();
        _segments          = EnumerateSegments(adapters, out _dedicatedCapacity);
        _nodes             = EnumerateNodes(adapters);
        _available         = _nodes.Length > 0;
        _prevTick          = Win32.GetTickCount64();

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

        // Capacity per engine over the elapsed interval (100-ns ticks).
        ulong capacity = deltaTick * 10_000UL;
        if (capacity == 0) return 0f;

        // Task Manager shows the MAX busy engine, not the average. A GPU with
        // 16 nodes (3D / Copy / Video Decode / Video Encode / Compute / ...)
        // where only the 3D engine is pegged would otherwise read as ~6%.
        double maxPct = 0;
        for (int i = 0; i < _nodes.Length; i++)
        {
            ulong cur  = QueryRunningTime(ref _nodes[i]);
            ulong busy = cur >= _nodes[i].PrevRunningTime
                ? cur - _nodes[i].PrevRunningTime
                : 0;
            _nodes[i].PrevRunningTime = cur;

            double pct = (double)busy / capacity * 100.0;
            if (pct > maxPct) maxPct = pct;
        }

        return (float)Math.Clamp(maxPct, 0.0, 100.0);
    }

    public static float ReadMemory()
    {
        if (_segments.Length == 0 || _dedicatedCapacity == 0) return 0f;

        ulong totalResident = 0;
        for (int i = 0; i < _segments.Length; i++)
            totalResident += QueryBytesResident(ref _segments[i]);

        return (float)Math.Clamp((double)totalResident / _dedicatedCapacity * 100.0, 0.0, 100.0);
    }

    // ── Internals ─────────────────────────────────────────────────────────────

    // Writes `id` to every candidate trailing-union offset in the struct. One of these
    // lands where Windows expects the input; the rest are harmless writes into
    // AdapterInformation's reserved tail (never read for SEGMENT/NODE queries).
    private static void SetQueryId(ref Win32.D3DKMT_QUERYSTATISTICS q, uint id)
    {
        q.QueryId_776 = id; q.QueryId_780 = id; q.QueryId_784 = id; q.QueryId_788 = id;
        q.QueryId_792 = id; q.QueryId_796 = id; q.QueryId_800 = id; q.QueryId_804 = id;
        q.QueryId_808 = id; q.QueryId_812 = id; q.QueryId_816 = id; q.QueryId_820 = id;
        q.QueryId_824 = id; q.QueryId_828 = id; q.QueryId_832 = id; q.QueryId_836 = id;
        q.QueryId_840 = id; q.QueryId_844 = id; q.QueryId_848 = id; q.QueryId_852 = id;
        q.QueryId_856 = id; q.QueryId_860 = id; q.QueryId_864 = id; q.QueryId_868 = id;
        q.QueryId_872 = id; q.QueryId_876 = id; q.QueryId_880 = id;
    }

    private static ulong QueryRunningTime(ref NodeState node)
    {
        var q = new Win32.D3DKMT_QUERYSTATISTICS
        {
            Type        = Win32.D3DKMT_QUERYSTATISTICS_NODE,
            AdapterLuid = node.AdapterLuid,
        };
        SetQueryId(ref q, node.NodeId);
        return Win32.D3DKMTQueryStatistics(ref q) == 0 ? q.NodeRunningTime : 0;
    }

    private static ulong QueryBytesResident(ref SegmentState seg)
    {
        var q = new Win32.D3DKMT_QUERYSTATISTICS
        {
            Type        = Win32.D3DKMT_QUERYSTATISTICS_SEGMENT,
            AdapterLuid = seg.AdapterLuid,
        };
        SetQueryId(ref q, seg.SegmentId);
        return Win32.D3DKMTQueryStatistics(ref q) == 0 ? q.SegmentBytesResident : 0;
    }

    private static Win32.D3DKMT_ADAPTERINFO[] EnumerateAdapters()
    {
        var e = new Win32.D3DKMT_ENUMADAPTERS2 { NumAdapters = 0, pAdapters = null };
        Win32.D3DKMTEnumAdapters2(&e);
        if (e.NumAdapters == 0) return [];

        var adapters = new Win32.D3DKMT_ADAPTERINFO[e.NumAdapters];
        fixed (Win32.D3DKMT_ADAPTERINFO* p = adapters)
        {
            e.pAdapters = p;
            if (Win32.D3DKMTEnumAdapters2(&e) != 0) return [];
        }
        return adapters;
    }

    private static SegmentState[] EnumerateSegments(Win32.D3DKMT_ADAPTERINFO[] adapters,
                                                    out ulong dedicatedCapacity)
    {
        var   segments          = new System.Collections.Generic.List<SegmentState>();
        ulong totalDedicatedCap = 0;

        foreach (var adapter in adapters)
        {
            for (uint segId = 0; segId < 64; segId++)
            {
                var q = new Win32.D3DKMT_QUERYSTATISTICS
                {
                    Type        = Win32.D3DKMT_QUERYSTATISTICS_SEGMENT,
                    AdapterLuid = adapter.AdapterLuid,
                };
                SetQueryId(ref q, segId);
                if (Win32.D3DKMTQueryStatistics(ref q) != 0) break;

                ulong cl = q.SegmentCommitLimit;
                if (cl == 0) continue;              // phantom / non-existent
                if (q.SegmentAperture != 0) continue; // aperture / shared memory — skip

                totalDedicatedCap += cl;
                segments.Add(new SegmentState { AdapterLuid = adapter.AdapterLuid, SegmentId = segId });
            }
        }

        dedicatedCapacity = totalDedicatedCap;
        return [.. segments];
    }

    private static NodeState[] EnumerateNodes(Win32.D3DKMT_ADAPTERINFO[] adapters)
    {
        var nodes = new System.Collections.Generic.List<NodeState>();
        foreach (var adapter in adapters)
        {
            for (uint nodeId = 0; nodeId < 64; nodeId++)
            {
                var q = new Win32.D3DKMT_QUERYSTATISTICS
                {
                    Type        = Win32.D3DKMT_QUERYSTATISTICS_NODE,
                    AdapterLuid = adapter.AdapterLuid,
                };
                SetQueryId(ref q, nodeId);
                if (Win32.D3DKMTQueryStatistics(ref q) != 0) break;

                nodes.Add(new NodeState { AdapterLuid = adapter.AdapterLuid, NodeId = nodeId });
            }
        }
        return [.. nodes];
    }
}
