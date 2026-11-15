using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Security.Principal;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LibreHardwareMonitor.Hardware;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Session;

namespace NetworkSpeedMonitor;

internal sealed class AdvancedCollector : IDisposable
{
    private TraceEventSession? session;
    private readonly ConcurrentDictionary<int,long[]> counts=new();
    private readonly Computer hardware=new(){IsCpuEnabled=true,IsGpuEnabled=true,IsMotherboardEnabled=true,IsStorageEnabled=true};
    private bool hardwareOpen;
    private double previousTime;
    private DateTimeOffset lastSensors;
    private List<SensorReading> sensors=new();
    private string status="尚未启动";
    public bool HardwareEnabled {get;set;}=true;
    public static bool Elevated=>new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
    public void Start()
    {
        if(session!=null)return;
        if(!Elevated){status="应用网络和磁盘排行需要管理员采集";return;}
        try
        {
            // Dedicated session: never alter another program's kernel logger.
            session=new TraceEventSession("NetworkSpeedMonitor-"+Environment.ProcessId){StopOnDispose=true};
            session.EnableKernelProvider(KernelTraceEventParser.Keywords.NetworkTCPIP|KernelTraceEventParser.Keywords.DiskIO|KernelTraceEventParser.Keywords.DiskFileIO|KernelTraceEventParser.Keywords.Process|KernelTraceEventParser.Keywords.Thread);
            session.Source.Kernel.ProcessStart+=e=>counts.TryRemove(e.ProcessID,out _);
            session.Source.Kernel.ProcessStop+=e=>counts.TryRemove(e.ProcessID,out _);
            void Add(int pid,int index,long bytes){if(pid<=0||bytes<0)return;var row=counts.GetOrAdd(pid,_=>new long[4]);Interlocked.Add(ref row[index],bytes);}
            session.Source.Kernel.TcpIpRecv+=e=>Add(e.ProcessID,0,e.size);
            session.Source.Kernel.TcpIpSend+=e=>Add(e.ProcessID,1,e.size);
            session.Source.Kernel.TcpIpRecvIPV6+=e=>Add(e.ProcessID,0,e.size);
            session.Source.Kernel.TcpIpSendIPV6+=e=>Add(e.ProcessID,1,e.size);
            session.Source.Kernel.UdpIpRecv+=e=>Add(e.ProcessID,0,e.size);
            session.Source.Kernel.UdpIpSend+=e=>Add(e.ProcessID,1,e.size);
            session.Source.Kernel.UdpIpRecvIPV6+=e=>Add(e.ProcessID,0,e.size);
            session.Source.Kernel.UdpIpSendIPV6+=e=>Add(e.ProcessID,1,e.size);
            session.Source.Kernel.DiskIORead+=e=>Add(e.ProcessID,2,e.TransferSize);
            session.Source.Kernel.DiskIOWrite+=e=>Add(e.ProcessID,3,e.TransferSize);
            var source=session.Source;Task.Run(()=>{try{source.Process();}catch(Exception ex){Paths.Log("ETW consumer: "+ex.Message);}});
            previousTime=Stopwatch.GetTimestamp()/(double)Stopwatch.Frequency;status="实时 · ETW";
        }catch(Exception ex){status="ETW 不可用："+ex.Message;Paths.Log(status);Stop();}
    }
    public AdvancedFrame Read()
    {
        double now=Stopwatch.GetTimestamp()/(double)Stopwatch.Frequency,elapsed=now-previousTime;previousTime=now;
        var rows=new List<IoRow>();
        foreach(var pair in counts)
        {
            var c=pair.Value;double R(int i)=>Interlocked.Exchange(ref c[i],0)/Math.Max(0.1,elapsed);
            var row=new IoRow(pair.Key,R(0),R(1),R(2),R(3));if(row.Download+row.Upload+row.Read+row.Write>0)rows.Add(row);
        }
        // Bounded PID map; inactive entries are removed without retaining connection details.
        if(counts.Count>8192)counts.Clear();
        if(!HardwareEnabled){sensors=new();if(hardwareOpen){try{hardware.Close();}catch{}hardwareOpen=false;}}
        if(HardwareEnabled&&DateTimeOffset.UtcNow-lastSensors>TimeSpan.FromSeconds(5))
        {
            lastSensors=DateTimeOffset.UtcNow;var next=new List<SensorReading>();
            try
            {
                if(!hardwareOpen){hardware.Open();hardwareOpen=true;}
                void Visit(IHardware h){h.Update();foreach(var s in h.Sensors)if(s.Value is float value&&float.IsFinite(value)&&s.SensorType is SensorType.Temperature or SensorType.Fan or SensorType.Power){if(s.SensorType==SensorType.Power&&value<=0)continue;if(s.SensorType==SensorType.Temperature&&(value<=0||s.Name.Contains("Warning")||s.Name.Contains("Critical")))continue;next.Add(new(s.Name,s.SensorType.ToString(),h.HardwareType+" · "+h.Name,value));}foreach(var sub in h.SubHardware)Visit(sub);}
                foreach(var h in hardware.Hardware)Visit(h);sensors=next;
            }catch(Exception ex){sensors=new();Paths.Log("Sensors: "+ex.Message);}
        }
        return new(DateTimeOffset.UtcNow,rows,sensors,status,session?.EventsLost??0);
    }
    public void Stop(){var current=session;session=null;try{current?.Dispose();}catch{}counts.Clear();}
    public void Dispose(){Stop();try{hardware.Close();}catch{}}
}

