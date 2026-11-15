using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms=System.Windows.Forms;

namespace NetworkSpeedMonitor;

internal sealed class MonitorApp : Application
{
    public Settings Settings {get;}=Settings.Load();
    public History History {get;}=new();
    public AdvancedBridge Advanced {get;}
    public Snapshot? Latest {get;private set;}
    public bool InspectionMode {get;}
    private readonly CancellationTokenSource cancel=new();
    private Task? sampling;
    private TaskbarHost? taskbar;
    private OverviewWindow? overview;
    private SettingsWindow? settingsWindow;
    private Forms.NotifyIcon? tray;
    private Forms.ContextMenuStrip? menu;
    private Icon? icon;
    private volatile bool detailsVisible,sessionPaused,powerPaused,screenPaused;
    private bool paused=>sessionPaused||powerPaused||screenPaused;
    private IntPtr powerNotification;
    private bool seenOwnForeground,lastMouseDown;
    private readonly DispatcherTimer focusTimer;
    public bool DetailsVisible=>detailsVisible;
    public Native.Rect Anchor
    {
        get { if(taskbar?.Visible==true)return taskbar.Anchor;var shell=Native.FindWindow("Shell_TrayWnd",null);Native.GetWindowRect(shell,out var rect);return new Native.Rect{Left=rect.Right-380,Right=rect.Right-20,Top=rect.Top,Bottom=rect.Bottom}; }
    }
    public MonitorApp(string[] args)
    {
        InspectionMode=args.Contains("--inspect");ShutdownMode=ShutdownMode.OnExplicitShutdown;
        Resources.MergedDictionaries.Add(new ResourceDictionary{Source=new Uri("pack://application:,,,/NetworkSpeedMonitor;component/Theme.xaml",UriKind.Absolute)});
        Advanced=new(Settings);
        DispatcherUnhandledException+=(_,e)=>{Paths.Log(e.Exception.ToString());e.Handled=true;};
        Startup+=(_,_)=>Initialize(args);
        focusTimer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(250)};focusTimer.Tick+=(_,_)=>CheckOutsideFocus();
        Exit+=(_,_)=>Cleanup();
    }
    private void Initialize(string[] args)
    {
        Directory.CreateDirectory(Paths.Data);overview=new OverviewWindow(this);var handle=new WindowInteropHelper(overview).EnsureHandle();
        HwndSource.FromHwnd(handle).AddHook((IntPtr h,int message,IntPtr w,IntPtr l,ref bool handled)=>
        {
            if(message==0x218&&w.ToInt32()==0x8013&&l!=IntPtr.Zero){var guid=System.Runtime.InteropServices.Marshal.PtrToStructure<Guid>(l);if(guid==new Guid("6fe69556-704a-47a0-8f24-c28d936fda47")){screenPaused=System.Runtime.InteropServices.Marshal.ReadInt32(l,20)==0;if(paused)HidePanels();}}
            if(message==0x0312){Toggle();handled=true;}
            if(message==0x802A){int cmd=w.ToInt32();if(cmd==1)Shutdown();else if(cmd==2)WriteStatus();else if(cmd==3)ShowSettings();else if(cmd>=10){ShowPanels();overview.OpenDetail(new[]{"network","cpu","memory","disk","gpu","sensors"}[Math.Clamp(cmd-10,0,5)],true);}else ShowPanels();handled=true;}
            return IntPtr.Zero;
        });
        Native.RegisterHotKey(handle,1,0x4003,0x4E);
        var displaySetting=new Guid("6fe69556-704a-47a0-8f24-c28d936fda47");powerNotification=Native.RegisterPowerSettingNotification(handle,ref displaySetting,0);
        menu=new Forms.ContextMenuStrip();menu.Items.Add("打开资源概览",null,(_,_)=>Dispatcher.BeginInvoke(ShowPanels));menu.Items.Add("设置…",null,(_,_)=>Dispatcher.BeginInvoke(ShowSettings));menu.Items.Add("任务管理器",null,(_,_)=>Process.Start(new ProcessStartInfo("taskmgr.exe"){UseShellExecute=true}));menu.Items.Add(new Forms.ToolStripSeparator());menu.Items.Add("退出",null,(_,_)=>Dispatcher.BeginInvoke(()=>Shutdown()));
        using(var image=new Bitmap(32,32))
        {
            using var g=Graphics.FromImage(image);g.Clear(Color.Transparent);using var pen=new Pen(Color.FromArgb(104,222,215),3){StartCap=System.Drawing.Drawing2D.LineCap.Round,EndCap=System.Drawing.Drawing2D.LineCap.Round};g.DrawLines(pen,new[]{new System.Drawing.Point(3,23),new System.Drawing.Point(10,23),new System.Drawing.Point(14,6),new System.Drawing.Point(19,27),new System.Drawing.Point(24,15),new System.Drawing.Point(29,15)});
            var hIcon=image.GetHicon();icon=(Icon)Icon.FromHandle(hIcon).Clone();DestroyIcon(hIcon);
        }
        tray=new Forms.NotifyIcon{Icon=icon,Text="NetworkSpeedMonitor · 点击查看资源",Visible=true,ContextMenuStrip=menu};tray.MouseClick+=(_,e)=>{if(e.Button==Forms.MouseButtons.Left)Dispatcher.BeginInvoke(Toggle);};
        taskbar=new TaskbarHost(Settings,Toggle,ShowMenu);
        SystemEvents.SessionSwitch+=SessionChanged;SystemEvents.PowerModeChanged+=PowerChanged;
        sampling=Task.Run(SampleLoop);
        if(args.Contains("--show"))Dispatcher.BeginInvoke(ShowPanels);
        int detail=Array.IndexOf(args,"--detail");if(detail>=0&&detail+1<args.Length)Dispatcher.BeginInvoke(()=>{ShowPanels();overview.OpenDetail(args[detail+1],true);});
        if(args.Contains("--settings"))Dispatcher.BeginInvoke(ShowSettings);
        Paths.Log("Started 0.1.2 "+Environment.ProcessPath);
    }
    [System.Runtime.InteropServices.DllImport("user32")] private static extern bool DestroyIcon(IntPtr icon);
    private async Task SampleLoop()
    {
        using var sampler=new Sampler(Settings);bool wasPaused=false;int previousInterval=Settings.Interval;
        while(!cancel.IsCancellationRequested)
        {
            try
            {
                if(paused){wasPaused=true;await Task.Delay(1000,cancel.Token);continue;}
                if(wasPaused||previousInterval!=Settings.Interval){sampler.Reset();wasPaused=false;previousInterval=Settings.Interval;}
                var s=sampler.Read(detailsVisible,Advanced.Latest);History.Append(s.Sample);
                await Dispatcher.InvokeAsync(()=>{Latest=s;taskbar?.Update(s);if(overview?.IsVisible==true)overview.Refresh(s);if(tray!=null)tray.Text=$"CPU {Format.Percent(s.Sample.Cpu)} · ↓ {Format.Size(s.Sample.Download,true)} · ↑ {Format.Size(s.Sample.Upload,true)}";});
                await Task.Delay(Settings.Interval*1000,cancel.Token);
            }catch(OperationCanceledException){break;}catch(Exception ex){Paths.Log("Sample: "+ex);try{await Task.Delay(2000,cancel.Token);}catch{break;}}
        }
    }
    public void Toggle(){if(overview?.IsVisible==true)HidePanels();else ShowPanels();}
    public void ShowPanels()
    {
        if(paused||overview==null)return;detailsVisible=true;seenOwnForeground=false;lastMouseDown=(Native.GetAsyncKeyState(1)&0x8000)!=0;Advanced.SetActive(true);overview.Show();overview.Position();overview.Activate();Native.SetForegroundWindow(new WindowInteropHelper(overview).Handle);if(Latest!=null)overview.Refresh(Latest);focusTimer.Start();
    }
    public void HidePanels(){detailsVisible=false;Advanced.SetActive(false);overview?.HideAll();focusTimer.Stop();Task.Run(()=>History.Flush());}
    public void CheckOutsideFocus()
    {
        if(!detailsVisible||overview==null)return;
        bool own=overview.OwnsForeground(Native.GetForegroundWindow());bool mouse=(Native.GetAsyncKeyState(1)&0x8000)!=0;
        if(own)seenOwnForeground=true;
        else if(seenOwnForeground||(mouse&&!lastMouseDown))HidePanels();
        lastMouseDown=mouse;
    }
    public void ShowSettings(){HidePanels();if(settingsWindow==null){settingsWindow=new SettingsWindow(this);settingsWindow.Closed+=(_,_)=>settingsWindow=null;}settingsWindow.Show();settingsWindow.Activate();}
    private void ShowMenu(){Native.GetCursorPos(out var p);menu?.Show(p.X,p.Y);}
    private void SessionChanged(object sender,SessionSwitchEventArgs e){if(e.Reason==SessionSwitchReason.SessionLock||e.Reason==SessionSwitchReason.RemoteDisconnect){sessionPaused=true;Dispatcher.BeginInvoke(HidePanels);}else if(e.Reason==SessionSwitchReason.SessionUnlock||e.Reason==SessionSwitchReason.RemoteConnect)sessionPaused=false;}
    private void PowerChanged(object sender,PowerModeChangedEventArgs e){if(e.Mode==PowerModes.Suspend){powerPaused=true;Dispatcher.BeginInvoke(HidePanels);}else if(e.Mode==PowerModes.Resume)powerPaused=false;}
    private void WriteStatus()
    {
        File.WriteAllText(Path.Combine(Paths.Data,"diagnostic-latest.json"),JsonSerializer.Serialize(new{Version="0.1.2",ProcessId=Environment.ProcessId,Elevated=AdvancedCollector.Elevated,Taskbar=JsonSerializer.Deserialize<JsonElement>(taskbar?.Diagnostics(!paused)??"{}"),DetailsVisible=detailsVisible,OverviewVisible=overview?.IsVisible,NativeVisible=overview!=null&&Native.IsWindowVisible(new WindowInteropHelper(overview).Handle),Position=overview==null?null:new{overview.Left,overview.Top,overview.ActualWidth,overview.ActualHeight},Paused=paused,Snapshot=Latest},new JsonSerializerOptions{WriteIndented=true}));
    }
    private void Cleanup()
    {
        Paths.Log("Shutdown");SystemEvents.SessionSwitch-=SessionChanged;SystemEvents.PowerModeChanged-=PowerChanged;focusTimer.Stop();cancel.Cancel();
        // UI dispatcher must remain free: sampler dispatch is awaited, so never synchronously wait for it here.
        taskbar?.Dispose();tray?.Dispose();menu?.Dispose();icon?.Dispose();Advanced.Dispose();History.Dispose();
        if(powerNotification!=IntPtr.Zero)Native.UnregisterPowerSettingNotification(powerNotification);
        if(overview!=null)Native.UnregisterHotKey(new WindowInteropHelper(overview).Handle,1);
        settingsWindow?.Close();overview?.CloseAll();
    }
}
