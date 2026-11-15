using System;
using System.Runtime.InteropServices;
using System.Text;

namespace NetworkSpeedMonitor;

internal static class Native
{
    [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left,Top,Right,Bottom; public int Width=>Right-Left; public int Height=>Bottom-Top; }
    [StructLayout(LayoutKind.Sequential)] internal struct Point { public int X,Y; }
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] internal struct ProcessEntry { public uint Size,Usage,Pid; public UIntPtr Heap; public uint Module,Threads,Parent; public int Priority; public uint Flags; [MarshalAs(UnmanagedType.ByValTStr,SizeConst=260)] public string Exe; }
    [StructLayout(LayoutKind.Sequential)] internal struct MemoryStatus { public uint Length,Load; public ulong Total,Available,PageTotal,PageAvailable,VirtualTotal,VirtualAvailable,Extended; }
    [StructLayout(LayoutKind.Sequential)] internal struct ProcessMemory { public uint Size,PageFaults; public UIntPtr PeakWorkingSet,WorkingSet,PeakPaged,Paged,PeakNonPaged,NonPaged,Pagefile,PeakPagefile,Private,PrivateWorkingSet; public ulong Shared; }
    [StructLayout(LayoutKind.Sequential)] internal struct Paint { public IntPtr Hdc; public int Erase; public Rect Rect; public int Restore,IncUpdate; [MarshalAs(UnmanagedType.ByValArray,SizeConst=32)] public byte[] Reserved; }
    [StructLayout(LayoutKind.Sequential)] internal struct MonitorInfo { public uint Size; public Rect Monitor,Work; public uint Flags; }
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] internal struct InterfaceRow
    {
        public ulong Luid; public uint Index; public Guid Guid;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=257)] public string Alias;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=257)] public string Description;
        public uint AddressLength;
        [MarshalAs(UnmanagedType.ByValArray,SizeConst=32)] public byte[] Address;
        [MarshalAs(UnmanagedType.ByValArray,SizeConst=32)] public byte[] PermanentAddress;
        public uint Mtu,Type,TunnelType,MediaType,PhysicalMedium,AccessType,Direction;
        public byte Flags; public uint OperStatus,AdminStatus,MediaState; public Guid NetworkGuid; public uint ConnectionType;
        public ulong TransmitSpeed,ReceiveSpeed,InOctets,InUcastPackets,InNonUcastPackets,InDiscards,InErrors,InUnknown,InUcastOctets,InMulticastOctets,InBroadcastOctets,OutOctets,OutUcastPackets,OutNonUcastPackets,OutDiscards,OutErrors,OutUcastOctets,OutMulticastOctets,OutBroadcastOctets,OutQueueLength;
    }
    [DllImport("iphlpapi")] internal static extern uint GetIfTable2(out IntPtr table);
    [DllImport("iphlpapi")] internal static extern void FreeMibTable(IntPtr table);
    internal delegate bool EnumWindow(IntPtr window, IntPtr data);
    [DllImport("kernel32")] internal static extern bool GetSystemTimes(out long idle,out long kernel,out long user);
    [DllImport("kernel32")] internal static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
    [DllImport("kernel32",SetLastError=true)] internal static extern IntPtr OpenProcess(uint access,bool inherit,int pid);
    [DllImport("kernel32")] internal static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32")] internal static extern bool GetProcessTimes(IntPtr h,out long created,out long exit,out long kernel,out long user);
    [DllImport("kernel32",CharSet=CharSet.Unicode)] internal static extern bool QueryFullProcessImageName(IntPtr h,uint flags,StringBuilder path,ref int size);
    [DllImport("psapi")] internal static extern bool GetProcessMemoryInfo(IntPtr h,ref ProcessMemory memory,uint size);
    [DllImport("kernel32")] internal static extern IntPtr CreateToolhelp32Snapshot(uint flags,uint id);
    [DllImport("kernel32",CharSet=CharSet.Unicode)] internal static extern bool Process32FirstW(IntPtr h,ref ProcessEntry entry);
    [DllImport("kernel32",CharSet=CharSet.Unicode)] internal static extern bool Process32NextW(IntPtr h,ref ProcessEntry entry);
    [DllImport("user32",CharSet=CharSet.Unicode)] internal static extern IntPtr FindWindow(string? cls,string? title);
    [DllImport("user32",CharSet=CharSet.Unicode)] internal static extern IntPtr FindWindowEx(IntPtr parent,IntPtr after,string cls,string? title);
    [DllImport("user32")] internal static extern bool GetWindowRect(IntPtr h,out Rect rect);
    [DllImport("user32")] internal static extern bool IsWindow(IntPtr h);
    [DllImport("user32")] internal static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32")] internal static extern bool EnumChildWindows(IntPtr h,EnumWindow callback,IntPtr data);
    [DllImport("user32",CharSet=CharSet.Unicode)] internal static extern int GetClassName(IntPtr h,StringBuilder text,int count);
    [DllImport("user32")] internal static extern uint GetDpiForWindow(IntPtr h);
    [DllImport("user32")] internal static extern bool SetWindowPos(IntPtr h,IntPtr after,int x,int y,int w,int height,uint flags);
    [DllImport("user32")] internal static extern bool ShowWindow(IntPtr h,int cmd);
    [DllImport("user32")] internal static extern bool InvalidateRect(IntPtr h,IntPtr rect,bool erase);
    [DllImport("user32")] internal static extern IntPtr BeginPaint(IntPtr h,out Paint paint);
    [DllImport("user32")] internal static extern bool EndPaint(IntPtr h,ref Paint paint);
    [DllImport("user32")] internal static extern bool GetCursorPos(out Point p);
    [DllImport("user32")] internal static extern IntPtr GetForegroundWindow();
    [DllImport("user32")] internal static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32")] internal static extern short GetAsyncKeyState(int key);
    [DllImport("user32")] internal static extern IntPtr MonitorFromWindow(IntPtr h,uint flags);
    [DllImport("user32")] internal static extern bool GetMonitorInfo(IntPtr h,ref MonitorInfo info);
    [DllImport("user32")] internal static extern bool RegisterHotKey(IntPtr h,int id,uint modifiers,uint key);
    [DllImport("user32")] internal static extern bool UnregisterHotKey(IntPtr h,int id);
    [DllImport("user32",SetLastError=true)] internal static extern bool PostMessage(IntPtr h,uint message,IntPtr w,IntPtr l);
    [DllImport("user32")] internal static extern IntPtr RegisterPowerSettingNotification(IntPtr recipient,ref Guid setting,uint flags);
    [DllImport("user32")] internal static extern bool UnregisterPowerSettingNotification(IntPtr handle);
    [DllImport("dwmapi")] internal static extern int DwmSetWindowAttribute(IntPtr h,int attribute,ref int value,int size);
    public static Rect WorkArea(IntPtr h) { var m=new MonitorInfo{Size=(uint)Marshal.SizeOf<MonitorInfo>()}; GetMonitorInfo(MonitorFromWindow(h,2),ref m); return m.Work; }
}
