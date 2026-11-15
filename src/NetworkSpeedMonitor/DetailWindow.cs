using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace NetworkSpeedMonitor;

internal sealed class DetailWindow : Window
{
    private readonly MonitorApp app;
    private readonly OverviewWindow overview;
    private readonly TextBlock title,hint,current,summary,note,rankingTitle;
    private readonly TrendChart chart;
    private readonly StackPanel ranking;
    private readonly ScrollViewer scroll;
    private readonly Dictionary<string,bool> expanded=new();
    private readonly ComboBox order;
    private string key="network";
    private TimeSpan range=TimeSpan.FromMinutes(10);
    public DetailWindow(MonitorApp app,OverviewWindow overview)
    {
        this.app=app;this.overview=overview;Owner=overview;Title="NetworkSpeedMonitor · 资源详情";Width=430;Height=600;WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;ShowInTaskbar=false;Topmost=true;AllowsTransparency=true;Background=Brushes.Transparent;FontFamily=new FontFamily("Segoe UI");
        if(app.InspectionMode){ShowInTaskbar=true;Owner=null;}
        var root=new Grid{Margin=new Thickness(19)};root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});root.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star)});
        var upper=new StackPanel();title=Ui.Text("网络详情",20,bold:true);upper.Children.Add(title);hint=Ui.Text("移动到其他资源切换 · 点击资源锁定",10,Ui.Muted);hint.Margin=new Thickness(0,5,0,12);upper.Children.Add(hint);
        current=Ui.Text("—",24,Ui.Accent,true);upper.Children.Add(current);summary=Ui.Text("",11,Ui.Muted);summary.Margin=new Thickness(0,5,0,12);upper.Children.Add(summary);
        var ranges=new StackPanel{Orientation=Orientation.Horizontal};foreach(var (label,seconds) in new[]{("1 分钟",60),("10 分钟",600),("1 小时",3600),("24 小时",86400)})
        {
            var radio=new RadioButton{Content=label,GroupName="range",Foreground=Ui.Brush(Ui.Muted),Margin=new Thickness(0,0,14,0),FontSize=11,IsChecked=seconds==600};radio.Checked+=(_,_)=>{range=TimeSpan.FromSeconds(seconds);if(app.Latest!=null)Refresh(app.Latest);};ranges.Children.Add(radio);
        }upper.Children.Add(ranges);chart=new TrendChart{Height=142,Margin=new Thickness(0,10,0,6)};upper.Children.Add(chart);
        note=Ui.Text("",10,Ui.Muted);note.TextWrapping=TextWrapping.Wrap;note.Margin=new Thickness(0,0,0,12);upper.Children.Add(note);
        var rankHead=new DockPanel();order=new ComboBox{Width=94,FontSize=11,Margin=new Thickness(8,0,0,0)};order.SelectionChanged+=(_,_)=>{if(app.Latest!=null)Refresh(app.Latest);};DockPanel.SetDock(order,Dock.Right);rankHead.Children.Add(order);rankingTitle=Ui.Text("应用排行",13,bold:true);rankHead.Children.Add(rankingTitle);upper.Children.Add(rankHead);Ui.Space(upper,8);root.Children.Add(upper);
        ranking=new StackPanel();scroll=new ScrollViewer{Content=ranking,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};Grid.SetRow(scroll,1);root.Children.Add(scroll);
        Content=new Border{Child=root,Background=Ui.Brush(Ui.Background),BorderBrush=Ui.Brush(Ui.Stroke),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(16)};
        PreviewKeyDown+=(_,e)=>{if(e.Key==System.Windows.Input.Key.Escape)app.HidePanels();};
        Deactivated+=(_,_)=>Dispatcher.BeginInvoke(()=>app.CheckOutsideFocus());
    }
    public void Select(string resource,bool pinned)
    {
        bool changed=key!=resource;key=resource;title.Text=resource switch{"network"=>"网络详情","cpu"=>"CPU 详情","memory"=>"内存详情","disk"=>"磁盘详情","gpu"=>"GPU 详情",_=>"硬件传感器"};
        hint.Text=pinned?"● 已锁定 · 再次点击资源解除":"移动到其他资源切换 · 点击资源锁定";
        if(changed||order.Items.Count==0){order.ItemsSource=key switch{"network"=>new[]{"总流量","下载","上传"},"disk"=>new[]{"总读写","读取","写入"},_=>new[]{"占用"}};order.SelectedIndex=0;scroll.ScrollToTop();}
        if(app.Latest!=null)Refresh(app.Latest);
    }
    public void Position()
    {
        var transform=PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice??Matrix.Identity;
        var area=Native.WorkArea(new WindowInteropHelper(overview).Handle);var start=transform.Transform(new Point(area.Left,area.Top));var end=transform.Transform(new Point(area.Right,area.Bottom));
        Height=Math.Min(620,Math.Max(380,end.Y-start.Y-24));
        Left=overview.Left-Width-9>=start.X?overview.Left-Width-9:Math.Min(end.X-Width-8,overview.Left+overview.Width+9);
        Left=Math.Max(start.X+8,Left);Top=Math.Clamp(overview.Top,start.Y+8,Math.Max(start.Y+8,end.Y-Height-8));
    }
    public void Refresh(Snapshot s)
    {
        double oldOffset=scroll.VerticalOffset;ranking.Children.Clear();var v=s.Sample;chart.Range=range;chart.Samples=app.History.Read(range);chart.Secondary=null;chart.FixedMaximum=null;
        note.Text="曲线只记录实际采样；休眠和采集缺口保留断线。";order.Visibility=key is "network" or "disk"?Visibility.Visible:Visibility.Collapsed;rankingTitle.Text="应用排行";
        switch(key)
        {
            case "network":current.Text=$"↓ {Format.Size(v.Download,true)}";summary.Text=$"↑ {Format.Size(v.Upload,true)} · {s.Network}";chart.Value=x=>x.Download;chart.Secondary=x=>x.Upload;
                note.Text="青色：下载 · 紫色：上传。物理网卡总量与应用排行统计层不同；代理流量可能归代理进程，排行包含本地通信。";IoRanking(s,true);break;
            case "cpu":current.Text=Format.Percent(v.Cpu);summary.Text=$"{Environment.ProcessorCount} 个逻辑核心 · 整机满负载为 100%";chart.Value=x=>x.Cpu;chart.FixedMaximum=100;
                ProcessRanking(s,p=>p.Cpu??0,true);break;
            case "memory":current.Text=Format.Size(v.Memory);summary.Text=$"可用 {Format.Size(s.AvailableMemory)} / 总量 {Format.Size(s.TotalMemory)}";chart.Value=x=>x.Memory;chart.FixedMaximum=s.TotalMemory;
                note.Text=$"提交量 {Format.Size(s.Committed)} / {Format.Size(s.CommitLimit)}。应用排行按私有工作集分组，不能与整机已用量直接求和比较。";ProcessRanking(s,p=>p.Memory,false);break;
            case "disk":current.Text=$"读 {Format.Size(v.DiskRead,true)}";summary.Text=$"写 {Format.Size(v.DiskWrite,true)} · 全部物理磁盘";chart.Value=x=>x.DiskRead;chart.Secondary=x=>x.DiskWrite;
                foreach(var drive in s.Drives)Line(drive.Name,$"可用 {Format.Size(drive.Free)} / {Format.Size(drive.Total)}");
                note.Text="青色：读取 · 紫色：写入。排行来自磁盘事件；系统缓存、内核和权限限制可能使排行合计与物理磁盘吞吐不同。";IoRanking(s,false);break;
            case "gpu":current.Text=Format.Percent(v.Gpu);summary.Text=$"独立显存已用 {Format.Size(v.Vram)}";chart.Value=x=>x.Gpu;chart.FixedMaximum=100;rankingTitle.Text="显卡传感器";
                note.Text="GPU 占用取所有可读物理引擎中最繁忙的一项，不累加不同引擎百分比。显存显示占用最高适配器的独立显存。";Sensors(s,"Gpu");break;
            default:current.Text=$"CPU {Format.Temp(v.CpuTemperature)}";summary.Text=$"GPU {Format.Temp(v.GpuTemperature)} · CPU 功耗 {Format.Watts(v.CpuPower)}";chart.Value=x=>x.CpuTemperature;chart.Secondary=x=>x.GpuTemperature;rankingTitle.Text="实际可读传感器";
                note.Text="青色：CPU 温度 · 紫色：GPU 温度。部件功耗不代表整机插座功耗；缺失的传感器显示不可用。";Sensors(s,null);break;
        }
        chart.InvalidateVisual();if(ranking.Children.Count==0)Line("等待有效读数","首次采样需要建立基线");scroll.ScrollToVerticalOffset(oldOffset);
    }
    private void ProcessRanking(Snapshot s,Func<ProcessRow,double> value,bool cpu)
    {
        if(cpu&&s.Processes.All(p=>p.Cpu==null)){Line("正在建立进程基线","下一轮采样后显示 CPU 排行");return;}
        foreach(var appRow in Format.Group(s.Processes,value).Take(app.Settings.RankingCount))
        {
            string identity=appRow.Processes.First().Path+appRow.Name;
            var children=new StackPanel();foreach(var p in appRow.Processes){var t=Ui.Text($"PID {p.Pid} · {p.Name}    {(cpu?Format.Percent(p.Cpu):Format.Size(p.Memory))}",11,Ui.Muted);t.Margin=new Thickness(10,5,0,5);children.Children.Add(t);}
            var header=new Grid{Width=310};header.ColumnDefinitions.Add(new ColumnDefinition());header.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});var name=Ui.Text(appRow.Name,12);name.MaxWidth=235;header.Children.Add(name);var amount=Ui.Text(cpu?Format.Percent(appRow.Value):Format.Size(appRow.Value),12,Ui.Accent);Grid.SetColumn(amount,1);header.Children.Add(amount);
            var expander=new Expander{Header=header,Content=children,Foreground=Ui.Brush(Ui.Foreground),HorizontalContentAlignment=HorizontalAlignment.Stretch,IsExpanded=expanded.GetValueOrDefault(identity)};expander.Expanded+=(_,_)=>expanded[identity]=true;expander.Collapsed+=(_,_)=>expanded[identity]=false;
            var panel=Ui.Panel(expander,new Thickness(8));panel.Margin=new Thickness(0,0,0,6);ranking.Children.Add(panel);
        }
    }
    private void IoRanking(Snapshot s,bool net)
    {
        var advanced=s.Advanced;
        if(advanced==null||DateTimeOffset.UtcNow-advanced.Time>TimeSpan.FromSeconds(10))
        {
            Line("需要管理员采集",app.Advanced.Status);var button=Ui.Button("启用管理员采集",async()=>{await app.Advanced.EnableAsync();},true);button.Margin=new Thickness(0,8,0,0);ranking.Children.Add(button);return;
        }
        if(advanced.LostEvents>0)Line("采集覆盖受限",$"已丢失 {advanced.LostEvents} 个事件，排行可能不完整");
        var names=s.Processes.ToDictionary(p=>p.Pid);
        double Amount(IoRow p)=>net?(order.SelectedIndex==1?p.Download:order.SelectedIndex==2?p.Upload:p.Download+p.Upload):(order.SelectedIndex==1?p.Read:order.SelectedIndex==2?p.Write:p.Read+p.Write);
        foreach(var group in advanced.Processes.Where(p=>Amount(p)>0).GroupBy(p=>names.TryGetValue(p.Pid,out var meta)&&meta.Path.Length>0?meta.Path:$"PID {p.Pid}").Select(g=>new{Key=g.Key,Value=g.Sum(Amount),Rows=g.ToList()}).OrderByDescending(g=>g.Value).Take(app.Settings.RankingCount))
        {
            var first=group.Rows[0];string name=names.TryGetValue(first.Pid,out var meta)?meta.Name:group.Key;
            Line(name,Format.Size(group.Value,true));
        }
        if(advanced.Processes.Count==0)Line("本周期没有可归属的活动",advanced.Status);
    }
    private void Sensors(Snapshot s,string? filter)
    {
        var sensors=s.Sensors.Where(r=>filter==null||r.Hardware.Contains(filter,StringComparison.OrdinalIgnoreCase)).ToList();
        foreach(var sensor in sensors){string value=sensor.Kind switch{"Temperature"=>Format.Temp(sensor.Value),"Fan"=>$"{sensor.Value:0} RPM","Power"=>Format.Watts(sensor.Value),_=>sensor.Value.ToString("0.0")};Line(sensor.Name,value,sensor.Hardware);}
        if(sensors.Count==0){Line("传感器暂不可用","硬件、驱动或权限未提供有效读数");if(!AdvancedCollector.Elevated)ranking.Children.Add(Ui.Button("启用管理员采集",async()=>await app.Advanced.EnableAsync()));}
    }
    private void Line(string name,string value,string? subtitle=null)
    {
        var stack=new StackPanel();var grid=new Grid();grid.ColumnDefinitions.Add(new ColumnDefinition());grid.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});var text=Ui.Text(name,12);text.MaxWidth=220;grid.Children.Add(text);var amount=Ui.Text(value,11,Ui.Accent);amount.MaxWidth=235;amount.ToolTip=value;Grid.SetColumn(amount,1);grid.Children.Add(amount);stack.Children.Add(grid);if(subtitle!=null){var sub=Ui.Text(subtitle,9,Ui.Muted);sub.Margin=new Thickness(0,4,0,0);stack.Children.Add(sub);}var panel=Ui.Panel(stack,new Thickness(10));panel.Margin=new Thickness(0,0,0,6);ranking.Children.Add(panel);
    }
}
