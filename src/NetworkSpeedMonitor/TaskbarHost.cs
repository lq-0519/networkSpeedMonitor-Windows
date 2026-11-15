using System;
using System.Drawing;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Automation;
using System.Windows.Threading;
using Forms=System.Windows.Forms;

namespace NetworkSpeedMonitor;

internal sealed class TaskbarHost : Forms.NativeWindow, IDisposable
{
    private readonly Settings settings;
    private readonly Dispatcher dispatcher;
    private readonly Action toggle,menu;
    private readonly DispatcherTimer timer;
    private IntPtr taskbar;
    private Snapshot? snapshot;
    private bool scanning,occupancyKnown,disposed;
    private double buttonRight;
    private DateTime lastScan=DateTime.MinValue;
    public string Status {get;private set;}="等待任务栏";
    public Native.Rect Anchor {get;private set;}
    public bool Visible {get;private set;}
    public TaskbarHost(Settings settings,Action toggle,Action menu)
    {
        this.settings=settings;this.toggle=toggle;this.menu=menu;dispatcher=Dispatcher.CurrentDispatcher;
        timer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(1)};timer.Tick+=(_,_)=>UpdatePosition();timer.Start();UpdatePosition();
    }
    public void Update(Snapshot value){snapshot=value;if(Handle!=IntPtr.Zero)Native.InvalidateRect(Handle,IntPtr.Zero,false);}
    private void UpdatePosition()
    {
        if(disposed)return;
        var shell=Native.FindWindow("Shell_TrayWnd",null);
        if(shell==IntPtr.Zero){Hide("未找到任务栏，仍可使用托盘入口");return;}
        if(taskbar!=shell||Handle==IntPtr.Zero||!Native.IsWindow(Handle))
        {
            if(Handle!=IntPtr.Zero){if(Native.IsWindow(Handle))DestroyHandle();else ReleaseHandle();}taskbar=shell;occupancyKnown=false;
            CreateHandle(new Forms.CreateParams{Caption="NetworkSpeedMonitor 任务栏",Parent=taskbar,Style=0x40000000|0x04000000,ExStyle=0x00000080});
        }
        Native.GetWindowRect(taskbar,out var bar);
        var tray=Native.FindWindowEx(taskbar,IntPtr.Zero,"TrayNotifyWnd",null);
        if(tray==IntPtr.Zero||!Native.GetWindowRect(tray,out var trayRect)){Hide("系统托盘布局尚未就绪");return;}
        double dpi=Math.Max(96,Native.GetDpiForWindow(taskbar))/96.0;
        int width=(int)((settings.ShowCpu||settings.ShowMemory?246:138)*dpi),height=Math.Min(bar.Height,(int)(46*dpi));
        int right=trayRect.Left-(int)(8*dpi)-settings.TaskbarOffset;
        if(!scanning&&DateTime.UtcNow-lastScan>TimeSpan.FromSeconds(3))
        {
            scanning=true;lastScan=DateTime.UtcNow;var captured=taskbar;var capturedBar=bar;var capturedTray=trayRect;
            Task.Run(()=>
            {
                double edge=capturedBar.Left;
                try
                {
                    var root=AutomationElement.FromHandle(captured);
                    var buttons=root.FindAll(TreeScope.Descendants,new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Button));
                    foreach(AutomationElement button in buttons){var r=button.Current.BoundingRectangle;if(!r.IsEmpty&&r.Width>0&&r.Left<capturedTray.Left-2&&r.Top>=capturedBar.Top-2&&r.Bottom<=capturedBar.Bottom+2)edge=Math.Max(edge,r.Right);}
                    dispatcher.BeginInvoke(()=>{if(!disposed&&taskbar==captured){buttonRight=edge;occupancyKnown=true;}scanning=false;});
                }catch(Exception ex){Paths.Log("Taskbar layout: "+ex.Message);dispatcher.BeginInvoke(()=>{scanning=false;occupancyKnown=false;});}
            });
        }
        if(!settings.TaskbarEnabled){Hide("任务栏显示已关闭");return;}
        if(!occupancyKnown){Hide("正在检测任务栏可用空间");return;}
        int left=right-width;
        if(bar.Width<bar.Height||bar.Height<=4||left<buttonRight+8*dpi||left<bar.Left){Hide("任务栏空间不足，点击托盘图标展开");return;}
        int top=bar.Top+(bar.Height-height)/2;
        Native.SetWindowPos(Handle,IntPtr.Zero,left-bar.Left,top-bar.Top,width,height,0x0010|0x0040);
        Native.GetWindowRect(Handle,out var actual);Anchor=actual;Visible=true;Status="任务栏已嵌入";
        Native.InvalidateRect(Handle,IntPtr.Zero,false);
    }
    private void Hide(string reason){if(Handle!=IntPtr.Zero)Native.ShowWindow(Handle,0);Visible=false;Status=reason;}
    protected override void WndProc(ref Forms.Message m)
    {
        switch(m.Msg)
        {
            case 0x000F:Paint();m.Result=IntPtr.Zero;return;
            case 0x0014:m.Result=new IntPtr(1);return;
            case 0x0202:dispatcher.BeginInvoke(toggle);return;
            case 0x0205:dispatcher.BeginInvoke(menu);return;
        }
        base.WndProc(ref m);
    }
    private void Paint()
    {
        var dc=Native.BeginPaint(Handle,out var paint);
        try
        {
            Native.GetWindowRect(Handle,out var r);if(r.Width<=0||r.Height<=0)return;
            using var bitmap=new Bitmap(r.Width,r.Height);using var g=Graphics.FromImage(bitmap);
            bool light=false;try{light=(int?)Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize","SystemUsesLightTheme",0)==1;}catch{}
            g.Clear(light?Color.FromArgb(243,243,243):Color.FromArgb(31,32,36));
            float scale=Math.Max(96,Native.GetDpiForWindow(taskbar))/96f;
            using var font=new Font("Segoe UI",11.5f*scale,FontStyle.Regular,GraphicsUnit.Pixel);
            using var accent=new SolidBrush(light?Color.FromArgb(0,95,120):Color.FromArgb(102,221,223));
            using var text=new SolidBrush(light?Color.FromArgb(30,38,47):Color.FromArgb(241,244,249));
            float y1=Math.Max(1,(r.Height-32*scale)/2),y2=y1+17*scale;
            void Draw(string label,string value,float x,float y){g.DrawString(label,font,accent,x,y);g.DrawString(value,font,text,x+19*scale,y);}
            Draw("↓",Format.Size(snapshot?.Sample.Download,true),8*scale,y1);Draw("↑",Format.Size(snapshot?.Sample.Upload,true),8*scale,y2);
            if(settings.ShowCpu||settings.ShowMemory)
            {
                float x=144*scale;using var labelBrush=new SolidBrush(light?Color.DimGray:Color.FromArgb(164,173,188));
                if(settings.ShowCpu){g.DrawString("CPU",font,labelBrush,x,y1);g.DrawString(Format.Percent(snapshot?.Sample.Cpu),font,text,x+43*scale,y1);}
                if(settings.ShowMemory){g.DrawString("内存",font,labelBrush,x,y2);g.DrawString(Format.Percent(snapshot is null||snapshot.TotalMemory==0?null:snapshot.Sample.Memory/snapshot.TotalMemory*100),font,text,x+43*scale,y2);}
            }
            using var target=Graphics.FromHdc(dc);target.DrawImageUnscaled(bitmap,0,0);
        }finally{Native.EndPaint(Handle,ref paint);}
    }
    public string Diagnostics()=>JsonSerializer.Serialize(new{Status,Visible,Handle=Handle.ToInt64(),Taskbar=taskbar.ToInt64(),Anchor=new{Anchor.Left,Anchor.Top,Anchor.Right,Anchor.Bottom},buttonRight,occupancyKnown});
    public void Dispose(){disposed=true;timer.Stop();if(Handle!=IntPtr.Zero)DestroyHandle();}
}
