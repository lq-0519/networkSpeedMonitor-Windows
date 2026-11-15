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
    private long paintCount;
    private StripLayout? cachedLayout;
    private (float Scale,bool Cpu,bool Memory) layoutKey;
    private readonly record struct StripLayout(float ArrowX,float ValueX,float StatsX,float StatsValueX,float PercentRight,float Width);

    private StripLayout Layout(float scale)
    {
        var key=(scale,settings.ShowCpu,settings.ShowMemory);
        if(cachedLayout is {} cached&&layoutKey==key)return cached;
        using var bitmap=new Bitmap(1,1);
        using var graphics=Graphics.FromImage(bitmap);
        using var font=new Font("Segoe UI",11.5f*scale,FontStyle.Regular,GraphicsUnit.Pixel);
        using var format=(StringFormat)StringFormat.GenericTypographic.Clone();
        format.FormatFlags|=StringFormatFlags.NoWrap;
        float Measure(string text)=>graphics.MeasureString(text,font,int.MaxValue,format).Width;
        float arrowX=4*scale;
        float valueX=arrowX+Math.Max(Measure("↓"),Measure("↑"))+3*scale;
        // Reserve the longest normal speed once, so changing digits do not shift the columns.
        float networkWidth=Measure("1023.9 MB/s");
        float statsX=valueX+networkWidth+8*scale;
        float labelWidth=Math.Max(settings.ShowCpu?Measure("CPU"):0,settings.ShowMemory?Measure("内存"):0);
        float statsValueX=statsX+labelWidth+5*scale;
        float percentRight=statsValueX+Measure("100%");
        float width=(settings.ShowCpu||settings.ShowMemory?percentRight:valueX+networkWidth)+4*scale;
        layoutKey=key;cachedLayout=new(arrowX,valueX,statsX,statsValueX,percentRight,width);
        return cachedLayout.Value;
    }
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
            // Win11 taskbar is hosted by DirectComposition. An ordinary GDI child can receive
            // WM_PAINT and hit tests while being absent from the composed screen. Give the
            // child its own layered surface, using constant alpha so GDI text stays opaque.
            CreateHandle(new Forms.CreateParams{Caption="NetworkSpeedMonitor 任务栏",Parent=taskbar,Style=0x40000000|0x04000000,ExStyle=0x00080080});
            if(!Native.SetLayeredWindowAttributes(Handle,0,255,2))Paths.Log("Taskbar layered surface: "+System.Runtime.InteropServices.Marshal.GetLastWin32Error());
        }
        Native.GetWindowRect(taskbar,out var bar);
        var tray=Native.FindWindowEx(taskbar,IntPtr.Zero,"TrayNotifyWnd",null);
        if(tray==IntPtr.Zero||!Native.GetWindowRect(tray,out var trayRect)){Hide("系统托盘布局尚未就绪");return;}
        double dpi=Math.Max(96,Native.GetDpiForWindow(taskbar))/96.0;
        int width=(int)Math.Ceiling(Layout((float)dpi).Width),height=Math.Min(bar.Height,(int)(46*dpi));
        int right=trayRect.Left-(int)(4*dpi)-settings.TaskbarOffset;
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
        paintCount++;
        var dc=Native.BeginPaint(Handle,out var paint);
        try
        {
            Native.GetWindowRect(Handle,out var r);if(r.Width<=0||r.Height<=0)return;
            using var bitmap=new Bitmap(r.Width,r.Height);using var g=Graphics.FromImage(bitmap);
            bool light=false;try{light=(int?)Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize","SystemUsesLightTheme",0)==1;}catch{}
            g.Clear(light?Color.FromArgb(243,243,243):Color.FromArgb(31,32,36));
            float scale=Math.Max(96,Native.GetDpiForWindow(taskbar))/96f;
            var layout=Layout(scale);
            using var font=new Font("Segoe UI",11.5f*scale,FontStyle.Regular,GraphicsUnit.Pixel);
            using var format=(StringFormat)StringFormat.GenericTypographic.Clone();
            format.FormatFlags|=StringFormatFlags.NoWrap|StringFormatFlags.NoClip;
            using var rightAligned=(StringFormat)format.Clone();rightAligned.Alignment=StringAlignment.Far;
            using var accent=new SolidBrush(light?Color.FromArgb(0,95,120):Color.FromArgb(102,221,223));
            using var text=new SolidBrush(light?Color.FromArgb(30,38,47):Color.FromArgb(241,244,249));
            float y1=Math.Max(1,(r.Height-32*scale)/2),y2=y1+17*scale;
            void Draw(string label,string value,float y){g.DrawString(label,font,accent,new PointF(layout.ArrowX,y),format);g.DrawString(value,font,text,new PointF(layout.ValueX,y),format);}
            Draw("↓",Format.Size(snapshot?.Sample.Download,true),y1);Draw("↑",Format.Size(snapshot?.Sample.Upload,true),y2);
            if(settings.ShowCpu||settings.ShowMemory)
            {
                using var labelBrush=new SolidBrush(light?Color.DimGray:Color.FromArgb(164,173,188));
                void Stat(string label,string value,float y){g.DrawString(label,font,labelBrush,new PointF(layout.StatsX,y),format);g.DrawString(value,font,text,new RectangleF(layout.StatsValueX,y,layout.PercentRight-layout.StatsValueX,17*scale),rightAligned);}
                if(settings.ShowCpu)Stat("CPU",Format.Percent(snapshot?.Sample.Cpu),y1);
                if(settings.ShowMemory)Stat("内存",Format.Percent(snapshot is null||snapshot.TotalMemory==0?null:snapshot.Sample.Memory/snapshot.TotalMemory*100),y2);
            }
            using var target=Graphics.FromHdc(dc);target.DrawImageUnscaled(bitmap,0,0);
        }finally{Native.EndPaint(Handle,ref paint);}
    }
    public string Diagnostics(bool captureVisible=true)
    {
        var hit=Native.WindowFromPoint(new Native.Point{X=(Anchor.Left+Anchor.Right)/2,Y=(Anchor.Top+Anchor.Bottom)/2});
        string Class(IntPtr h){var text=new System.Text.StringBuilder(256);Native.GetClassName(h,text,text.Capacity);return text.ToString();}
        int screenTextPixels=0,screenAccentPixels=0;string? visualCheckError=null;
        if(captureVisible&&Visible&&Anchor.Width>0&&Anchor.Height>0)
        {
            // Regression check against the composed screen, restricted to this application's own strip.
            // A successful WM_PAINT or hit test alone does not prove that DWM presents the child window.
            try
            {
                using var screen=new Bitmap(Anchor.Width,Anchor.Height);
                using(var graphics=Graphics.FromImage(screen))graphics.CopyFromScreen(Anchor.Left,Anchor.Top,0,0,screen.Size,CopyPixelOperation.SourceCopy);
                for(int y=4;y<screen.Height-4;y+=2)for(int x=4;x<screen.Width-4;x+=2){var pixel=screen.GetPixel(x,y);if(pixel.R>205&&pixel.G>205&&pixel.B>205)screenTextPixels++;if(pixel.R>65&&pixel.R<150&&pixel.G>180&&pixel.B>180)screenAccentPixels++;}
                screen.Save(System.IO.Path.Combine(Paths.Data,"taskbar-visible.png"),System.Drawing.Imaging.ImageFormat.Png);
            }catch(Exception ex){visualCheckError=ex.Message;}
        }
        return JsonSerializer.Serialize(new{Status,Visible,Handle=Handle.ToInt64(),Taskbar=taskbar.ToInt64(),Anchor=new{Anchor.Left,Anchor.Top,Anchor.Right,Anchor.Bottom},buttonRight,occupancyKnown,paintCount,NativeVisible=Native.IsWindowVisible(Handle),HitWindow=hit.ToInt64(),HitClass=Class(hit),Parent=Native.GetParent(Handle).ToInt64(),PreviousSibling=Class(Native.GetWindow(Handle,3)),NextSibling=Class(Native.GetWindow(Handle,2)),screenTextPixels,screenAccentPixels,visualCheckError,VisualCheckPassed=captureVisible&&Native.IsWindowVisible(Handle)&&hit==Handle&&paintCount>0&&screenTextPixels>20&&screenAccentPixels>5});
    }
    public void Dispose(){disposed=true;timer.Stop();if(Handle!=IntPtr.Zero)DestroyHandle();}
}
