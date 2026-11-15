using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace NetworkSpeedMonitor;

internal sealed class History : IDisposable
{
    private IntPtr db;
    private readonly object gate=new();
    private readonly List<Sample> samples=new(),pending=new();
    private DateTimeOffset lastFlush=DateTimeOffset.UtcNow;
    public string? Error {get;private set;}
    [DllImport("winsqlite3",CallingConvention=CallingConvention.Cdecl)] private static extern int sqlite3_open_v2([MarshalAs(UnmanagedType.LPUTF8Str)]string path,out IntPtr db,int flags,IntPtr vfs);
    [DllImport("winsqlite3",CallingConvention=CallingConvention.Cdecl)] private static extern int sqlite3_exec(IntPtr db,[MarshalAs(UnmanagedType.LPUTF8Str)]string sql,IntPtr callback,IntPtr arg,out IntPtr error);
    [DllImport("winsqlite3",CallingConvention=CallingConvention.Cdecl)] private static extern int sqlite3_prepare_v2(IntPtr db,[MarshalAs(UnmanagedType.LPUTF8Str)]string sql,int length,out IntPtr statement,IntPtr tail);
    [DllImport("winsqlite3",CallingConvention=CallingConvention.Cdecl)] private static extern int sqlite3_step(IntPtr statement);
    [DllImport("winsqlite3",CallingConvention=CallingConvention.Cdecl)] private static extern int sqlite3_finalize(IntPtr statement);
    [DllImport("winsqlite3",CallingConvention=CallingConvention.Cdecl)] private static extern double sqlite3_column_double(IntPtr statement,int col);
    [DllImport("winsqlite3",CallingConvention=CallingConvention.Cdecl)] private static extern IntPtr sqlite3_column_text(IntPtr statement,int col);
    [DllImport("winsqlite3",CallingConvention=CallingConvention.Cdecl)] private static extern int sqlite3_column_type(IntPtr statement,int col);
    [DllImport("winsqlite3",CallingConvention=CallingConvention.Cdecl)] private static extern int sqlite3_close(IntPtr db);
    [DllImport("winsqlite3",CallingConvention=CallingConvention.Cdecl)] private static extern void sqlite3_free(IntPtr memory);
    public History(string? path=null)
    {
        try
        {
            Directory.CreateDirectory(Paths.Data);path??=Path.Combine(Paths.Data,"History.sqlite");
            if(sqlite3_open_v2(path,out db,6,IntPtr.Zero)!=0)throw new IOException("无法打开历史数据库");
            Exec("PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA busy_timeout=200; CREATE TABLE IF NOT EXISTS samples(time INTEGER PRIMARY KEY,segment TEXT NOT NULL,cpu REAL,memory REAL,download REAL,upload REAL,diskread REAL,diskwrite REAL,gpu REAL,vram REAL,cputemp REAL,gputemp REAL,cpupower REAL,gpupower REAL);");
            Exec($"DELETE FROM samples WHERE time<{DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeMilliseconds()};");
            if(sqlite3_prepare_v2(db,"SELECT * FROM samples ORDER BY time",-1,out var statement,IntPtr.Zero)==0)
            {
                try { while(sqlite3_step(statement)==100){double? V(int c)=>sqlite3_column_type(statement,c)==5?null:sqlite3_column_double(statement,c);samples.Add(new(DateTimeOffset.FromUnixTimeMilliseconds((long)(V(0)??0)),Marshal.PtrToStringUTF8(sqlite3_column_text(statement,1))??"",V(2),V(3),V(4),V(5),V(6),V(7),V(8),V(9),V(10),V(11),V(12),V(13)));}}finally{sqlite3_finalize(statement);}
            }
        }catch(Exception ex){Error=ex.Message;Paths.Log("History: "+ex.Message);}
    }
    private void Exec(string sql)
    {
        int code=sqlite3_exec(db,sql,IntPtr.Zero,IntPtr.Zero,out var error);
        if(code!=0){var message=Marshal.PtrToStringUTF8(error)??$"SQLite {code}";sqlite3_free(error);throw new IOException(message);}
    }
    public void Append(Sample sample)
    {
        lock(gate){samples.Add(sample);pending.Add(sample);var cutoff=sample.Time.AddDays(-1);samples.RemoveAll(s=>s.Time<cutoff);if(pending.Count>8192)pending.RemoveRange(0,pending.Count-8192);if(sample.Time-lastFlush>TimeSpan.FromMinutes(1))Flush();}
    }
    public List<Sample> Read(TimeSpan range){lock(gate){var cutoff=DateTimeOffset.UtcNow-range;return samples.Where(s=>s.Time>=cutoff).ToList();}}
    public void Flush()
    {
        lock(gate)
        {
            if(db==IntPtr.Zero||pending.Count==0)return;
            try
            {
                static string N(double? v)=>v is double n&&double.IsFinite(n)?n.ToString("R",CultureInfo.InvariantCulture):"NULL";
                Exec("BEGIN IMMEDIATE;");
                foreach(var s in pending)Exec($"INSERT OR REPLACE INTO samples VALUES({s.Time.ToUnixTimeMilliseconds()},'{s.Segment}',{N(s.Cpu)},{N(s.Memory)},{N(s.Download)},{N(s.Upload)},{N(s.DiskRead)},{N(s.DiskWrite)},{N(s.Gpu)},{N(s.Vram)},{N(s.CpuTemperature)},{N(s.GpuTemperature)},{N(s.CpuPower)},{N(s.GpuPower)});");
                Exec($"DELETE FROM samples WHERE time<{DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeMilliseconds()}; COMMIT;");pending.Clear();Error=null;lastFlush=DateTimeOffset.UtcNow;
            }catch(Exception ex){try{Exec("ROLLBACK;");}catch{}Error=ex.Message;Paths.Log("History flush: "+ex.Message);lastFlush=DateTimeOffset.UtcNow;}
        }
    }
    public void Dispose(){lock(gate){Flush();if(db!=IntPtr.Zero){sqlite3_close(db);db=IntPtr.Zero;}}}
}
