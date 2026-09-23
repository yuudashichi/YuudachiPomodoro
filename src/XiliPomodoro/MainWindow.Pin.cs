using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace XiliPomodoro;
public sealed partial class MainWindow
{
    Button pinButton = null!;
    Microsoft.UI.Xaml.Shapes.Path pinBody = null!;
    Line pinStem = null!;
    Button BuildPinButton()
    {
        pinButton = Button("", TogglePin); pinButton.Padding = new Thickness(10); pinButton.Background = Brush("00000000");
        var glyph = new Grid { Width = 24, Height = 24, RenderTransform = new RotateTransform { Angle = 35, CenterX = 12, CenterY = 12 } };
        pinBody = (Microsoft.UI.Xaml.Shapes.Path)Microsoft.UI.Xaml.Markup.XamlReader.Load("<Path xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Data='M 7,3 L 17,3 L 16,5 L 16,10 L 19,13 L 19,15 L 5,15 L 5,13 L 8,10 L 8,5 Z' StrokeThickness='1.6' StrokeLineJoin='Round'/>");
        pinStem = new Line { X1 = 12, X2 = 12, Y1 = 15, Y2 = 22, StrokeThickness = 1.6, StrokeEndLineCap = PenLineCap.Round };
        glyph.Children.Add(pinBody); glyph.Children.Add(pinStem);
        pinButton.Content = new Viewbox { Width = 20, Height = 20, Child = glyph };
        RefreshPin(); return pinButton;
    }
    void TogglePin()
    {
        data.Settings.AlwaysOnTop = !data.Settings.AlwaysOnTop; RefreshPin();
        if (AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter) presenter.IsAlwaysOnTop = data.Settings.AlwaysOnTop;
        Save();
    }
    void RefreshPin()
    {
        if (pinButton == null) return;
        var on = data.Settings.AlwaysOnTop;
        pinBody.Stroke = pinStem.Stroke = on ? accent : ink;
        pinBody.Fill = on ? accent : Brush("00000000");
        var name = on ? "取消置顶" : "窗口置顶";
        ToolTipService.SetToolTip(pinButton, name);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(pinButton, name);
    }
}
