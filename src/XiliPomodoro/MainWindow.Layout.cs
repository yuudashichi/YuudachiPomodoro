using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace XiliPomodoro;

public sealed partial class MainWindow
{
    const double FocusStackBreakpoint = 680;
    Grid focusMainCard = null!, timerPanel = null!, tasksPanel = null!, focusHeader = null!;
    StackPanel focusStats = null!;
    ScrollViewer focusScroller = null!;
    Border heatCard = null!;
    Viewbox timerViewbox = null!;
    bool? narrowFocus, compactHeader;

    void UpdateResponsiveLayout()
    {
        if (focusPage.ActualWidth <= 0 || heatCard == null) return;
        var narrow = focusPage.ActualWidth < FocusStackBreakpoint;
        if (narrowFocus != narrow)
        {
            narrowFocus = narrow;
            focusMainCard.ColumnDefinitions[0].Width = new GridLength(narrow ? 1 : 1.12, GridUnitType.Star);
            focusMainCard.ColumnDefinitions[1].Width = narrow ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
            focusMainCard.RowDefinitions[0].Height = narrow ? new GridLength(306) : new GridLength(1, GridUnitType.Star);
            focusMainCard.RowDefinitions[1].Height = narrow ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
            Grid.SetColumn(tasksPanel, narrow ? 0 : 1);
            Grid.SetRow(tasksPanel, narrow ? 1 : 0);
            tasksPanel.BorderThickness = narrow ? new Thickness(0, 1, 0, 0) : new Thickness(1, 0, 0, 0);
            HideHeatTip(); timerPointerInside = false; UpdateTimerHover();
        }
        var height = narrow
            ? 306 + Math.Clamp(focusScroller.ActualHeight * 0.55, 250, 380)
            : Math.Clamp(focusScroller.ActualHeight - heatCard.ActualHeight - 20, 316, 560);
        if (Math.Abs(focusMainCard.Height - height) > 0.1 || double.IsNaN(focusMainCard.Height)) focusMainCard.Height = height;
        var compact = focusPage.ActualWidth < 540;
        if (compactHeader != compact)
        {
            compactHeader = compact;
            Grid.SetRow(focusStats, compact ? 1 : 0); Grid.SetColumn(focusStats, compact ? 0 : 1);
            Grid.SetColumnSpan(focusStats, compact ? 2 : 1);
            focusStats.HorizontalAlignment = compact ? HorizontalAlignment.Left : HorizontalAlignment.Right;
            todayTime.FontSize = todayCount.FontSize = compact ? 17 : 20;
        }
    }
}
