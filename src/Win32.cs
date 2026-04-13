using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

// All Win32 P/Invoke declarations and structs.
// Uses LibraryImport (not DllImport) for NativeAOT compatibility.
// All string parameters use StringMarshalling.Utf16 (= WCHAR / W-suffix APIs).

internal static partial class Win32
{
    // ── Constants ────────────────────────────────────────────────────────────

    // Window styles
    public const uint WS_OVERLAPPEDWINDOW = 0x00CF0000;
    public const uint WS_POPUP            = 0x80000000;
    public const uint WS_BORDER           = 0x00800000;
    public const uint WS_SYSMENU         = 0x00080000;
    public const uint WS_MINIMIZEBOX     = 0x00020000;
    public const uint WS_MINIMIZE        = 0x20000000;
    public const uint WS_CHILD           = 0x40000000;
    public const uint WS_VISIBLE         = 0x10000000;
    public const uint WS_TABSTOP         = 0x00010000;
    public const uint WS_GROUP           = 0x00020000;

    // Extended window styles
    public const uint WS_EX_TOOLWINDOW  = 0x00000080;
    public const uint WS_EX_TOPMOST     = 0x00000008;
    public const uint WS_EX_APPWINDOW   = 0x00040000;
    public const uint WS_EX_CLIENTEDGE  = 0x00000200;

    // Window class styles
    public const uint CS_HREDRAW = 0x0002;
    public const uint CS_VREDRAW = 0x0001;
    public const uint CS_DBLCLKS = 0x0008;

    // ShowWindow commands
    public const int SW_HIDE         = 0;
    public const int SW_MINIMIZE     = 6;
    public const int SW_SHOWMINIMIZED = 2;
    public const int SW_SHOW         = 5;
    public const int SW_RESTORE      = 9;

    // SetWindowPos flags
    public const uint SWP_NOSIZE       = 0x0001;
    public const uint SWP_NOMOVE       = 0x0002;
    public const uint SWP_NOZORDER     = 0x0004;
    public const uint SWP_SHOWWINDOW   = 0x0040;
    public const uint SWP_NOACTIVATE   = 0x0010;

    // Window messages
    public const uint WM_DESTROY       = 0x0002;
    public const uint WM_SIZE          = 0x0005;
    public const uint WM_ACTIVATE      = 0x0006;
    public const uint WM_SETFOCUS      = 0x0007;
    public const uint WM_KILLFOCUS     = 0x0008;
    public const uint WM_PAINT         = 0x000F;
    public const uint WM_CLOSE         = 0x0010;
    public const uint WM_SETFONT       = 0x0030;
    public const uint WM_TIMER         = 0x0113;
    public const uint WM_LBUTTONDOWN   = 0x0201;
    public const uint WM_COMMAND       = 0x0111;
    public const uint WM_SETICON       = 0x0080;
    public const uint WM_SYSCOMMAND    = 0x0112;
    public const uint WM_GETMINMAXINFO = 0x0024;
    public const uint WM_NCHITTEST     = 0x0084;

    // WM_ACTIVATE wParam values
    public const uint WA_INACTIVE = 0;
    public const uint WA_ACTIVE   = 1;

    // WM_SYSCOMMAND wParam values
    public const uint SC_RESTORE  = 0xF120;
    public const uint SC_MINIMIZE = 0xF020;
    public const uint SC_CLOSE    = 0xF060;

    // WM_SETICON / icon sizes
    public const nint ICON_SMALL = 0;
    public const nint ICON_BIG   = 1;

    // WM_COMMAND notification codes (HIWORD of wParam)
    public const int EN_CHANGE  = 0x0300;
    public const int BN_CLICKED = 0x0000;

    // Button styles
    public const uint BS_AUTOCHECKBOX = 0x00000003;

    // Static styles
    public const uint SS_CENTER = 0x00000001;

    // Edit styles
    public const uint ES_NUMBER = 0x2000;
    public const uint ES_CENTER = 0x0001;

// GetWindowLongPtr / SetWindowLongPtr index
    public const int GWLP_USERDATA = -21;
    public const int GWL_STYLE     = -16;

