using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace XiliPomodoro;
public sealed partial class MainWindow
{
    Grid heatCells = null!, monthLabels = null!, dayLabels = null!, heatViewport = null!;
    TextBlock yearLabel = null!, heatTipText = null!;
    Button nextYear = null!;
    ControlTemplate? heatTemplate;
    readonly Dictionary<string, Button> heatButtons = new();
    readonly Dictionary<string, int> heatLevels = new();
    int builtYear;
    double heatSide;
    bool? sparseMonthLabels;
    Border? heatTip;
    Button? hoveredHeatCell;
    Windows.Foundation.Point heatTipAnchor;
    Border BuildHeatmap()
    {
        builtYear = 0; heatSide = 0; sparseMonthLabels = null; heatButtons.Clear(); heatLevels.Clear();
        var content = new StackPanel { Spacing = 12 };
        var heading = new Grid(); heading.ColumnDefinitions.Add(new ColumnDefinition()); heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 }; var t = Text("专注记录", 16); t.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold; title.Children.Add(t); heading.Children.Add(title);
        var switcher = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
        switcher.Children.Add(IconButton("\uE76B", "上一年", () => { if (year > 2000) { year--; RefreshHeatmap(); } })); yearLabel = Text(year.ToString(), 12); switcher.Children.Add(yearLabel);
        nextYear = IconButton("\uE76C", "下一年", () => { if (year < DateTime.Now.Year) { year++; RefreshHeatmap(); } }); switcher.Children.Add(nextYear); Place(heading, switcher, 0, 1); content.Children.Add(heading);
        foreach (var nav in switcher.Children.OfType<Button>()) nav.Padding = new Thickness(5);
        var calendar = new Grid { ColumnSpacing = 10, RowSpacing = 6 }; calendar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) }); calendar.ColumnDefinitions.Add(new ColumnDefinition());
        calendar.RowDefinitions.Add(new RowDefinition { Height = new GridLength(18) }); calendar.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        monthLabels = new Grid { ColumnSpacing = 3, UseLayoutRounding = false }; Place(calendar, monthLabels, 0, 1);
        dayLabels = new Grid { RowSpacing = 3, UseLayoutRounding = false }; for (int i = 0; i < 7; i++) dayLabels.RowDefinitions.Add(new RowDefinition());
        for (int i = 0; i < 7; i += 2) { var label = Text(new[] { "一", "二", "三", "四", "五", "六", "日" }[i], 10, true); Place(dayLabels, label, i); } Place(calendar, dayLabels, 1);
        heatCells = new Grid { ColumnSpacing = 3, RowSpacing = 3, UseLayoutRounding = false };
        heatViewport = new Grid(); heatViewport.Children.Add(new Viewbox { Child = heatCells, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Top }); Place(calendar, heatViewport, 1, 1);
        heatViewport.SizeChanged += (_, _) => LayoutHeatmap(); content.Children.Add(calendar);
        var legend = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, HorizontalAlignment = HorizontalAlignment.Right }; legend.Children.Add(Text("少", 10, true));
        for (int i = 0; i < 5; i++) legend.Children.Add(new Border { Width = 10, Height = 10, CornerRadius = new CornerRadius(2), Background = HeatBrush(i) }); legend.Children.Add(Text("多", 10, true)); content.Children.Add(legend);
        RefreshHeatmap(); return Card(content, new Thickness(23, 15, 23, 17));
    }
    SolidColorBrush HeatBrush(int level) => Brush((dark ? new[] { "434C3A", "657054", "899775", "B0BD97", "D4DEBA" } : new[] { "ECEFE1", "E0E7CC", "C8D3AD", "ACBC90", "869D70" })[level]);
    void LayoutHeatmap()
    {
        var columns = heatCells.ColumnDefinitions.Count;
        if (columns == 0 || heatViewport.ActualWidth <= 0) return;
        // The year grid keeps fixed internal geometry. Viewbox scales the retained
        // visual tree without measuring and arranging hundreds of day controls again.
        var ratio = heatViewport.ActualWidth / heatCells.Width;
        var side = 12 * ratio;
        if (Math.Abs(side - heatSide) < 0.001) return;
        HideHeatTip(); heatSide = side;
        monthLabels.ColumnSpacing = dayLabels.RowSpacing = 3 * ratio;
        foreach (var row in dayLabels.RowDefinitions) row.Height = new GridLength(side);
        dayLabels.Height = heatCells.Height * ratio;
        var sparse = heatViewport.ActualWidth < 470;
        if (sparseMonthLabels != sparse)
        {
            sparseMonthLabels = sparse;
            for (var i = 0; i < monthLabels.Children.Count; i++)
            {
                var label = (TextBlock)monthLabels.Children[i];
                label.Visibility = sparse && i % 2 == 1 ? Visibility.Collapsed : Visibility.Visible;
                Grid.SetColumnSpan(label, Math.Min(sparse ? 8 : 4, columns - Grid.GetColumn(label)));
            }
        }
    }
    void RefreshHeatmap()
    {
        if (heatCells == null) return;
        if (builtYear != year) BuildHeatCells();
        yearLabel.Text = year.ToString(); nextYear.IsEnabled = year < DateTime.Now.Year;
        var sums = data.Slices.GroupBy(s => s.Day).ToDictionary(g => g.Key, g => g.Sum(s => s.Seconds));
        var completedCounts = data.Tasks.Where(t => !t.Deleted && t.CompletedDay != null).GroupBy(t => t.CompletedDay!).ToDictionary(g => g.Key, g => g.Count());
        foreach (var (key, cell) in heatButtons)
        {
            var seconds = sums.GetValueOrDefault(key);
            var level = seconds <= 0 ? 0 : seconds < 1800 ? 1 : seconds < 3600 ? 2 : seconds < 7200 ? 3 : 4;
            if (!heatLevels.TryGetValue(key, out var previous) || previous != level) { cell.Background = HeatBrush(level); heatLevels[key] = level; }
            var date = (DateTime)cell.Tag;
            cell.Opacity = date > DateTime.Today ? 0.3 : 1;
            cell.IsEnabled = date <= DateTime.Today;
            cell.IsTabStop = date == DateTime.Today;
            if (cell != hoveredHeatCell) cell.BorderBrush = date == DateTime.Today ? accent : Brush("00000000");
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(cell, $"{date:yyyy年M月d日} 专注 {FormatDuration(seconds)} · 完成 {completedCounts.GetValueOrDefault(key)} 项");
        }
        if (hoveredHeatCell != null) ShowHeatTip(hoveredHeatCell, heatTipAnchor);
    }
    void BuildHeatCells()
    {
        HideHeatTip(); builtYear = year; heatSide = 0; sparseMonthLabels = null; heatButtons.Clear(); heatLevels.Clear();
        heatTemplate ??= (ControlTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Button'><Border Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' CornerRadius='{TemplateBinding CornerRadius}'/></ControlTemplate>");
        heatCells.Children.Clear(); heatCells.ColumnDefinitions.Clear(); heatCells.RowDefinitions.Clear(); monthLabels.Children.Clear(); monthLabels.ColumnDefinitions.Clear();
        var first = new DateTime(year, 1, 1); var offset = ((int)first.DayOfWeek + 6) % 7; var begin = first.AddDays(-offset); var count = DateTime.IsLeapYear(year) ? 366 : 365; var weeks = (int)Math.Ceiling((offset + count) / 7.0);
        for (int col = 0; col < weeks; col++) { heatCells.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) }); monthLabels.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); }
        for (int row = 0; row < 7; row++) heatCells.RowDefinitions.Add(new RowDefinition { Height = new GridLength(12) });
        heatCells.Width = weeks * 12 + (weeks - 1) * 3; heatCells.Height = 7 * 12 + 6 * 3;
        for (int month = 1; month <= 12; month++)
        {
            var col = (int)(new DateTime(year, month, 1) - begin).TotalDays / 7; var label = Text(month + "月", 10, true); label.TextWrapping = TextWrapping.NoWrap; Grid.SetColumn(label, col); Grid.SetColumnSpan(label, Math.Min(4, weeks - col)); monthLabels.Children.Add(label);
        }
        for (int n = 0; n < count; n++)
        {
            var date = first.AddDays(n); var key = date.ToString("yyyy-MM-dd");
            var cell = new Button { Tag = date, Template = heatTemplate, UseSystemFocusVisuals = true, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(1), Padding = new Thickness(0), MinWidth = 0, MinHeight = 0, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch, UseLayoutRounding = false };
            cell.PointerEntered += (_, e) => ShowHeatTip(cell, e.GetCurrentPoint(root).Position);
            cell.PointerMoved += (_, e) => { if (hoveredHeatCell != cell) ShowHeatTip(cell, e.GetCurrentPoint(root).Position); else PositionHeatTip(e.GetCurrentPoint(root).Position); };
            // PointerExited may report the last in-bounds point at rounded/subpixel edges.
            // The cell has a single border child, so exit itself is authoritative.
            cell.PointerExited += (_, _) => { if (cell == hoveredHeatCell) HideHeatTip(); };
            cell.PointerCaptureLost += (_, _) => { if (cell == hoveredHeatCell) HideHeatTip(); };
            cell.GotFocus += (_, _) => { if (cell.FocusState == FocusState.Keyboard) ShowHeatTip(cell); };
            cell.LostFocus += (_, _) => { if (cell == hoveredHeatCell) HideHeatTip(); };
            cell.Click += async (_, _) => { HideHeatTip(); if (date <= DateTime.Today) await ShowDay(date); };
            cell.KeyDown += (_, e) => { if (e.Key is Windows.System.VirtualKey.Left or Windows.System.VirtualKey.Right or Windows.System.VirtualKey.Up or Windows.System.VirtualKey.Down) { var delta = e.Key switch { Windows.System.VirtualKey.Left => -7, Windows.System.VirtualKey.Right => 7, Windows.System.VirtualKey.Up => -1, _ => 1 }; var index = heatCells.Children.IndexOf(cell) + delta; if (index >= 0 && index < heatCells.Children.Count && heatCells.Children[index] is Button b) b.Focus(FocusState.Keyboard); e.Handled = true; } };
            heatButtons[key] = cell; Place(heatCells, cell, (offset + n) % 7, (offset + n) / 7);
        }
        LayoutHeatmap();
    }
    void BuildHeatTip()
    {
        var overlay = new Canvas { IsHitTestVisible = false };
        Grid.SetRowSpan(overlay, 2);
        heatTipText = Text("", 12);
        heatTipText.LineHeight = 19;
        var material = new AcrylicBrush { TintColor = Brush(dark ? "46503B" : "FAFCF1").Color, TintOpacity = dark ? 0.65 : 0.55, TintLuminosityOpacity = 0.82, FallbackColor = Brush(dark ? "46503B" : "FAFCF1").Color };
        var edge = new LinearGradientBrush { StartPoint = new Windows.Foundation.Point(0, 0), EndPoint = new Windows.Foundation.Point(1, 1) };
        edge.GradientStops.Add(new GradientStop { Color = Brush(dark ? "40FFFFFF" : "E6FFFFFF").Color, Offset = 0 });
        edge.GradientStops.Add(new GradientStop { Color = Brush(dark ? "149CAA80" : "30939F78").Color, Offset = 1 });
        heatTip = new Border { Child = heatTipText, MaxWidth = 292, Padding = new Thickness(12, 9, 12, 9), CornerRadius = new CornerRadius(12), Background = material, BorderBrush = edge, BorderThickness = new Thickness(1), Shadow = new ThemeShadow(), Translation = new System.Numerics.Vector3(0, 0, 20), Visibility = Visibility.Collapsed, IsHitTestVisible = false };
        overlay.Children.Add(heatTip); root.Children.Add(overlay);
        // Also watch the root: movement into the calendar's gaps/header must clear stale tips,
        // even when a child handles pointer events or an exit arrives with a rounded coordinate.
        root.AddHandler(UIElement.PointerMovedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler((_, e) => DismissHeatTipOutside(e.GetCurrentPoint(root).Position)), true);
        root.PointerExited += (_, e) => { if (!Inside(root, e.GetCurrentPoint(root).Position)) HideHeatTip(); };
    }
    void DismissHeatTipOutside(Windows.Foundation.Point pointer)
    {
        if (hoveredHeatCell == null) return;
        var bounds = hoveredHeatCell.TransformToVisual(root).TransformBounds(new Windows.Foundation.Rect(0, 0, hoveredHeatCell.ActualWidth, hoveredHeatCell.ActualHeight));
        if (pointer.X < bounds.Left || pointer.X >= bounds.Right || pointer.Y < bounds.Top || pointer.Y >= bounds.Bottom) HideHeatTip();
    }
    void ShowHeatTip(Button cell, Windows.Foundation.Point? pointer = null)
    {
        if (heatTip == null || cell.XamlRoot == null) return;
        if ((DateTime)cell.Tag > DateTime.Today) { HideHeatTip(); return; }
        if (hoveredHeatCell != cell) HideHeatTip();
        hoveredHeatCell = cell; cell.BorderBrush = accent;
        var date = (DateTime)cell.Tag; var key = date.ToString("yyyy-MM-dd");
        var tasks = data.Tasks.Where(t => !t.Deleted && t.CompletedDay == key).ToList();
        heatTipText.Text = $"{date:yyyy年M月d日}\n专注 {FormatDuration(engine.SecondsOn(key))} · 完成 {tasks.Count} 项" + (tasks.Count == 0 ? "" : "\n" + string.Join("\n", tasks.Take(3).Select(t => "· " + (t.Title.Length > 40 ? t.Title[..40] + "…" : t.Title))) + (tasks.Count > 3 ? "\n点击查看全部" : ""));
        // No hit testing or layout participation: the overlay cannot displace the cell.
        heatTip.Visibility = Visibility.Visible; heatTip.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
        PositionHeatTip(pointer ?? cell.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point(cell.ActualWidth, cell.ActualHeight)));
    }
    void PositionHeatTip(Windows.Foundation.Point pointer)
    {
        if (heatTip == null) return;
        heatTipAnchor = pointer;
        var width = heatTip.DesiredSize.Width; var height = heatTip.DesiredSize.Height;
        var x = pointer.X + 16; var y = pointer.Y + 20;
        if (x + width > root.ActualWidth - 8) x = pointer.X - width - 16;
        if (y + height > root.ActualHeight - 8) y = pointer.Y - height - 16;
        Canvas.SetLeft(heatTip, Math.Clamp(x, 8, Math.Max(8, root.ActualWidth - width - 8)));
        Canvas.SetTop(heatTip, Math.Clamp(y, 40, Math.Max(40, root.ActualHeight - height - 8)));
    }
    void HideHeatTip()
    {
        if (heatTip != null) heatTip.Visibility = Visibility.Collapsed;
        if (hoveredHeatCell != null) hoveredHeatCell.BorderBrush = (DateTime)hoveredHeatCell.Tag == DateTime.Today ? accent : Brush("00000000");
        hoveredHeatCell = null;
    }
    async Task ShowDay(DateTime date)
    {
        var key = date.ToString("yyyy-MM-dd"); var panel = new StackPanel { Spacing = 16, MaxWidth = 420 };
        panel.Children.Add(Text("专注 " + FormatDuration(engine.SecondsOn(key)), 23));
        var completed = data.Tasks.Where(t => !t.Deleted && t.CompletedDay == key).ToList(); panel.Children.Add(Text("已完成任务 · " + completed.Count, 13, true));
        var list = new StackPanel { Spacing = 12 }; foreach (var t in completed) list.Children.Add(Text("✓  " + t.Title, 14));
        if (completed.Count == 0) list.Children.Add(Text("暂无已完成任务", 13, true));
        panel.Children.Add(new ScrollViewer { Content = list, MaxHeight = 300 }); await Dialog(date.ToString("yyyy年M月d日"), panel, "", "关闭");
    }
}
