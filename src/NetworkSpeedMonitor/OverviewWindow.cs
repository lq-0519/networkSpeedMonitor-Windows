using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace NetworkSpeedMonitor;

internal sealed class OverviewWindow : Window
{
    private readonly MonitorApp app;
    private readonly Dictionary<string,(TextBlock Value,TextBlock Subtitle,Border Border)> rows=new();
    private readonly TextBlock status;
    private string? locked;
    private DetailWindow? detail;
    private bool changing;
    public string? ActiveDetailKey=>detail?.IsVisible==true?detail.ResourceKey:null;
    public OverviewWindow(MonitorApp app)
    {
        this.app=app;Title="NetworkSpeedMonitor · 系统概览";Width=310;SizeToContent=SizeToContent.Height;WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;ShowInTaskbar=false;Topmost=true;AllowsTransparency=true;Background=Brushes.Transparent;FontFamily=new FontFamily("Segoe UI");
        if(app.InspectionMode)ShowInTaskbar=true;
        var stack=new StackPanel{Margin=new Thickness(18)};
        var header=new DockPanel();var close=Ui.Button("×",()=>app.HidePanels());DockPanel.SetDock(close,Dock.Right);header.Children.Add(close);
        var titles=new StackPanel();titles.Children.Add(Ui.Text("系统监控",21,bold:true));titles.Children.Add(Ui.Text("NETWORK SPEED MONITOR",9,Ui.Muted));header.Children.Add(titles);stack.Children.Add(header);Ui.Space(stack,16);
        AddRow(stack,"network","↕","网络",Ui.Accent);
        AddRow(stack,"cpu","◉","CPU",Color.FromRgb(153,166,255));
        AddRow(stack,"memory","▤","内存",Color.FromRgb(108,188,255));
        AddRow(stack,"disk","▣","磁盘",Color.FromRgb(249,192,113));
        AddRow(stack,"gpu","◇","GPU",Color.FromRgb(209,161,255));
        AddRow(stack,"sensors","⌁","温度 / 风扇 / 功耗",Color.FromRgb(244,150,160));
        status=Ui.Text("正在建立采样基线…",11,Ui.Muted);status.Margin=new Thickness(0,8,0,12);stack.Children.Add(status);
        var footer=new DockPanel();var settings=Ui.Button("设置",()=>app.ShowSettings());DockPanel.SetDock(settings,Dock.Right);footer.Children.Add(settings);footer.Children.Add(Ui.Button("任务管理器",()=>Process.Start(new ProcessStartInfo("taskmgr.exe"){UseShellExecute=true})));stack.Children.Add(footer);
        Content=new Border{Background=Ui.Brush(Ui.Background),BorderBrush=Ui.Brush(Ui.Stroke),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(16),Child=stack};
        PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Escape){app.HidePanels();e.Handled=true;}};
        Deactivated+=(_,_)=>Dispatcher.BeginInvoke(()=>{if(!changing && IsVisible && detail?.IsActive!=true && !IsActive)app.CheckOutsideFocus();});
        SourceInitialized+=(_,_)=>{int dark=1;Native.DwmSetWindowAttribute(new WindowInteropHelper(this).Handle,20,ref dark,4);};
        Loaded+=(_,_)=>Position();
    }
    private void AddRow(StackPanel stack,string key,string icon,string name,Color color)
    {
        var text=new StackPanel();var label=Ui.Text(icon+"  "+name,12,color);text.Children.Add(label);var value=Ui.Text("—",17,bold:true);value.Margin=new Thickness(0,4,0,2);text.Children.Add(value);var subtitle=Ui.Text("等待采样",10,Ui.Muted);text.Children.Add(subtitle);
        var border=Ui.Panel(text,new Thickness(12,9,12,9));border.Cursor=Cursors.Hand;
        var button=new Button{Content=border,Style=(Style)FindResource("ResourceRowButton"),Background=Brushes.Transparent,BorderThickness=new Thickness(0),Padding=new Thickness(0),Margin=new Thickness(0,0,0,7),HorizontalContentAlignment=HorizontalAlignment.Stretch,Cursor=Cursors.Hand};
        System.Windows.Automation.AutomationProperties.SetName(button,name+"详情");
        button.MouseEnter+=(_,_)=>{if(locked==null)OpenDetail(key,false);};
        button.Click+=(_,_)=>{locked=locked==key?null:key;OpenDetail(key,locked!=null);};
        stack.Children.Add(button);rows[key]=(value,subtitle,border);
    }
    public void OpenDetail(string key,bool pinned)
    {
        changing=true;
        try{app.SetDetailResource(key);detail??=new DetailWindow(app,this);detail.Select(key,pinned);if(!detail.IsVisible)detail.Show();detail.Position();Highlight(key);}finally{changing=false;}
    }
    private void Highlight(string key){foreach(var pair in rows)pair.Value.Border.BorderBrush=Ui.Brush(pair.Key==key?Ui.Accent:Ui.Stroke);}
    public void Refresh(Snapshot s)
    {
        var v=s.Sample;
        Set("network",$"↓ {Format.Size(v.Download,true)}",$"↑ {Format.Size(v.Upload,true)} · {s.Network}");
        Set("cpu",Format.Percent(v.Cpu),"全部逻辑核心归一化 · 应用排行");
        Set("memory",$"{Format.Size(v.Memory)} / {Format.Size(s.TotalMemory)}",$"可用 {Format.Size(s.AvailableMemory)} · {Format.Percent(v.Memory/s.TotalMemory*100)}");
        Set("disk",$"读 {Format.Size(v.DiskRead,true)}",$"写 {Format.Size(v.DiskWrite,true)} · {s.Drives.Count} 块本地卷");
        Set("gpu",Format.Percent(v.Gpu),$"显存 {Format.Size(v.Vram)} · 最繁忙引擎");
        string temperatures=$"CPU {Format.Temp(v.CpuTemperature)} · GPU {Format.Temp(v.GpuTemperature)}";
        Set("sensors",temperatures,s.Sensors.Count>0?$"{s.Sensors.Count} 个有效读数 · 点击查看":"进入详情按需读取传感器");
        status.Text=$"● 实时 · {app.Settings.Interval} 秒采样  {v.Time.ToLocalTime():HH:mm:ss}";
        if(app.History.Error!=null)status.Text="历史保存受限，实时采集继续";
        if(detail?.IsVisible==true)detail.Refresh(s);
    }
    private void Set(string key,string value,string subtitle)
    {
        var row=rows[key];row.Value.Text=value;row.Subtitle.Text=subtitle;row.Subtitle.ToolTip=subtitle;
    }
    public void Position()
    {
        var anchor=app.Anchor;var source=PresentationSource.FromVisual(this);var transform=source?.CompositionTarget?.TransformFromDevice??Matrix.Identity;var p=transform.Transform(new Point(anchor.Right,anchor.Top));
        var work=Native.WorkArea(new WindowInteropHelper(this).Handle);var start=transform.Transform(new Point(work.Left,work.Top));var end=transform.Transform(new Point(work.Right,work.Bottom));
        Left=Math.Clamp(p.X-Width,start.X+8,Math.Max(start.X+8,end.X-Width-8));Top=Math.Max(start.Y+8,Math.Min(p.Y-ActualHeight-10,end.Y-ActualHeight-8));detail?.Position();
    }
    public void HideAll(){locked=null;detail?.Close();detail=null;Hide();foreach(var row in rows.Values)row.Border.BorderBrush=Ui.Brush(Ui.Stroke);}
    public bool OwnsForeground(IntPtr handle)=>new WindowInteropHelper(this).Handle==handle||(detail!=null&&new WindowInteropHelper(detail).Handle==handle);
    public void CloseAll(){detail?.Close();Close();}
}