    // System metrics
    public const int SM_CXSCREEN = 0;
    public const int SM_CYSCREEN = 1;

    // Cursor
    public static readonly nint IDC_ARROW = (nint)32512;

    // Stock objects
    public const int NULL_BRUSH  = 5;
    public const int BLACK_BRUSH = 4;

    // SetBkMode
    public const int TRANSPARENT = 1;
    public const int OPAQUE      = 2;

    // CreateDIBSection
    public const uint DIB_RGB_COLORS = 0;
    public const uint BI_RGB         = 0;

    // Font weight
    public const int FW_NORMAL = 400;
    public const int FW_BOLD   = 700;

    // DrawText flags
    public const uint DT_CENTER    = 0x00000001;
    public const uint DT_VCENTER   = 0x00000004;
    public const uint DT_SINGLELINE = 0x00000020;
    public const uint DT_LEFT      = 0x00000000;

    // Hit test
    public const nint HTCLIENT  = 1;
    public const nint HTCAPTION = 2;

    // Colors
    public const int COLOR_WINDOW     = 5;
    public const int COLOR_HIGHLIGHT  = 13;
    public const int COLOR_WINDOWTEXT = 8;

    // WM_SIZE wParam
    public const uint SIZE_MINIMIZED = 1;

    // NCHITTEST passthrough (for draggable popup)
    public const uint WS_EX_LAYERED      = 0x00080000;
    public const uint WS_EX_TRANSPARENT  = 0x00000020;

    // DWM
    public const int DWMWA_USE_IMMERSIVE_DARK_MODE  = 20;
    public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    public const int DWMWA_SYSTEMBACKDROP_TYPE      = 38;

    // Disk I/O
    public const uint GENERIC_READ       = 0x80000000;
    public const uint FILE_SHARE_READ    = 0x00000001;
    public const uint FILE_SHARE_WRITE   = 0x00000002;
    public const uint OPEN_EXISTING      = 3;
    // CTL_CODE(FILE_DEVICE_DISK=7, 8, METHOD_BUFFERED=0, FILE_READ_ACCESS=1)
    // = (7<<16)|(1<<14)|(8<<2)|0 = 0x00074020
    public const uint IOCTL_DISK_PERFORMANCE = 0x00074020;

    // Network interface types / status
    public const uint IF_TYPE_SOFTWARE_LOOPBACK = 24;
    public const uint IfOperStatusUp            = 1;

    // Known folder
    public static readonly Guid FOLDERID_RoamingAppData = new("3EB685DB-65F9-4CF6-A03A-E3EF65729F3D");

    // Helper: pack COLORREF (BBGGRR layout for GDI)
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint RGB(byte r, byte g, byte b) => (uint)(r | (g << 8) | (b << 16));

    // Helper: split wParam into lo/hi word
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int LOWORD(nint v) => (int)(v & 0xFFFF);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int HIWORD(nint v) => (int)((v >> 16) & 0xFFFF);

