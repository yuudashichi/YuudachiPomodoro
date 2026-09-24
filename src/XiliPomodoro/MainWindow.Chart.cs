using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using XiliPomodoro.Core;
using Windows.Foundation;
using Windows.System;

namespace XiliPomodoro;

public sealed partial class MainWindow
{
    DateTime chartMonth = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    Canvas chartCanvas = null!;
    Grid chartHost = null!;
    TextBlock chartMonthText = null!, chartTotal = null!, chartTipText = null!;
    Button chartNext = null!, chartPrevious = null!;
    Border? chartTip;
    Line? chartGuide;
    Ellipse? chartDot;
    IReadOnlyList<FocusDay> chartDays = [];
    readonly List<Point> chartPoints = [];
    int chartSelected = -1;
    bool chartDrawQueued;
    readonly List<Line> chartGridLines = [];
    readonly List<TextBlock> chartYLabels = [], chartXLabels = [];
    readonly List<Ellipse> chartDayDots = [];
    Polyline? chartLine;
    Polygon? chartArea;

    void ResetChartResources()
    {
        CompositionTarget.Rendering -= DrawChartOnFrame; chartDrawQueued = false;
        chartGridLines.Clear(); chartYLabels.Clear(); chartXLabels.Clear(); chartDayDots.Clear(); chartLine = null; chartArea = null;
        chartTip = null; chartGuide = null; chartDot = null; chartSelected = -1; chartDays = [];
        chartCanvas = null!; chartHost = null!;
        chartMonthText = chartTotal = chartTipText = null!;
        chartNext = chartPrevious = null!; chartPoints.Clear();
    }

