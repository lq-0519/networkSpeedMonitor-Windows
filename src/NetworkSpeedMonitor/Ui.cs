using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NetworkSpeedMonitor;

internal static class Ui
{
    public static readonly Color Background=Color.FromRgb(21,25,34),Card=Color.FromRgb(30,36,48),Accent=Color.FromRgb(104,222,215),Muted=Color.FromRgb(151,166,188),Foreground=Color.FromRgb(237,242,250),Stroke=Color.FromRgb(46,56,72);
    public static SolidColorBrush Brush(Color c)=>new(c);
    public static TextBlock Text(string text,double size=13,Color? color=null,bool bold=false)=>new(){Text=text,FontSize=size,Foreground=Brush(color??Foreground),FontWeight=bold?FontWeights.SemiBold:FontWeights.Normal,VerticalAlignment=VerticalAlignment.Center,TextTrimming=TextTrimming.CharacterEllipsis};
    public static Button Button(string text,Action action,bool accent=false)
    {
        var b=new Button{Content=text,Foreground=Brush(accent?Background:Foreground),Background=Brush(accent?Accent:Card),BorderBrush=Brush(Stroke),BorderThickness=new Thickness(1),Padding=new Thickness(12,7,12,7),Cursor=System.Windows.Input.Cursors.Hand,FontSize=12};b.Click+=(_,_)=>action();return b;
    }
    public static Border Panel(UIElement child,Thickness? padding=null)=>new(){Child=child,Background=Brush(Card),BorderBrush=Brush(Stroke),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(10),Padding=padding??new Thickness(12)};
    public static void Space(Panel panel,double height=10)=>panel.Children.Add(new Border{Height=height});
}

internal sealed class TrendChart : FrameworkElement
{
    public IReadOnlyList<Sample> Samples {get;set;}=Array.Empty<Sample>();
    public Func<Sample,double?> Value {get;set;}=s=>s.Cpu;
    public Func<Sample,double?>? Secondary {get;set;}
    public TimeSpan Range {get;set;}=TimeSpan.FromMinutes(10);
    public Color Color {get;set;}=Ui.Accent;
    public double? FixedMaximum {get;set;}
    public bool Compact {get;set;}
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);double w=ActualWidth,h=ActualHeight;if(w<4||h<4)return;
        double bottom=Compact?0:22,left=Compact?0:3,plotHeight=h-bottom;
        var now=DateTimeOffset.UtcNow;var start=now-Range;
        var points=Samples.Where(s=>s.Time>=start).ToList();
        var values=points.Select(Value).Concat(Secondary is null?Array.Empty<double?>():points.Select(Secondary)).Where(v=>v is double d&&double.IsFinite(d)).Select(v=>v!.Value).ToList();
        double max=FixedMaximum??(values.Count==0?1:Math.Max(1,values.Max()*1.15));
        if(!Compact){var grid=new Pen(Ui.Brush(Ui.Stroke),1);for(int i=0;i<4;i++){double y=8+(plotHeight-14)*i/3;dc.DrawLine(grid,new(left,y),new(w,y));}Label(start.ToLocalTime().ToString(Range.TotalHours>=1?"HH:mm":"HH:mm:ss"),left,h-17,dc);Label("现在",w-28,h-17,dc);}
        if(values.Count==0){if(!Compact)Label("运行后逐步积累历史",Math.Max(4,w/2-60),h/2-8,dc);return;}
        void Series(Func<Sample,double?> selector,Color color)
        {
            var geometry=new StreamGeometry();
            using(var context=geometry.Open())
            {
                bool drawing=false;Sample? previous=null;
                // Preserve real minima/maxima in each pixel bucket, as well as segment boundaries.
                var reduced=new List<Sample>();
                foreach(var bucket in points.GroupBy(s=>(int)((s.Time-start).TotalSeconds/Range.TotalSeconds*Math.Max(1,w))))
                {
                    var list=bucket.ToList();var candidates=new[]{list.First(),list.Last(),list.MinBy(s=>selector(s)??double.PositiveInfinity)!,list.MaxBy(s=>selector(s)??double.NegativeInfinity)!};
                    reduced.AddRange(candidates.Concat(list.Where(s=>selector(s)==null)).DistinctBy(s=>s.Time).OrderBy(s=>s.Time));
                }
                foreach(var s in reduced)
                {
                    var value=selector(s);if(value is null||!double.IsFinite(value.Value)){drawing=false;previous=s;continue;}
                    double x=left+(s.Time-start).TotalSeconds/Range.TotalSeconds*(w-left),y=7+(1-Math.Clamp(value.Value/max,0,1))*(plotHeight-14);
                    var p=new Point(x,y);
                    if(!drawing||previous is null||previous.Segment!=s.Segment||(s.Time-previous.Time)>TimeSpan.FromSeconds(20)){context.BeginFigure(p,false,false);drawing=true;}else context.LineTo(p,true,false);
                    previous=s;
                }
            }
            geometry.Freeze();dc.DrawGeometry(null,new Pen(Ui.Brush(color),Compact?1.7:2),geometry);
        }
        Series(Value,Color);if(Secondary!=null)Series(Secondary,Color.FromRgb(153,159,250));
    }
    private static void Label(string text,double x,double y,DrawingContext dc)=>dc.DrawText(new FormattedText(text,CultureInfo.CurrentCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),10,Ui.Brush(Ui.Muted),1),new(x,y));
}
