using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;

namespace NetworkSpeedMonitor;

internal sealed class RateCounter
{
    private (ulong Value,double Time)? previous;
    public double? Update(ulong value,double time,double maximumGap=20)
    {
        var old=previous; previous=(value,time);
        if(old is not {} p||value<p.Value||time<=p.Time||time-p.Time>maximumGap)return null;
        return (value-p.Value)/(time-p.Time);
    }
    public void Reset()=>previous=null;
}

internal sealed class Sampler : IDisposable
{
    private readonly Settings settings;
    private readonly Counters counters=new();
    private readonly Dictionary<string,(RateCounter Down,RateCounter Up)> network=new();
    private readonly Dictionary<int,(long Start,long Ticks,double Time,string Path)> previousProcesses=new();
    private (long Idle,long Total)? previousCpu;
    private string segment=Guid.NewGuid().ToString("N"), previousNetwork="";
    private double lastTime, lastDrives=-100;
    private List<Native.InterfaceRow> selectedInterfaces=new();
    private string selectedNetwork="";
    private double nextInterfaceDiscovery;
    private volatile bool networkChanged=true;
    private IReadOnlyList<(string Name,long Total,long Free)> drives=Array.Empty<(string,long,long)>();
    public Sampler(Settings settings){this.settings=settings;NetworkChange.NetworkAddressChanged+=AddressChanged;NetworkChange.NetworkAvailabilityChanged+=AvailabilityChanged;}
    private void AddressChanged(object? sender,EventArgs e)=>networkChanged=true;
    private void AvailabilityChanged(object? sender,NetworkAvailabilityEventArgs e)=>networkChanged=true;
    public void Reset(){previousCpu=null;network.Clear();previousProcesses.Clear();segment=Guid.NewGuid().ToString("N");lastTime=0;networkChanged=true;}
    private List<Native.InterfaceRow> ActiveInterfaces(double now)
    {
        if(networkChanged||selectedNetwork!=settings.NetworkId||now>=nextInterfaceDiscovery)
        {
            networkChanged=false;selectedNetwork=settings.NetworkId;
            selectedInterfaces=Interfaces().Where(n=>selectedNetwork.Length==0?DefaultAdapter(n):n.Guid.ToString().Equals(selectedNetwork.Trim('{','}'),StringComparison.OrdinalIgnoreCase)).ToList();
            nextInterfaceDiscovery=now+(selectedInterfaces.Count==0?5:30);
        }
        var active=new List<Native.InterfaceRow>(selectedInterfaces.Count);
        foreach(var known in selectedInterfaces)
        {
            var current=known;
            if(Native.GetIfEntry2(ref current)!=0){networkChanged=true;continue;}
            if(current.OperStatus==1)active.Add(current);
        }
        return active;
    }
    private static List<Native.InterfaceRow> Interfaces()
    {
        var rows=new List<Native.InterfaceRow>();if(Native.GetIfTable2(out var table)!=0)return rows;
        try{int count=Marshal.ReadInt32(table);int stride=Marshal.SizeOf<Native.InterfaceRow>();for(int i=0;i<Math.Min(count,4096);i++)rows.Add(Marshal.PtrToStructure<Native.InterfaceRow>(table+8+i*stride));}finally{Native.FreeMibTable(table);}return rows;
    }
    public static List<(string Id,string Name)> Adapters()=>Interfaces().Where(n=>n.Type!=24&&!n.Description.Contains("Filter",StringComparison.OrdinalIgnoreCase)&&!n.Description.Contains("Scheduler",StringComparison.OrdinalIgnoreCase)).Select(n=>(n.Guid.ToString(),n.Alias+" · "+n.Description)).DistinctBy(n=>n.Item1).ToList();
    private static bool DefaultAdapter(Native.InterfaceRow n)
    {
        if(n.Type is not (6 or 71)||(n.Flags&1)==0)return false;
        string name=(n.Alias+" "+n.Description).ToLowerInvariant();
        return !new[]{"virtual","tap-","tunnel","clash","vpn","loopback","hyper-v","wsl","bluetooth","xbox","vmware","virtualbox","tailscale","wireguard","filter","scheduler","npcap"}.Any(name.Contains);
    }
    public Snapshot Read(bool details,AdvancedFrame? advanced)
    {
        double now=Stopwatch.GetTimestamp()/(double)Stopwatch.Frequency;
        if(lastTime>0 && now-lastTime>Math.Max(20,settings.Interval*4))Reset();
        lastTime=now;
        double? cpu=null;
        if(Native.GetSystemTimes(out long idle,out long kernel,out long user))
        {
            long total=kernel+user;
            if(previousCpu is {} old && total>old.Total && idle>=old.Idle)cpu=Math.Clamp(100.0*(total-old.Total-(idle-old.Idle))/(total-old.Total),0,100);
            previousCpu=(idle,total);
        } else previousCpu=null;
        var memory=new Native.MemoryStatus{Length=(uint)Marshal.SizeOf<Native.MemoryStatus>()};
        bool memoryOk=Native.GlobalMemoryStatusEx(ref memory);
        counters.Collect();
        double? download=null,upload=null; string adapterName="未连接";
        try
        {
            var active=ActiveInterfaces(now);
            string key=string.Join("|",active.Select(n=>n.Guid).Order());
            if(key!=previousNetwork){network.Clear();previousNetwork=key;segment=Guid.NewGuid().ToString("N");}
            bool valid=active.Count>0;double down=0,up=0;
            foreach(var n in active)
            {
                string id=n.Guid.ToString();if(!network.TryGetValue(id,out var rates))network[id]=rates=(new(),new());
                var d=rates.Down.Update(n.InOctets,now);var u=rates.Up.Update(n.OutOctets,now);
                if(d is null||u is null)valid=false;else {down+=d.Value;up+=u.Value;}
            }
            if(valid){download=down;upload=up;} adapterName=active.Count==0?"未连接":string.Join(" + ",active.Select(n=>n.Alias));
        }catch(Exception ex){Paths.Log("Network: "+ex.Message);}
        if(now-lastDrives>=10)
        {
            lastDrives=now;var list=new List<(string,long,long)>();
            foreach(var drive in DriveInfo.GetDrives())try{if(drive.IsReady&&drive.DriveType==DriveType.Fixed)list.Add((drive.Name,drive.TotalSize,drive.TotalFreeSpace));}catch{}
            drives=list;
        }
        var sensors=advanced is not null && DateTimeOffset.UtcNow-advanced.Time<TimeSpan.FromSeconds(12)?advanced.Sensors:new List<SensorReading>();
        double? Sensor(string hardware,string kind,string name)=>sensors.Where(s=>s.Hardware.Contains(hardware,StringComparison.OrdinalIgnoreCase)&&s.Kind==kind&&s.Name.Contains(name,StringComparison.OrdinalIgnoreCase)).Select(s=>(double?)s.Value).FirstOrDefault();
        var sample=new Sample(DateTimeOffset.UtcNow,segment,cpu,memoryOk?memory.Total-memory.Available:null,download,upload,counters.Read("read"),counters.Read("write"),counters.Gpu(),counters.Vram(),Sensor("Cpu","Temperature","Package")??Sensor("Cpu","Temperature","Core"),Sensor("Gpu","Temperature","Core"),Sensor("Cpu","Power","Package"),Sensor("Gpu","Power","Package")??Sensor("Gpu","Power","Core"));
        var processes=details?ReadProcesses(now):new List<ProcessRow>();
        if(!details)previousProcesses.Clear();
        return new(sample,memory.Total,memory.Available,memory.PageTotal-memory.PageAvailable,memory.PageTotal,adapterName,processes,drives,sensors,advanced,"");
    }
    private List<ProcessRow> ReadProcesses(double now)
    {
        var result=new List<ProcessRow>();var next=new Dictionary<int,(long,long,double,string)>();
        var handle=Native.CreateToolhelp32Snapshot(2,0);if(handle==new IntPtr(-1))return result;
        try
        {
            var e=new Native.ProcessEntry{Size=(uint)Marshal.SizeOf<Native.ProcessEntry>(),Exe=""};
            if(!Native.Process32FirstW(handle,ref e))return result;
            do
            {
                int pid=(int)e.Pid;if(pid==0)continue;
                var process=Native.OpenProcess(0x1000,false,pid);if(process==IntPtr.Zero)continue;
                try
                {
                    if(!Native.GetProcessTimes(process,out long started,out _,out long kernel,out long user))continue;
                    long ticks=kernel+user;double? percent=null; string path="";
                    if(previousProcesses.TryGetValue(pid,out var old)&&old.Start==started)
                    {
                        path=old.Path;double seconds=now-old.Time;
                        if(ticks>=old.Ticks&&seconds>0&&seconds<=20)percent=Math.Clamp((ticks-old.Ticks)/10_000_000.0/seconds/Environment.ProcessorCount*100,0,100);
                    }
                    if(path.Length==0){var buffer=new StringBuilder(32768);int length=buffer.Capacity;if(Native.QueryFullProcessImageName(process,0,buffer,ref length))path=buffer.ToString();}
                    var m=new Native.ProcessMemory{Size=(uint)Marshal.SizeOf<Native.ProcessMemory>()};ulong bytes=0;
                    if(Native.GetProcessMemoryInfo(process,ref m,m.Size))bytes=m.PrivateWorkingSet.ToUInt64();
                    next[pid]=(started,ticks,now,path);
                    string name=Path.GetFileNameWithoutExtension(e.Exe);result.Add(new(pid,(int)e.Parent,started,name,path,percent,bytes));
                }finally{Native.CloseHandle(process);}
            }while(Native.Process32NextW(handle,ref e));
        }finally{Native.CloseHandle(handle);}
        previousProcesses.Clear();foreach(var pair in next)previousProcesses[pair.Key]=pair.Value;
        return result;
    }
    public void Dispose(){NetworkChange.NetworkAddressChanged-=AddressChanged;NetworkChange.NetworkAvailabilityChanged-=AvailabilityChanged;counters.Dispose();}
}