    Grid BuildChart()
    {
        ResetChartResources();
        var page = new Grid { RowSpacing = 24 };
        page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var header = new Grid(); header.ColumnDefinitions.Add(new ColumnDefinition()); header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var heading = Text("每日专注", 25); heading.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold; header.Children.Add(heading);
        var navigation = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        chartPrevious = IconButton("\uE76B", "上个月", () => ChangeChartMonth(-1)); navigation.Children.Add(chartPrevious);
        chartMonthText = Text("", 14); navigation.Children.Add(chartMonthText);
        chartNext = IconButton("\uE76C", "下个月", () => ChangeChartMonth(1)); navigation.Children.Add(chartNext);
        Place(header, navigation, 0, 1); page.Children.Add(header);
        var content = new Grid { RowSpacing = 28 };
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var summary = new StackPanel { Spacing = 8 }; summary.Children.Add(Text("本月累计", 12, true)); chartTotal = Text("0 分钟", 30); summary.Children.Add(chartTotal); content.Children.Add(summary);
        chartHost = new Grid { MinHeight = 180, Background = Brush("00000000") };
        chartCanvas = new Canvas { IsHitTestVisible = false }; chartHost.Children.Add(chartCanvas);
        chartHost.SizeChanged += (_, _) => { if (chartOpen) QueueChartDraw(); };
        chartHost.Unloaded += (_, _) => { CompositionTarget.Rendering -= DrawChartOnFrame; chartDrawQueued = false; };
        chartHost.PointerMoved += (_, e) =>
        {
            var p = e.GetCurrentPoint(chartHost).Position;
            if (chartPoints.Count == 0 || p.X < 52 || p.X > chartHost.ActualWidth - 20 || p.Y < 18 || p.Y > chartHost.ActualHeight - 38) { HideChartTip(); return; }
            var index = Enumerable.Range(0, chartPoints.Count).MinBy(i => Math.Abs(chartPoints[i].X - p.X));
            ShowChartTip(index);
        };
        chartHost.PointerExited += (_, _) => HideChartTip();
        chartHost.PointerCaptureLost += (_, _) => HideChartTip();
        // One keyboard target, rather than dozens of tab stops.
        var keyboardTarget = new Button { Background = Brush("00000000"), BorderThickness = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch, Opacity = 0 };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(keyboardTarget, "每日专注折线图，使用左右方向键查看日期");
        keyboardTarget.GotFocus += (_, _) => { if (keyboardTarget.FocusState == FocusState.Keyboard) ShowChartTip(Math.Max(0, chartDays.Count - 1)); };
        keyboardTarget.LostFocus += (_, _) => HideChartTip();
        keyboardTarget.KeyDown += (_, e) =>
        {
            if (e.Key is VirtualKey.Left or VirtualKey.Right && chartDays.Count > 0)
            {
                ShowChartTip(Math.Clamp(chartSelected + (e.Key == VirtualKey.Left ? -1 : 1), 0, chartDays.Count - 1)); e.Handled = true;
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(keyboardTarget, chartTipText.Text);
            }
        };
        chartHost.Children.Insert(0, keyboardTarget);
        Place(content, chartHost, 1); Place(page, Card(content), 1);
        return page;
    }
    void ChangeChartMonth(int delta)
    {
        var next = chartMonth.AddMonths(delta);
        if (next < new DateTime(2000, 1, 1) || next > DateTime.Today) return;
        chartMonth = next; RefreshChart();
    }
    void QueueChartDraw()
    {
        if (chartDrawQueued) return;
        chartDrawQueued = true;
        CompositionTarget.Rendering += DrawChartOnFrame;
    }
    void DrawChartOnFrame(object? sender, object args)
    {
        CompositionTarget.Rendering -= DrawChartOnFrame; chartDrawQueued = false;
        if (chartOpen && !hidden) DrawChart();
    }
    void RefreshChart()
    {
        if (chartCanvas == null || !chartOpen) return;
        var nextDays = FocusHistory.Month(data.Slices, chartMonth, DateTime.Today);
        var changed = !chartDays.SequenceEqual(nextDays);
        chartDays = nextDays;
        chartMonthText.Text = chartMonth.ToString("yyyy年M月");
        chartPrevious.IsEnabled = chartMonth > new DateTime(2000, 1, 1);
        chartNext.IsEnabled = chartMonth.AddMonths(1) <= DateTime.Today;
        chartTotal.Text = FormatDuration(chartDays.Sum(d => d.Seconds));
        // Keep point controls and hover details stable during idle summary refreshes.
        if (changed || chartPoints.Count != chartDays.Count) DrawChart();
    }
    void DrawChart()
    {
        HideChartTip(); chartPoints.Clear();
        var width = chartHost.ActualWidth; var height = chartHost.ActualHeight;
        if (width < 100 || height < 100 || chartDays.Count == 0) return;
        const double left = 52, top = 18;
        var right = width - 20; var bottom = height - 38;
        var peak = Math.Max(30, chartDays.Max(d => d.Seconds) / 60);
        var step = peak <= 60 ? 15 : peak <= 120 ? 30 : peak <= 240 ? 60 : Math.Ceiling(peak / 240) * 60;
        var ceiling = Math.Ceiling(peak / step) * step;
        var yCount = (int)(ceiling / step) + 1;
        for (int i = 0; i < yCount; i++)
        {
            var minutes = i * step; var y = bottom - minutes / ceiling * (bottom - top);
            var gridLine = ReuseChartElement(chartGridLines, i, () => new Line { Stroke = line, StrokeThickness = 1 });
            gridLine.X1 = left; gridLine.X2 = right; gridLine.Y1 = gridLine.Y2 = y;
            var label = ReuseChartElement(chartYLabels, i, () => Text("", 11, true));
            label.Text = minutes >= 60 ? $"{minutes / 60:0.#} 时" : $"{minutes:0} 分"; label.Width = 44; label.TextAlignment = TextAlignment.Right; Canvas.SetTop(label, y - 8);
        }
        for (int i = 0; i < chartDays.Count; i++)
        {
            var x = chartDays.Count == 1 ? (left + right) / 2 : left + i * (right - left) / (chartDays.Count - 1);
            chartPoints.Add(new Point(x, bottom - chartDays[i].Seconds / 60 / ceiling * (bottom - top)));
        }
        HideUnusedChartElements(chartGridLines, yCount); HideUnusedChartElements(chartYLabels, yCount);
        var xCount = 0;
        var labelStep = Math.Max(1, (int)Math.Ceiling(chartDays.Count / Math.Max(2, (right - left) / 70)));
        for (int i = 0; i < chartDays.Count; i++)
        {
            if (i != chartDays.Count - 1 && (i % labelStep != 0 || i > chartDays.Count - 1 - labelStep)) continue;
            var label = ReuseChartElement(chartXLabels, xCount++, () => Text("", 11, true)); label.Text = chartDays[i].Date.ToString("M/d"); label.Width = 48; label.TextAlignment = TextAlignment.Center;
            Canvas.SetLeft(label, chartPoints[i].X - 24); Canvas.SetTop(label, bottom + 14);
        }
        HideUnusedChartElements(chartXLabels, xCount);
        if (chartArea == null)
        {
            var fill = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            var color = accent.Color; color.A = 55; fill.GradientStops.Add(new GradientStop { Color = color, Offset = 0 }); color.A = 0; fill.GradientStops.Add(new GradientStop { Color = color, Offset = 1 });
            chartArea = new Polygon { Fill = fill }; chartCanvas.Children.Insert(0, chartArea);
            chartLine = new Polyline { Stroke = accent, StrokeThickness = 2.5, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round }; chartCanvas.Children.Add(chartLine);
        }
        chartArea.Points.Clear(); chartLine!.Points.Clear();
        if (chartPoints.Count > 1)
        {
            chartArea.Points.Add(new Point(chartPoints[0].X, bottom));
            foreach (var point in chartPoints) { chartArea.Points.Add(point); chartLine.Points.Add(point); }
            chartArea.Points.Add(new Point(chartPoints[^1].X, bottom));
        }
        for (var i = 0; i < chartPoints.Count; i++)
        {
            var dot = ReuseChartElement(chartDayDots, i, () => new Ellipse { Width = 5, Height = 5, Fill = accent });
            Canvas.SetLeft(dot, chartPoints[i].X - 2.5); Canvas.SetTop(dot, chartPoints[i].Y - 2.5);
        }
        HideUnusedChartElements(chartDayDots, chartPoints.Count);
        if (chartGuide == null)
        {
            chartGuide = new Line { Y1 = top, Y2 = bottom, Stroke = accent, StrokeThickness = 1, Opacity = 0.3, Visibility = Visibility.Collapsed }; chartCanvas.Children.Add(chartGuide);
            chartDot = new Ellipse { Width = 10, Height = 10, Fill = accent, Stroke = surface, StrokeThickness = 2, Visibility = Visibility.Collapsed }; chartCanvas.Children.Add(chartDot);
            chartTipText = Text("", 12);
            chartTip = new Border { Child = chartTipText, Padding = new Thickness(12, 9, 12, 9), CornerRadius = new CornerRadius(10), BorderBrush = line, BorderThickness = new Thickness(1), Background = new AcrylicBrush { TintColor = quiet.Color, TintOpacity = 0.7, FallbackColor = quiet.Color }, Visibility = Visibility.Collapsed, IsHitTestVisible = false };
            chartCanvas.Children.Add(chartTip);
            Canvas.SetZIndex(chartGuide, 10); Canvas.SetZIndex(chartDot, 11); Canvas.SetZIndex(chartTip, 12);
        }
        chartGuide.Y2 = bottom;
    }
    T ReuseChartElement<T>(List<T> pool, int index, Func<T> create) where T : FrameworkElement
    {
        if (index == pool.Count) { var element = create(); pool.Add(element); chartCanvas.Children.Add(element); }
        pool[index].Visibility = Visibility.Visible;
        return pool[index];
    }
    static void HideUnusedChartElements<T>(List<T> pool, int used) where T : FrameworkElement
    {
        for (var i = used; i < pool.Count; i++) pool[i].Visibility = Visibility.Collapsed;
    }
    void ShowChartTip(int index)
    {
        if (index < 0 || index >= chartPoints.Count || chartTip == null) return;
        chartSelected = index; var item = chartDays[index]; var point = chartPoints[index];
        chartTipText.Text = $"{item.Date:M月d日} · {FormatDuration(item.Seconds)}";
        chartTip.Visibility = chartGuide!.Visibility = chartDot!.Visibility = Visibility.Visible;
        chartGuide.X1 = chartGuide.X2 = point.X; Canvas.SetLeft(chartDot, point.X - 5); Canvas.SetTop(chartDot, point.Y - 5);
        chartTip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(chartTip, Math.Clamp(point.X + 14, 52, Math.Max(52, chartHost.ActualWidth - chartTip.DesiredSize.Width - 8)));
        Canvas.SetTop(chartTip, Math.Clamp(point.Y - chartTip.DesiredSize.Height - 14, 0, Math.Max(0, chartHost.ActualHeight - chartTip.DesiredSize.Height)));
    }
    void HideChartTip()
    {
        if (chartTip != null) chartTip.Visibility = Visibility.Collapsed;
        if (chartGuide != null) chartGuide.Visibility = Visibility.Collapsed;
        if (chartDot != null) chartDot.Visibility = Visibility.Collapsed;
        chartSelected = -1;
    }
}
