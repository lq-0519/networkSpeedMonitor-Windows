using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace NetworkSpeedMonitor;

internal sealed class Counters : IDisposable
{
    private IntPtr query;
    private readonly Dictionary<string,IntPtr> handles=new();
    [StructLayout(LayoutKind.Explicit,Size=16)] private struct Value { [FieldOffset(0)] public uint Status; [FieldOffset(8)] public double Number; }
    [StructLayout(LayoutKind.Sequential)] private struct Item { public IntPtr Name; public Value Value; }
    [DllImport("pdh",CharSet=CharSet.Unicode)] private static extern uint PdhOpenQuery(string? source,IntPtr data,out IntPtr query);
    [DllImport("pdh",CharSet=CharSet.Unicode)] private static extern uint PdhAddEnglishCounter(IntPtr query,string path,IntPtr data,out IntPtr counter);
    [DllImport("pdh")] private static extern uint PdhCollectQueryData(IntPtr query);
    [DllImport("pdh")] private static extern uint PdhGetFormattedCounterValue(IntPtr counter,uint format,out uint type,out Value value);
    [DllImport("pdh",CharSet=CharSet.Unicode)] private static extern uint PdhGetFormattedCounterArray(IntPtr counter,uint format,ref uint size,out uint count,IntPtr buffer);
    [DllImport("pdh")] private static extern uint PdhCloseQuery(IntPtr query);
    public Counters()
    {
        if(PdhOpenQuery(null,IntPtr.Zero,out query)!=0) return;
        Add("read",@"\PhysicalDisk(_Total)\Disk Read Bytes/sec"); Add("write",@"\PhysicalDisk(_Total)\Disk Write Bytes/sec");
        Add("gpu",@"\GPU Engine(*)\Utilization Percentage"); Add("vram",@"\GPU Adapter Memory(*)\Dedicated Usage");
        PdhCollectQueryData(query);
    }
    private void Add(string name,string path) { if(PdhAddEnglishCounter(query,path,IntPtr.Zero,out var h)==0)handles[name]=h; }
    public void Collect() { if(query!=IntPtr.Zero)PdhCollectQueryData(query); }
    public double? Read(string name)
    {
        if(!handles.TryGetValue(name,out var h)||PdhGetFormattedCounterValue(h,0x200,out _,out var v)!=0||v.Status>1||!double.IsFinite(v.Number))return null;
        return Math.Max(0,v.Number);
    }
    private List<(string Name,double Number)> Array(string name)
    {
        var result=new List<(string,double)>(); if(!handles.TryGetValue(name,out var h))return result;
        uint size=0; PdhGetFormattedCounterArray(h,0x200,ref size,out _,IntPtr.Zero); if(size==0||size>16_000_000)return result;
        var memory=Marshal.AllocHGlobal((int)size);
        try { if(PdhGetFormattedCounterArray(h,0x200,ref size,out var count,memory)!=0)return result;
            int stride=Marshal.SizeOf<Item>();for(int i=0;i<count;i++){var item=Marshal.PtrToStructure<Item>(memory+i*stride); if(item.Value.Status<=1&&double.IsFinite(item.Value.Number))result.Add((Marshal.PtrToStringUni(item.Name)??"",Math.Max(0,item.Value.Number)));}
        } finally {Marshal.FreeHGlobal(memory);} return result;
    }
    // GPU engine usage is summed by physical engine across processes, then the busiest engine is used.
    public double? Gpu() { var values=Array("gpu"); if(values.Count==0)return null; return Math.Clamp(values.GroupBy(x=>x.Name.Contains("_luid_")?x.Name[(x.Name.IndexOf("_luid_",StringComparison.Ordinal)+1)..]:x.Name).Max(g=>g.Sum(x=>x.Number)),0,100); }
    public double? Vram() { var values=Array("vram"); return values.Count==0?null:values.Max(v=>v.Number); }
    public void Dispose(){if(query!=IntPtr.Zero){PdhCloseQuery(query);query=IntPtr.Zero;}}
}
