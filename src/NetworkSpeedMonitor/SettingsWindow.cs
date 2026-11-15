using System;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace NetworkSpeedMonitor;

internal sealed class SettingsWindow : Window
{
    public SettingsWindow(MonitorApp app)
    {
        Title="NetworkSpeedMonitor 设置";Width=500;Height=650;MinHeight=500;WindowStartupLocation=WindowStartupLocation.CenterScreen;Background=Ui.Brush(Ui.Background);Foreground=Ui.Brush(Ui.Foreground);FontFamily=new System.Windows.Media.FontFamily("Segoe UI");
        var stack=new StackPanel{Margin=new Thickness(25)};stack.Children.Add(Ui.Text("监控设置",23,bold:true));Ui.Space(stack,8);stack.Children.Add(Ui.Text("任务栏入口 · 本地数据 · 按需采集",12,Ui.Muted));Ui.Space(stack,22);
        ComboBox Choice(string label,object[] values,object selected,Action<object> change)
        {
            stack.Children.Add(Ui.Text(label));var box=new ComboBox{ItemsSource=values,SelectedItem=selected,Margin=new Thickness(0,6,0,17),Padding=new Thickness(7)};box.SelectionChanged+=(_,_)=>{if(box.SelectedItem!=null){change(box.SelectedItem);app.Settings.Save();}};stack.Children.Add(box);return box;
        }
        void Check(string label,bool value,Action<bool> change)
        {
            var check=new CheckBox{Content=label,IsChecked=value,Foreground=Ui.Brush(Ui.Foreground),Margin=new Thickness(0,0,0,13)};check.Checked+=(_,_)=>{change(true);app.Settings.Save();};check.Unchecked+=(_,_)=>{change(false);app.Settings.Save();};stack.Children.Add(check);
        }
        Choice("采样间隔（秒）",new object[]{1,2,5},app.Settings.Interval,o=>app.Settings.Interval=(int)o);
        Choice("每类应用排行数量",new object[]{5,10,20,50},app.Settings.RankingCount,o=>app.Settings.RankingCount=(int)o);
        var adapters=Sampler.Adapters();var options=new[]{("","自动：物理以太网 / Wi-Fi")}.Concat(adapters).ToList();
        stack.Children.Add(Ui.Text("网速统计网卡"));var network=new ComboBox{ItemsSource=options.Select(x=>x.Item2).ToList(),SelectedIndex=Math.Max(0,options.FindIndex(x=>x.Item1==app.Settings.NetworkId)),Margin=new Thickness(0,6,0,17),Padding=new Thickness(7)};network.SelectionChanged+=(_,_)=>{if(network.SelectedIndex>=0){app.Settings.NetworkId=options[network.SelectedIndex].Item1;app.Settings.Save();}};stack.Children.Add(network);
        Check("在任务栏显示实时数据",app.Settings.TaskbarEnabled,v=>app.Settings.TaskbarEnabled=v);
        Check("任务栏显示 CPU",app.Settings.ShowCpu,v=>app.Settings.ShowCpu=v);
        Check("任务栏显示内存",app.Settings.ShowMemory,v=>app.Settings.ShowMemory=v);
        Check("启用详细硬件传感器（读取周期至少 5 秒）",app.Settings.HardwareEnabled,v=>{app.Settings.HardwareEnabled=v;app.Advanced.SetActive(app.DetailsVisible);});
        Check("展开概览时采集应用网络 / 磁盘排行",app.Settings.AdvancedEnabled,v=>{app.Settings.AdvancedEnabled=v;app.Advanced.SetActive(app.DetailsVisible);});
        using(var run=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
            Check("登录 Windows 后自动启动",run?.GetValue("NetworkSpeedMonitor")!=null,v=>{using var key=Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");if(v)key.SetValue("NetworkSpeedMonitor","\""+Environment.ProcessPath+"\"");else key.DeleteValue("NetworkSpeedMonitor",false);});
        var offset=new Slider{Minimum=0,Maximum=800,TickFrequency=8,IsSnapToTickEnabled=true,Value=app.Settings.TaskbarOffset,Margin=new Thickness(0,8,0,17)};stack.Children.Add(Ui.Text("任务栏向左偏移（像素）",12,Ui.Muted));offset.ValueChanged+=(_,_)=>{app.Settings.TaskbarOffset=(int)offset.Value;app.Settings.Save();};stack.Children.Add(offset);
        var explanation=Ui.Text("自动网卡会排除常见隧道与虚拟设备。使用代理时，物理网卡总量与应用排行可能不同。任务栏空间不足时仍可从托盘图标展开。",11,Ui.Muted);explanation.TextWrapping=TextWrapping.Wrap;stack.Children.Add(explanation);Ui.Space(stack,15);
        stack.Children.Add(Ui.Button("启用管理员采集",async()=>await app.Advanced.EnableAsync()));Ui.Space(stack,8);
        stack.Children.Add(Ui.Button("打开本地数据目录",()=>Process.Start(new ProcessStartInfo("explorer.exe",Paths.Data){UseShellExecute=true})));Ui.Space(stack,8);
        stack.Children.Add(Ui.Text("快捷键 Ctrl + Alt + N 展开 / 收起\n数据只保存在本机，不上传监控信息。",11,Ui.Muted));
        Content=new ScrollViewer{Content=stack,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
    }
}
