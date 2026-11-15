using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace NetworkSpeedMonitor;

internal static class Paths
{
    public static readonly string Data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NetworkSpeedMonitor");
    public static void Log(string text)
    {
        try { Directory.CreateDirectory(Data); var file = Path.Combine(Data, "app.log"); if (File.Exists(file) && new FileInfo(file).Length > 1_000_000) File.Move(file, file + ".old", true); File.AppendAllText(file, $"{DateTimeOffset.Now:O} {text}\n"); } catch { }
    }
}

internal sealed class Settings
{
    public int Interval { get; set; } = 2;
    public int RankingCount { get; set; } = 10;
    public string NetworkId { get; set; } = "";
    public bool ShowMemory { get; set; } = true;
    public bool ShowCpu { get; set; } = true;
    public bool TaskbarEnabled { get; set; } = true;
    public bool HardwareEnabled { get; set; } = true;
    public bool AdvancedEnabled { get; set; } = true;
    public int TaskbarOffset { get; set; }
    public static Settings Load()
    {
        try { var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(Path.Combine(Paths.Data, "settings.json"))) ?? new(); s.Interval = new[] {1,2,5}.Contains(s.Interval) ? s.Interval : 2; s.RankingCount = Math.Clamp(s.RankingCount,1,50); s.TaskbarOffset = Math.Clamp(s.TaskbarOffset,0,2000); return s; } catch { return new(); }
    }
    public void Save() { Directory.CreateDirectory(Paths.Data); var file = Path.Combine(Paths.Data,"settings.json"); File.WriteAllText(file+".tmp",JsonSerializer.Serialize(this,new JsonSerializerOptions {WriteIndented=true})); File.Move(file+".tmp",file,true); }
}

internal sealed record ProcessRow(int Pid, int Parent, long Started, string Name, string Path, double? Cpu, ulong Memory);
internal sealed record AppRow(string Name, double Value, IReadOnlyList<ProcessRow> Processes);
internal sealed record SensorReading(string Name, string Kind, string Hardware, double Value);
internal sealed record IoRow(int Pid, double Download, double Upload, double Read, double Write);
internal sealed record AdvancedFrame(DateTimeOffset Time, List<IoRow> Processes, List<SensorReading> Sensors, string Status, long LostEvents);
internal sealed record Sample(DateTimeOffset Time, string Segment, double? Cpu, double? Memory, double? Download, double? Upload, double? DiskRead, double? DiskWrite, double? Gpu, double? Vram, double? CpuTemperature, double? GpuTemperature, double? CpuPower, double? GpuPower);
internal sealed record Snapshot(Sample Sample, ulong TotalMemory, ulong AvailableMemory, ulong Committed, ulong CommitLimit, string Network, IReadOnlyList<ProcessRow> Processes, IReadOnlyList<(string Name, long Total, long Free)> Drives, IReadOnlyList<SensorReading> Sensors, AdvancedFrame? Advanced, string Error);

internal static class Format
{
    private static readonly string[] RateUnits={"B/s","KB/s","MB/s","GB/s"};
    private static readonly string[] SizeUnits={"B","KB","MB","GB","TB"};
    public static string Percent(double? value) => value is double v && double.IsFinite(v) ? $"{v:0}%" : "—";
    public static string Size(double? value, bool rate=false)
    {
        if (value is not double v || !double.IsFinite(v) || v<0) return "—";
        string[] units=rate?RateUnits:SizeUnits;
        int i=0; while(v>=1024 && i<units.Length-1){v/=1024;i++;}
        return v.ToString(i==0?"0":"0.0",System.Globalization.CultureInfo.InvariantCulture)+" "+units[i];
    }
    public static string Temp(double? value) => value is double v ? $"{v:0} °C" : "—";
    public static string Watts(double? value) => value is double v ? $"{v:0.0} W" : "—";
    public static List<AppRow> Group(IReadOnlyList<ProcessRow> rows, Func<ProcessRow,double> value)
        => rows.GroupBy(p=>string.IsNullOrEmpty(p.Path)?$"{p.Name}:{p.Pid}":p.Path,StringComparer.OrdinalIgnoreCase)
            .Select(g=>new AppRow(g.First().Name,g.Sum(value),g.OrderByDescending(value).ToList())).OrderByDescending(r=>r.Value).ToList();
}
