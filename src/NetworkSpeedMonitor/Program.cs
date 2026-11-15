using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NetworkSpeedMonitor;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            if(args.Length>=3&&args[0]=="--collector"){AdvancedBridge.RunHelper(args[1],int.Parse(args[2])).GetAwaiter().GetResult();return 0;}
            if(args.Contains("--self-test")){return Diagnostics.SelfTest(args.Last());}
            if(args.Length>=3&&args[0]=="--diagnose"){Diagnostics.Sample(int.Parse(args[1]),args[2]);return 0;}
            int command=args.Contains("--quit")?1:args.Contains("--status")?2:args.Contains("--settings")?3:args.Contains("--hide")?4:0;
            int detail=Array.IndexOf(args,"--detail");if(detail>=0&&detail+1<args.Length)command=10+Math.Max(0,Array.IndexOf(new[]{"network","cpu","memory","disk","gpu","sensors"},args[detail+1]));
            using var mutex=new Mutex(true,"Local\\NetworkSpeedMonitor.Windows",out bool first);
            if(!first||command==1||command==2||command==4){var window=Native.FindWindow(null,"NetworkSpeedMonitor · 系统概览");if(window!=IntPtr.Zero)Native.PostMessage(window,0x802A,new IntPtr(command),IntPtr.Zero);return 0;}
            return new MonitorApp(args).Run();
        }catch(Exception ex){Paths.Log(ex.ToString());return 1;}
    }
}

internal static class Diagnostics
{
    public static int SelfTest(string output)
    {
        int checks=0;void Check(bool condition,string label){if(!condition)throw new InvalidOperationException(label);checks++;}
        var counter=new RateCounter();Check(counter.Update(100,1)==null,"first sample must be unknown");Check(counter.Update(300,3)==100,"byte rate uses actual elapsed time");Check(counter.Update(1,4)==null,"counter rollover resets baseline");Check(counter.Update(501,100)==null,"resume must not bridge a long gap");Check(counter.Update(701,102)==100,"sampling recovers after gap");counter.Reset();Check(counter.Update(1000,103)==null,"reset must discard baseline");Check(Format.Size(double.NaN)=="—","invalid telemetry is unknown");Check(Format.Size(1536,true)=="1.5 KB/s","binary byte units");
        Check(System.Runtime.InteropServices.Marshal.SizeOf<Native.InterfaceRow>()==1352,"native interface table ABI size");
        Check(System.Runtime.InteropServices.Marshal.OffsetOf<Native.InterfaceRow>(nameof(Native.InterfaceRow.Flags)).ToInt32()==1152,"native hardware interface flag ABI offset");
        var s=new Sample(DateTimeOffset.UtcNow,Guid.NewGuid().ToString("N"),20,100,200,100,50,20,10,100,null,null,null,null);
        string path=Path.Combine(Path.GetTempPath(),"nsm-history-test-"+Guid.NewGuid().ToString("N")+".sqlite");
        using(var history=new History(path)){history.Append(s);history.Flush();Check(history.Error==null,"sqlite transaction");Check(history.Read(TimeSpan.FromMinutes(1)).Count==1,"in-memory read");}
        using(var history=new History(path)){Check(history.Read(TimeSpan.FromMinutes(1)).Single().Cpu==20,"history survives reopen");Check(history.Read(TimeSpan.FromMinutes(1)).Single().CpuTemperature==null,"unknown value survives persistence");}
        // Scratch database belongs to this self-test only; no user history is changed.
        File.Delete(path);File.WriteAllText(output,JsonSerializer.Serialize(new{Passed=true,Checks=checks}));return 0;
    }
    public static void Sample(int seconds,string output)
    {
        var settings=Settings.Load();using var sampler=new Sampler(settings);using var advanced=new AdvancedCollector();advanced.Start();var list=new System.Collections.Generic.List<Snapshot>();
        for(int i=0;i<Math.Clamp(seconds,2,60);i++){list.Add(sampler.Read(true,advanced.Read()));Thread.Sleep(1000);}
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);File.WriteAllText(output,JsonSerializer.Serialize(list,new JsonSerializerOptions{WriteIndented=true}));
    }
}
