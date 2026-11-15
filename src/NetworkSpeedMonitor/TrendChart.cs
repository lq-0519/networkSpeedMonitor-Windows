using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace NetworkSpeedMonitor;

internal sealed class TrendChart : FrameworkElement
{
    public IReadOnlyList<Sample> Samples {get;set;}=Array.Empty<Sample>();
    public Func<Sample,double?> Value {get;set;}=s=>s.Cpu;
    public Func<Sample,double?>? Secondary {get;set;}
    public string PrimaryLabel {get;set;}="CPU";
    public string SecondaryLabel {get;set;}="";
    public Func<double?,string> FormatValue {get;set;}=Format.Percent;
    public TimeSpan Range {get;set;}=TimeSpan.FromMinutes(10);
    public Color Color {get;set;}=Ui.Accent;
    public double? FixedMaximum {get;set;}
    public bool Compact {get;set;}
    public bool Mirrored {get;set;}

    private static readonly Color SecondColor=Color.FromRgb(153,159,250);
    private readonly ToolTip hoverTip;
    private readonly TextBlock hoverTime,hoverPrimary,hoverSecondary;
    private Point? pointer;
    private Sample[] points=Array.Empty<Sample>();
    private readonly List<Lane> lanes=new();
    private DateTimeOffset start,end;
    private IReadOnlyList<Sample>? cachedSamples;
    private Func<Sample,double?>? cachedValue,cachedSecondary;
    private TimeSpan cachedRange;
    private Size cachedSize;
    private bool cachedCompact;
    private bool cachedMirrored;
    private double? cachedMaximum;
    private Color cachedColor;

    private sealed record Lane(Rect Plot,StreamGeometry Line,double Maximum,Color Color,Func<Sample,double?> Selector,bool Downward);

