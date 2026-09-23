using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace XiliPomodoro;

public sealed partial class MainWindow
{
    Button NavigationButton(UIElement icon, string label, Action action)
    {
        var button = IconButton("", label, action);
        button.Content = icon;
        button.Width = button.Height = 44;
        button.Padding = new Thickness(0);
        button.HorizontalAlignment = HorizontalAlignment.Center;
        return button;
    }

    UIElement BuildAlarmIcon()
    {
        return (UIElement)Microsoft.UI.Xaml.Markup.XamlReader.Load($"""
            <Viewbox xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" Width="26" Height="26">
              <Canvas Width="32" Height="32">
                <Path Fill="{accent.Color}" Data="M4,10 Q1,5 6,3 Q10,1 12,5 Z M20,5 Q23,1 27,3 Q32,5 28,10 Z"/>
                <Ellipse Width="22" Height="22" Canvas.Left="5" Canvas.Top="7" Fill="{quiet.Color}" Stroke="{ink.Color}" StrokeThickness="1.7"/>
                <Path Stroke="{ink.Color}" StrokeThickness="1.8" StrokeStartLineCap="Round" StrokeEndLineCap="Round" Data="M16,12 L16,18 L21,21 M9,27 L6,30 M23,27 L26,30"/>
                <Ellipse Width="3" Height="3" Canvas.Left="14.5" Canvas.Top="16.5" Fill="{accent.Color}"/>
              </Canvas>
            </Viewbox>
            """);
    }

    UIElement BuildChartIcon() => (UIElement)Microsoft.UI.Xaml.Markup.XamlReader.Load($"""
        <Viewbox xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" Width="26" Height="26">
          <Canvas Width="32" Height="32">
            <Path Stroke="{ink.Color}" StrokeThickness="1.8" StrokeStartLineCap="Round" StrokeEndLineCap="Round" StrokeLineJoin="Round" Data="M5,4 L5,28 L29,28"/>
            <Path Stroke="{accent.Color}" StrokeThickness="2.6" StrokeStartLineCap="Round" StrokeEndLineCap="Round" StrokeLineJoin="Round" Data="M9,21 L15,14 L21,18 L27,7"/>
          </Canvas>
        </Viewbox>
        """);
}