    // ── Structs ───────────────────────────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct WNDCLASSEX
    {
        public uint   cbSize;
        public uint   style;
        public delegate* unmanaged[Stdcall]<nint, uint, nint, nint, nint> lpfnWndProc;
        public int    cbClsExtra;
        public int    cbWndExtra;
        public nint   hInstance;
        public nint   hIcon;
        public nint   hCursor;
        public nint   hbrBackground;
        public nint   lpszMenuName;   // null
        public char*  lpszClassName;
        public nint   hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSG
    {
        public nint   hwnd;
        public uint   message;
        public nint   wParam;
        public nint   lParam;
        public uint   time;
        public POINT  pt;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PAINTSTRUCT
    {
        public nint   hdc;
        public int    fErase;
        public RECT   rcPaint;
        public int    fRestore;
        public int    fIncUpdate;
        public unsafe fixed byte rgbReserved[32];
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
        public int Width  => Right  - Left;
        public int Height => Bottom - Top;
        public RECT(int l, int t, int r, int b) { Left=l; Top=t; Right=r; Bottom=b; }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X, Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct FILETIME
    {
        public uint Low, High;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MEMORYSTATUSEX
    {
        public uint  dwLength;
        public uint  dwMemoryLoad;       // 0–100
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;

        public static MEMORYSTATUSEX Create()
        {
            var s = new MEMORYSTATUSEX();
            s.dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>();
            return s;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFOHEADER
    {
        public uint  biSize;
        public int   biWidth;
        public int   biHeight;   // negative = top-down
        public ushort biPlanes;
        public ushort biBitCount;
        public uint  biCompression;
        public uint  biSizeImage;
        public int   biXPelsPerMeter;
        public int   biYPelsPerMeter;
        public uint  biClrUsed;
        public uint  biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFO
    {
        public BITMAPINFOHEADER bmiHeader;
        public uint             bmiColors; // single RGBQUAD, unused for 32bpp BI_RGB
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ICONINFO
    {
        public int  fIcon;       // 1 = icon, 0 = cursor
        public uint xHotspot;
        public uint yHotspot;
        public nint hbmMask;    // monochrome mask bitmap
        public nint hbmColor;   // color bitmap
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DISK_PERFORMANCE
    {
        public long  BytesRead;
        public long  BytesWritten;
        public long  ReadTime;
        public long  WriteTime;
        public long  IdleTime;
        public uint  ReadCount;
        public uint  WriteCount;
        public uint  QueueDepth;
        public uint  SplitCount;
        public long  QueryTime;
        public uint  StorageDeviceNumber;
        public ulong StorageManagerName0;  // 8 WCHAR padding (16 bytes)
        public ulong StorageManagerName1;
    }

    // MIB_IF_ROW2 — simplified: only the fields we read (InOctets, OutOctets, Type, OperStatus)
    // Full struct is ~1.3 KB; we use fixed-size padding for the parts we skip.
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public unsafe struct MIB_IF_ROW2
    {
        public ulong InterfaceLuid;
        public uint  InterfaceIndex;
        public Guid  InterfaceGuid;
        public fixed char Alias[257];
        public fixed char Description[257];
        public uint  PhysicalAddressLength;
        public fixed byte PhysicalAddress[32];
        public fixed byte PermanentPhysicalAddress[32];
        public uint  Mtu;
        public uint  Type;           // IF_TYPE_SOFTWARE_LOOPBACK = 24
        public uint  TunnelType;
        public uint  MediaType;
        public uint  PhysicalMediumType;
        public uint  AccessType;
        public uint  DirectionType;
        public byte  InterfaceAndOperStatusFlags;
        public uint  OperStatus;     // IfOperStatusUp = 1
        public uint  AdminStatus;
        public uint  MediaConnectState;
        public Guid  NetworkGuid;
        public uint  ConnectionType;
        public ulong TransmitLinkSpeed;
        public ulong ReceiveLinkSpeed;
        public ulong InOctets;       // cumulative bytes received
        public ulong InUcastPkts;
        public ulong InNUcastPkts;
        public ulong InDiscards;
        public ulong InErrors;
        public ulong InUnknownProtos;
        public ulong InUcastOctets;
        public ulong InMulticastOctets;
        public ulong InBroadcastOctets;
        public ulong OutOctets;      // cumulative bytes sent
        public ulong OutUcastPkts;
        public ulong OutNUcastPkts;
        public ulong OutDiscards;
        public ulong OutErrors;
        public ulong OutUcastOctets;
        public ulong OutMulticastOctets;
        public ulong OutBroadcastOctets;
        public ulong OutQLen;
    }

    // D3DKMT structs for GPU utilization via gdi32.dll (same as Task Manager)
    [StructLayout(LayoutKind.Sequential)]
    public struct LUID { public uint LowPart; public int HighPart; }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct D3DKMT_ADAPTERINFO
    {
        public uint  hAdapter;
        public LUID  AdapterLuid;
        public uint  NumOfSources;
        public int   bPresentMoveRegionsPreferred;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct D3DKMT_ENUMADAPTERS2
    {
        public uint             NumAdapters;
        public D3DKMT_ADAPTERINFO* pAdapters;
    }

    // D3DKMT_QUERYSTATISTICS_TYPE enum values (d3dkmthk.h)
    public const uint D3DKMT_QUERYSTATISTICS_NODE = 5; // not 6 — 6 is PROCESS_NODE

    [StructLayout(LayoutKind.Sequential)]
    public struct D3DKMT_QUERYSTATISTICS_QUERY_NODE
    {
        public uint NodeId;
    }

    // Empirically confirmed via diagnostic dump: RunningTime is at offset 8 within the result.
    // Offset 8 held 0x000000116C197BCD (75B 100-ns ticks) while all other probed offsets were 0.
    [StructLayout(LayoutKind.Explicit)]
    public struct D3DKMT_QUERYSTATISTICS_NODE_INFORMATION
    {
        [FieldOffset(8)] public ulong RunningTime; // 100-ns ticks the engine was busy
    }

    // Minimal union — only the Node variant we use
    [StructLayout(LayoutKind.Explicit, Size = 2048)]
    public struct D3DKMT_QUERYSTATISTICS_RESULT
    {
        [FieldOffset(0)] public D3DKMT_QUERYSTATISTICS_NODE_INFORMATION NodeInformation;
    }

    // No Padding field — the C struct has Type(4) + LUID(8) + QueryNode(4) + Result(2048)
    // with Result naturally aligned at offset 16. An extra uint here shifts Result by 4.
    [StructLayout(LayoutKind.Sequential)]
    public struct D3DKMT_QUERYSTATISTICS
    {
        public uint  Type;
        public LUID  AdapterLuid;
        public D3DKMT_QUERYSTATISTICS_QUERY_NODE QueryNode;
        public D3DKMT_QUERYSTATISTICS_RESULT     Result;
    }

    // ── kernel32.dll ─────────────────────────────────────────────────────────

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetSystemTimes(out FILETIME idle, out FILETIME kernel, out FILETIME user);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint CreateFileW(string lpFileName, uint dwDesiredAccess,
        uint dwShareMode, nint lpSecurityAttributes, uint dwCreationDisposition,
        uint dwFlagsAndAttributes, nint hTemplateFile);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static unsafe partial bool DeviceIoControl(nint hDevice, uint dwIoControlCode,
        nint lpInBuffer, uint nInBufferSize,
        DISK_PERFORMANCE* lpOutBuffer, uint nOutBufferSize,
        out uint lpBytesReturned, nint lpOverlapped);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseHandle(nint hObject);

    [LibraryImport("kernel32.dll")]
    public static partial ulong GetTickCount64();

    [LibraryImport("kernel32.dll")]
    public static partial nint GetModuleHandleW(nint lpModuleName);  // null → current module

    // ── user32.dll ───────────────────────────────────────────────────────────

    [LibraryImport("user32.dll")]
    public static unsafe partial ushort RegisterClassExW(WNDCLASSEX* lpwcx);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint CreateWindowExW(uint dwExStyle, string lpClassName,
        string? lpWindowName, uint dwStyle,
        int X, int Y, int nWidth, int nHeight,
        nint hWndParent, nint hMenu, nint hInstance, nint lpParam);

    [LibraryImport("user32.dll")]
    public static partial nint DefWindowProcW(nint hWnd, uint msg, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyWindow(nint hWnd);

    [LibraryImport("user32.dll")]
    public static partial int GetMessageW(out MSG lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool TranslateMessage(in MSG lpMsg);

    [LibraryImport("user32.dll")]
    public static partial nint DispatchMessageW(in MSG lpMsg);

    [LibraryImport("user32.dll")]
    public static partial void PostQuitMessage(int nExitCode);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ShowWindow(nint hWnd, int nCmdShow);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetWindowPos(nint hWnd, nint hWndInsertAfter,
        int X, int Y, int cx, int cy, uint uFlags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetWindowRect(nint hWnd, out RECT lpRect);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetCursorPos(out POINT lpPoint);

    [LibraryImport("user32.dll")]
    public static partial int GetSystemMetrics(int nIndex);

    [LibraryImport("user32.dll")]
    public static partial nuint SetTimer(nint hWnd, nuint nIDEvent, uint uElapse, nint lpTimerFunc);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool KillTimer(nint hWnd, nuint uIDEvent);

    [LibraryImport("user32.dll")]
    public static partial nint SendMessageW(nint hWnd, uint Msg, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    public static partial nint BeginPaint(nint hWnd, out PAINTSTRUCT lpPaint);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool EndPaint(nint hWnd, in PAINTSTRUCT lpPaint);

    [LibraryImport("user32.dll")]
    public static partial int FillRect(nint hDC, in RECT lprc, nint hbr);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int DrawTextW(nint hdc, string lpchText, int cchText, ref RECT lprc, uint format);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool InvalidateRect(nint hWnd, nint lpRect, [MarshalAs(UnmanagedType.Bool)] bool bErase);

    [LibraryImport("user32.dll")]
    public static partial nint SetWindowLongPtrW(nint hWnd, int nIndex, nint dwNewLong);

    [LibraryImport("user32.dll")]
    public static partial nint GetWindowLongPtrW(nint hWnd, int nIndex);

    [LibraryImport("user32.dll")]
    public static partial nint LoadCursorW(nint hInstance, nint lpCursorName);

    [LibraryImport("user32.dll")]
    public static partial nint GetSysColorBrush(int nIndex);

    [LibraryImport("user32.dll")]
    public static partial uint GetSysColor(int nIndex);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetForegroundWindow(nint hWnd);


    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetWindowTextW(nint hWnd, string lpString);

    [LibraryImport("user32.dll")]
    public static unsafe partial int GetWindowTextW(nint hWnd, char* lpString, int nMaxCount);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetClientRect(nint hWnd, out RECT lpRect);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ScreenToClient(nint hWnd, ref POINT lpPoint);

    // ── gdi32.dll ────────────────────────────────────────────────────────────

    [LibraryImport("gdi32.dll")]
    public static partial nint CreateCompatibleDC(nint hdc);

    [LibraryImport("gdi32.dll")]
    public static unsafe partial nint CreateDIBSection(nint hdc, BITMAPINFO* pbmi,
        uint usage, out nint ppvBits, nint hSection, uint offset);

    [LibraryImport("gdi32.dll")]
    public static partial nint CreateBitmap(int nWidth, int nHeight,
        uint nPlanes, uint nBitCount, nint lpBits);

    [LibraryImport("gdi32.dll")]
    public static partial nint SelectObject(nint hdc, nint h);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DeleteObject(nint ho);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DeleteDC(nint hdc);

    [LibraryImport("gdi32.dll")]
    public static partial nint CreateSolidBrush(uint color);

    [LibraryImport("gdi32.dll")]
    public static partial nint GetStockObject(int i);

    [LibraryImport("gdi32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint CreateFontW(int cHeight, int cWidth, int cEscapement,
        int cOrientation, int cWeight, uint bItalic, uint bUnderline, uint bStrikeOut,
        uint iCharSet, uint iOutPrecision, uint iClipPrecision, uint iQuality,
        uint iPitchAndFamily, string pszFaceName);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static unsafe partial bool TextOutW(nint hdc, int x, int y, char* lpString, int c);

    [LibraryImport("gdi32.dll")]
    public static partial int SetBkMode(nint hdc, int mode);

    [LibraryImport("gdi32.dll")]
    public static partial uint SetTextColor(nint hdc, uint color);

    [LibraryImport("gdi32.dll")]
    public static partial uint SetBkColor(nint hdc, uint color);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool BitBlt(nint hdc, int x, int y, int cx, int cy,
        nint hdcSrc, int x1, int y1, uint rop);

    [LibraryImport("user32.dll")]
    public static partial nint CreateIconIndirect(ref ICONINFO piconinfo);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyIcon(nint hIcon);

    // ── iphlpapi.dll ─────────────────────────────────────────────────────────

    [LibraryImport("iphlpapi.dll")]
    public static partial uint GetIfTable2(out nint pTable);  // MIB_IF_TABLE2*

    [LibraryImport("iphlpapi.dll")]
    public static partial void FreeMibTable(nint pIpTable);

    // ── dwmapi.dll ───────────────────────────────────────────────────────────

    [LibraryImport("dwmapi.dll")]
    public static partial int DwmSetWindowAttribute(nint hwnd, int dwAttribute,
        ref int pvAttribute, int cbAttribute);

    public static void ApplyWin11Theme(nint hwnd)
    {
        int v;
        v = 1; DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE,  ref v, 4);
        v = 2; DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref v, 4);
        v = 2; DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE,      ref v, 4);
    }

    // ── gdi32.dll — D3DKMT (GPU utilization) ─────────────────────────────────

    [LibraryImport("gdi32.dll")]
    public static unsafe partial int D3DKMTEnumAdapters2(D3DKMT_ENUMADAPTERS2* pParams);

    [LibraryImport("gdi32.dll")]
    public static partial int D3DKMTQueryStatistics(ref D3DKMT_QUERYSTATISTICS pData);

    // ── shell32.dll ──────────────────────────────────────────────────────────

    [LibraryImport("shell32.dll")]
    public static partial int SHGetKnownFolderPath(in Guid rfid, uint dwFlags,
        nint hToken, out nint ppszPath);

    [LibraryImport("shell32.dll")]
    public static unsafe partial int SHGetPropertyStoreForWindow(nint hwnd, Guid* riid, out nint ppv);

    // Minimal PROPVARIANT for VT_LPWSTR (16 bytes on x64)
    [StructLayout(LayoutKind.Explicit, Size = 16)]
    public struct PROPVARIANT
    {
        [FieldOffset(0)] public ushort vt;      // VT_LPWSTR = 31
        [FieldOffset(8)] public nint   pwszVal; // pointer to wide string
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PROPERTYKEY { public Guid fmtid; public uint pid; }

    // Assigns a unique per-window AppUserModelID so Windows 11 shows
    // each icon window as a separate taskbar button instead of grouping them.
    // Must be called before ShowWindow.
    public static unsafe void SetWindowAppId(nint hwnd, string appId)
    {
        var iid = new Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"); // IID_IPropertyStore
        if (SHGetPropertyStoreForWindow(hwnd, &iid, out nint pStore) != 0) return;

        var key = new PROPERTYKEY
        {
            fmtid = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), // PKEY_AppUserModel_ID
            pid   = 5
        };

        fixed (char* pStr = appId)
        {
            var pv = new PROPVARIANT { vt = 31 /*VT_LPWSTR*/, pwszVal = (nint)pStr };

            // IPropertyStore vtable (IUnknown[0-2] + GetCount[3] + GetAt[4] + GetValue[5] + SetValue[6] + Commit[7])
            nint* vtbl = *(nint**)pStore;
            ((delegate* unmanaged[Stdcall]<nint, PROPERTYKEY*, PROPVARIANT*, int>)vtbl[6])(pStore, &key, &pv);
            ((delegate* unmanaged[Stdcall]<nint, int>)vtbl[7])(pStore);
        }

        // Release the IPropertyStore
        nint* vtbl2 = *(nint**)pStore;
        ((delegate* unmanaged[Stdcall]<nint, uint>)vtbl2[2])(pStore);
    }

    [LibraryImport("ole32.dll")]
    public static partial void CoTaskMemFree(nint pv);

    // ── ntdll.dll ─────────────────────────────────────────────────────────────

    // SystemPerformanceInformation (class 2) — available without elevation.
    // IoReadTransferCount  is at byte offset  8.
    // IoWriteTransferCount is at byte offset 16.
    [LibraryImport("ntdll.dll")]
    public static unsafe partial int NtQuerySystemInformation(
        int   SystemInformationClass,
        void* SystemInformation,
        uint  SystemInformationLength,
        out uint ReturnLength);
}