internal sealed class AdvancedBridge : IDisposable
{
    private readonly Settings settings;
    private readonly AdvancedCollector? local;
    private readonly CancellationTokenSource cancel=new();
    private NamedPipeServerStream? server;
    private StreamWriter? writer;
    private volatile bool active;
    private Task? loop;
    private AdvancedFrame? latest;
    public AdvancedFrame? Latest=>Volatile.Read(ref latest);
    public string Status {get;private set;}="打开概览后采集应用排行";
    public AdvancedBridge(Settings settings){this.settings=settings;if(AdvancedCollector.Elevated)local=new();}
    public void SetActive(bool value)
    {
        active=value&&settings.AdvancedEnabled;
        if(!active)Volatile.Write(ref latest,null);
        if(local!=null)
        {
            loop??=Task.Run(async()=>{while(!cancel.IsCancellationRequested){try{if(active){local.HardwareEnabled=settings.HardwareEnabled;local.Start();Volatile.Write(ref latest,local.Read());}else{local.Stop();Volatile.Write(ref latest,null);}await Task.Delay(1000,cancel.Token);}catch(OperationCanceledException){break;}catch(Exception ex){Status=ex.Message;Paths.Log("Advanced: "+ex.Message);}}});
        }
        else if(writer!=null){try{writer.WriteLine(active?(settings.HardwareEnabled?"start":"start:nohardware"):"stop");}catch{Status="采集连接中断";}}
        else if(active)Status="点击「管理员采集」启用应用网络、磁盘排行及硬件传感器";
    }
    public async Task EnableAsync()
    {
        settings.AdvancedEnabled=true;settings.Save();if(local!=null){SetActive(true);return;}
        if(server!=null)return;
        string pipe="NetworkSpeedMonitor-"+Environment.ProcessId+"-"+Guid.NewGuid().ToString("N");
        server=new NamedPipeServerStream(pipe,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
        try
        {
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!,"--collector "+pipe+" "+Environment.ProcessId){UseShellExecute=true,Verb="runas",WindowStyle=ProcessWindowStyle.Hidden});
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancel.Token);timeout.CancelAfter(30_000);
            await server.WaitForConnectionAsync(timeout.Token);writer=new StreamWriter(server){AutoFlush=true};writer.WriteLine(active?(settings.HardwareEnabled?"start":"start:nohardware"):"stop");Status="已连接管理员采集";
            loop=Task.Run(async()=>{using var reader=new StreamReader(server);while(!cancel.IsCancellationRequested){var line=await reader.ReadLineAsync(cancel.Token);if(line==null)break;if(active)Volatile.Write(ref latest,JsonSerializer.Deserialize<AdvancedFrame>(line));}});
        }
        catch(Exception ex){Status="管理员采集未启用："+ex.Message;server.Dispose();server=null;}
    }
    public static async Task RunHelper(string pipe,int parent)
    {
        using var client=new NamedPipeClientStream(".",pipe,PipeDirection.InOut,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);await client.ConnectAsync(15_000);
        using var reader=new StreamReader(client);using var writer=new StreamWriter(client){AutoFlush=true};using var collector=new AdvancedCollector();
        bool enabled=false;var read=reader.ReadLineAsync();
        while(client.IsConnected)
        {
            try{using var p=Process.GetProcessById(parent);if(p.HasExited)break;}catch{break;}
            if(await Task.WhenAny(read,Task.Delay(1000))==read){string? command=await read;if(command==null||command=="exit")break;enabled=command.StartsWith("start",StringComparison.Ordinal);collector.HardwareEnabled=command!="start:nohardware";if(enabled)collector.Start();else collector.Stop();read=reader.ReadLineAsync();}
            if(enabled)await writer.WriteLineAsync(JsonSerializer.Serialize(collector.Read()));
        }
    }
    public void Dispose(){cancel.Cancel();try{writer?.WriteLine("exit");}catch{}server?.Dispose();try{loop?.Wait(3000);}catch{}local?.Dispose();cancel.Dispose();}
}
