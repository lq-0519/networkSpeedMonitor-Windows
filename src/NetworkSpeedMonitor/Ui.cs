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