    public TrendChart()
    {
        Cursor=Cursors.Cross;
        var content=new StackPanel();
        hoverTime=Ui.Text("",10,Ui.Muted);hoverTime.Margin=new Thickness(0,0,0,5);content.Children.Add(hoverTime);
        hoverPrimary=Ui.Text("",12,Ui.Accent,true);content.Children.Add(hoverPrimary);
        hoverSecondary=Ui.Text("",12,SecondColor,true);hoverSecondary.Margin=new Thickness(0,4,0,0);content.Children.Add(hoverSecondary);
        hoverTip=new ToolTip{Content=content,PlacementTarget=this,Placement=PlacementMode.Relative,IsHitTestVisible=false,StaysOpen=true};
        Unloaded+=(_,_)=>hoverTip.IsOpen=false;
        IsVisibleChanged+=(_,_)=>{if(!IsVisible){pointer=null;hoverTip.IsOpen=false;}};
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);pointer=e.GetPosition(this);InvalidateVisual();
    }
    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);pointer=null;hoverTip.IsOpen=false;InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);double width=ActualWidth,height=ActualHeight;if(width<12||height<16)return;
        dc.DrawRectangle(Brushes.Transparent,null,new Rect(0,0,width,height));
        if(!ReferenceEquals(cachedSamples,Samples)||cachedValue!=Value||cachedSecondary!=Secondary||cachedRange!=Range||cachedSize!=RenderSize||cachedCompact!=Compact||cachedMirrored!=Mirrored||cachedMaximum!=FixedMaximum||cachedColor!=Color)
            Rebuild(width,height);

        bool mirrored=Mirrored&&lanes.Count==2;
        if(mirrored)
        {
            var axis=lanes[0].Plot;
            var axisPen=new Pen(Ui.Brush(Ui.Muted),Compact?0.7:1);
            dc.DrawLine(axisPen,new Point(axis.Left,axis.Bottom),new Point(axis.Right,axis.Bottom));
            if(!Compact)for(int tick=0;tick<=4;tick++){double x=axis.Left+axis.Width*tick/4;dc.DrawLine(axisPen,new Point(x,axis.Bottom-3),new Point(x,axis.Bottom+3));}
        }

        Sample? selected=null;DateTimeOffset? selectedTime=null;double? cursorX=null;
        if(pointer is {} mouse&&lanes.Count>0)
        {
            var plot=lanes[0].Plot;
            double fraction=Math.Clamp((mouse.X-plot.Left)/Math.Max(1,plot.Width),0,1);
            selectedTime=start+TimeSpan.FromSeconds(Range.TotalSeconds*fraction);
            selected=Closest(selectedTime.Value);
            cursorX=selected is null?plot.Left+fraction*plot.Width:TimeX(selected.Time,plot);
        }

        for(int index=0;index<lanes.Count;index++)
        {
            var lane=lanes[index];var plot=lane.Plot;string label=index==0?PrimaryLabel:SecondaryLabel;
            if(!Compact)
            {
                double labelY=mirrored&&index==1?plot.Bottom+4:plot.Top-20;
                Text(dc,label,plot.Left,labelY,11,lane.Color);
                double? reading=pointer!=null?(selected is null?null:lane.Selector(selected)):(points.Length==0?null:lane.Selector(points[^1]));
                RightText(dc,FormatValue(reading),plot.Right,labelY,11,lane.Color);
                var gridPen=new Pen(Ui.Brush(Ui.Stroke),0.7);
                for(int row=0;row<3;row++)
                {
                    if(mirrored&&((index==0&&row==2)||(index==1&&row==0)))continue;
                    double y=plot.Top+plot.Height*row/2;dc.DrawLine(gridPen,new Point(plot.Left,y),new Point(plot.Right,y));
                }
            }
            else if(lanes.Count>1)
            {
                string shortLabel=label switch{"下载"=>"↓","上传"=>"↑","读取"=>"读","写入"=>"写","CPU 温度"=>"C","GPU 温度"=>"G",_=>label.Length==0?"":label[..1]};
                Text(dc,shortLabel,0,plot.Top+Math.Max(0,(plot.Height-10)/2),8,lane.Color);
                if(!mirrored)dc.DrawLine(new Pen(Ui.Brush(Ui.Stroke),0.6),new Point(plot.Left,plot.Bottom),new Point(plot.Right,plot.Bottom));
            }
            dc.PushClip(new RectangleGeometry(new Rect(plot.Left-1,plot.Top-3,plot.Width+2,plot.Height+6)));
            dc.DrawGeometry(null,new Pen(Ui.Brush(lane.Color),Compact?1.35:1.8),lane.Line);
            if(cursorX is double x)
            {
                var guide=new Pen(Ui.Brush(Ui.Muted),0.8){DashStyle=DashStyles.Dot};
                dc.DrawLine(guide,new Point(x,plot.Top),new Point(x,plot.Bottom));
                var value=selected is null?null:lane.Selector(selected);
                if(value is double v&&double.IsFinite(v))dc.DrawEllipse(Ui.Brush(lane.Color),new Pen(Ui.Brush(Ui.Background),1.3),new Point(x,ValueY(v,lane)),Compact?2.3:3.5,Compact?2.3:3.5);
            }
            dc.Pop();
        }
        if(!Compact)
        {
            string startText=start.ToLocalTime().ToString(Range.TotalHours>=1?"HH:mm":"HH:mm:ss");
            if(mirrored)
            {
                // One shared zero/time axis. Downward geometry changes presentation only;
                // uploads/writes retain their positive values in labels and hover readouts.
                double axisY=lanes[0].Plot.Bottom+3;
                var leftLabel=Formatted(startText,9,Ui.Muted);var rightLabel=Formatted("现在",9,Ui.Muted);
                var background=Ui.Brush(Ui.Background);
                dc.DrawRectangle(background,null,new Rect(2,axisY-1,leftLabel.Width+5,leftLabel.Height+2));
                dc.DrawRectangle(background,null,new Rect(width-rightLabel.Width-7,axisY-1,rightLabel.Width+5,rightLabel.Height+2));
                dc.DrawText(leftLabel,new Point(3,axisY));dc.DrawText(rightLabel,new Point(width-rightLabel.Width-3,axisY));
            }
            else
            {
                Text(dc,startText,3,height-17,10,Ui.Muted);
                RightText(dc,"现在",width-3,height-17,10,Ui.Muted);
            }
            if(points.Length==0)Text(dc,"运行后逐步积累历史",Math.Max(4,width/2-60),height/2-8,11,Ui.Muted);
        }
        if(pointer is {} p&&selectedTime is {} time)
        {
            hoverTime.Text=(selected?.Time??time).ToLocalTime().ToString("MM-dd HH:mm:ss")+(selected==null?" · 采样缺口":"");
            hoverPrimary.Text=PrimaryLabel+"   "+FormatValue(selected is null?null:Value(selected));hoverPrimary.Foreground=Ui.Brush(Color);
            hoverSecondary.Visibility=Secondary==null?Visibility.Collapsed:Visibility.Visible;
            hoverSecondary.Text=SecondaryLabel+"   "+FormatValue(selected is null||Secondary==null?null:Secondary(selected));
            hoverTip.HorizontalOffset=Math.Clamp(p.X-90,0,Math.Max(0,width-190));hoverTip.VerticalOffset=p.Y-(Secondary==null?70:92);
            if(!hoverTip.IsOpen)hoverTip.IsOpen=true;
        }
    }

    private void Rebuild(double width,double height)
    {
        cachedSamples=Samples;cachedValue=Value;cachedSecondary=Secondary;cachedRange=Range;cachedSize=RenderSize;cachedCompact=Compact;cachedMirrored=Mirrored;cachedMaximum=FixedMaximum;cachedColor=Color;
        end=DateTimeOffset.UtcNow;start=end-Range;
        points=Samples.Where(s=>s.Time>=start&&s.Time<=end).OrderBy(s=>s.Time).ToArray();lanes.Clear();
        int count=Secondary==null?1:2;
        bool mirrored=Mirrored&&count==2;
        double sharedMaximum=mirrored?(FixedMaximum??Math.Max(1,points.Select(Value).Concat(points.Select(Secondary!)).Where(v=>v is double n&&double.IsFinite(n)).Select(v=>v!.Value).DefaultIfEmpty(0).Max()*1.1)):0;
        double bottom=Compact?1:24,gap=count==1?0:Compact?7:16;
        double bandHeight=(height-bottom-gap*(count-1))/count;
        for(int index=0;index<count;index++)
        {
            var selector=index==0?Value:Secondary!;var color=index==0?Color:SecondColor;
            double header=Compact?1:23;
            double left=Compact?(count>1?12:1):3;
            double plotWidth=Math.Max(1,width-(Compact?(count>1?13:2):6));
            Rect plot;
            if(mirrored)
            {
                double top=Compact?1:23,plotBottom=height-(Compact?1:23);
                double middle=(top+plotBottom)/2;
                plot=index==0?new Rect(left,top,plotWidth,Math.Max(2,middle-top)):new Rect(left,middle,plotWidth,Math.Max(2,plotBottom-middle));
            }
            else plot=new Rect(left,index*(bandHeight+gap)+header,plotWidth,Math.Max(2,bandHeight-header-2));
            bool downward=mirrored&&index==1;
            double max=mirrored?sharedMaximum:(FixedMaximum??Math.Max(1,points.Select(selector).Where(v=>v is double n&&double.IsFinite(n)).Select(v=>v!.Value).DefaultIfEmpty(0).Max()*1.1));
            var geometry=new StreamGeometry();
            using(var context=geometry.Open())
            {
                bool drawing=false;Sample? previous=null;
                // Cache geometry between samples, preserving extrema and gaps within each pixel bucket.
                foreach(var bucket in points.GroupBy(s=>(int)((s.Time-start).TotalSeconds/Math.Max(1,Range.TotalSeconds)*plot.Width)))
                {
                    var list=bucket.ToList();
                    var candidates=new[]{list[0],list[^1],list.MinBy(s=>selector(s)??double.PositiveInfinity)!,list.MaxBy(s=>selector(s)??double.NegativeInfinity)!};
                    foreach(var sample in candidates.Concat(list.Where(s=>selector(s)==null)).DistinctBy(s=>s.Time).OrderBy(s=>s.Time))
                    {
                        var value=selector(sample);
                        if(value is not double v||!double.IsFinite(v)){drawing=false;previous=sample;continue;}
                        var point=new Point(TimeX(sample.Time,plot),PlotY(v,plot,max,downward));
                        if(!drawing||previous==null||previous.Segment!=sample.Segment||(sample.Time-previous.Time).TotalSeconds>20){context.BeginFigure(point,false,false);drawing=true;}else context.LineTo(point,true,false);
                        previous=sample;
                    }
                }
            }
            geometry.Freeze();lanes.Add(new(plot,geometry,max,color,selector,downward));
        }
    }
    private Sample? Closest(DateTimeOffset time)
    {
        if(points.Length==0)return null;
        int lo=0,hi=points.Length;
        while(lo<hi){int mid=lo+(hi-lo)/2;if(points[mid].Time<time)lo=mid+1;else hi=mid;}
        var result=points[Math.Min(lo,points.Length-1)];
        if(lo>0&&Math.Abs((points[lo-1].Time-time).TotalSeconds)<Math.Abs((result.Time-time).TotalSeconds))result=points[lo-1];
        return Math.Abs((result.Time-time).TotalSeconds)<=8?result:null;
    }
    private double TimeX(DateTimeOffset time,Rect plot)=>plot.Left+(time-start).TotalSeconds/Math.Max(1,Range.TotalSeconds)*plot.Width;
    private static double PlotY(double value,Rect plot,double maximum,bool downward)=>downward?plot.Top+Math.Clamp(value/maximum,0,1)*plot.Height:plot.Bottom-Math.Clamp(value/maximum,0,1)*plot.Height;
    private static double ValueY(double value,Lane lane)=>PlotY(value,lane.Plot,lane.Maximum,lane.Downward);
    private void Text(DrawingContext dc,string text,double x,double y,double size,Color color)=>dc.DrawText(Formatted(text,size,color),new Point(x,y));
    private void RightText(DrawingContext dc,string text,double right,double y,double size,Color color){var formatted=Formatted(text,size,color);dc.DrawText(formatted,new Point(Math.Max(0,right-formatted.Width),y));}
    private FormattedText Formatted(string text,double size,Color color)=>new(text,CultureInfo.CurrentCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),size,Ui.Brush(color),VisualTreeHelper.GetDpi(this).PixelsPerDip);
}
