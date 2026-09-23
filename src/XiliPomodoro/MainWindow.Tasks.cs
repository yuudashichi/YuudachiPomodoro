using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using XiliPomodoro.Core;
using Windows.System;

namespace XiliPomodoro;
public sealed partial class MainWindow
{
    sealed class TaskRow
    {
        public required Grid Root;
        public required CheckBox Check;
        public required TextBlock Label;
        public required TextBlock Carry;
        public required Button Edit;
        public required Button Delete;
        public bool PointerInside;
    }
    readonly Dictionary<string, TaskRow> taskRows = new();
    UIElement? emptyTasks;
    UIElement BuildTasks()
    {
        taskRows.Clear(); emptyTasks = null;
        var grid = new Grid { BorderBrush = line, BorderThickness = new Thickness(1, 0, 0, 0), Padding = new Thickness(20, 20, 20, 18) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var heading = new Grid { Margin = new Thickness(0, 0, 0, 14) }; heading.ColumnDefinitions.Add(new ColumnDefinition()); heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = Text("今日任务", 17); title.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold; heading.Children.Add(title);
        tasksCount = Text("0 / 0", 12, true); Place(heading, tasksCount, 0, 1); grid.Children.Add(heading);
        taskList = new StackPanel { Spacing = 5 }; var scroll = new ScrollViewer { Content = taskList, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0, 0, 4, 0) }; Place(grid, scroll, 1);
        var inputRow = new Grid { Margin = new Thickness(0, 16, 0, 0), ColumnSpacing = 8 }; inputRow.ColumnDefinitions.Add(new ColumnDefinition()); inputRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        taskInput = new TextBox { PlaceholderText = "添加到今日任务…", MaxLength = 200, CornerRadius = new CornerRadius(10), FontSize = 13, Padding = new Thickness(12, 9, 12, 9) };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(taskInput, "添加到今日任务"); taskInput.KeyDown += (_, e) => { if (e.Key == VirtualKey.Enter) { AddTask(); e.Handled = true; } }; inputRow.Children.Add(taskInput);
        var add = IconButton("\uE710", "添加任务", AddTask); add.Background = quiet; Place(inputRow, add, 0, 1); Place(grid, inputRow, 2); return grid;
    }
    void AddTask()
    {
        var title = taskInput.Text.Trim(); if (title.Length == 0) return;
        data.Tasks.Add(new FocusTask { Title = title }); taskInput.Text = ""; Save(); RenderTasks(); taskInput.Focus(FocusState.Programmatic);
    }
    void RenderTasks()
    {
        if (taskList == null) return;
        var tasks = engine.TodayTasks(DateTimeOffset.Now).ToList();
        tasksCount.Text = $"{tasks.Count(t => t.CompletedAt != null)} / {tasks.Count}";
        var ids = tasks.Select(t => t.Id).ToHashSet();
        foreach (var id in taskRows.Keys.Where(id => !ids.Contains(id)).ToList())
        { taskList.Children.Remove(taskRows[id].Root); taskRows.Remove(id); }
        if (tasks.Count == 0)
        {
            if (emptyTasks == null)
            {
                var empty = Text("暂无任务", 14, true); empty.HorizontalAlignment = HorizontalAlignment.Center; empty.Margin = new Thickness(0, 75, 0, 0); emptyTasks = empty;
            }
            if (!taskList.Children.Contains(emptyTasks)) taskList.Children.Add(emptyTasks);
            return;
        }
        if (emptyTasks != null) taskList.Children.Remove(emptyTasks);
        foreach (var task in tasks)
        {
            if (!taskRows.TryGetValue(task.Id, out var view))
            {
                view = CreateTaskRow(task); taskRows.Add(task.Id, view); taskList.Children.Add(view.Root); AnimateEntrance(view.Root);
            }
            view.Root.Background = view.PointerInside ? quiet : Brush("00000000");
            view.Check.IsChecked = task.CompletedAt != null;
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(view.Check, "完成任务：" + task.Title);
            view.Label.Text = task.Title;
            view.Label.TextDecorations = task.CompletedAt != null ? Windows.UI.Text.TextDecorations.Strikethrough : Windows.UI.Text.TextDecorations.None;
            view.Label.Foreground = task.CompletedAt != null ? muted : ink;
            view.Label.Opacity = task.CompletedAt != null ? 0.65 : 1;
            view.Carry.Text = "从 " + task.CreatedAt.ToString("M/d") + " 延续";
            view.Carry.Visibility = task.CompletedAt == null && task.CreatedAt.LocalDateTime.Date < DateTime.Today ? Visibility.Visible : Visibility.Collapsed;
        }
    }
    TaskRow CreateTaskRow(FocusTask task)
    {
        var row = new Grid { Tag = task.Id, Padding = new Thickness(8, 7, 5, 7), CornerRadius = new CornerRadius(10), Background = Brush("00000000"), MinHeight = 47 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) }); row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
        var check = new CheckBox { MinWidth = 28, Padding = new Thickness(0), VerticalAlignment = VerticalAlignment.Center };
        check.Click += (_, _) =>
        {
            TimerEngine.SetCompleted(task, check.IsChecked == true, DateTimeOffset.Now);
            RenderTasks(); RenderTimer(); UpdateStatistics(); RefreshHeatmap(); Save();
        }; row.Children.Add(check);
        var labelPanel = new StackPanel { Spacing = 3 };
        var label = Text(task.Title, 16); label.MaxLines = 2; label.TextTrimming = TextTrimming.CharacterEllipsis;
        var carry = Text("", 10, true); labelPanel.Children.Add(label); labelPanel.Children.Add(carry);
        labelPanel.VerticalAlignment = VerticalAlignment.Center;
        Place(row, labelPanel, 0, 1);
        var edit = IconButton("\uE70F", "编辑任务", async () => await EditTask(task)); edit.Padding = new Thickness(5); edit.Content = Icon("\uE70F", 12); edit.Opacity = 0; Place(row, edit, 0, 2);
        var delete = IconButton("\uE711", "删除任务", () => DeleteTask(task)); delete.Padding = new Thickness(5); delete.Content = Icon("\uE711", 12); delete.Opacity = 0; Place(row, delete, 0, 3);
        var view = new TaskRow { Root = row, Check = check, Label = label, Carry = carry, Edit = edit, Delete = delete };
        row.PointerEntered += (_, _) => { view.PointerInside = true; UpdateTaskActions(view); };
        row.PointerExited += (_, e) => { if (!Inside(row, e.GetCurrentPoint(row).Position)) { view.PointerInside = false; UpdateTaskActions(view); } };
        row.GotFocus += (_, _) => UpdateTaskActions(view);
        row.LostFocus += (_, _) => DispatcherQueue.TryEnqueue(() => UpdateTaskActions(view));
        return view;
    }
    void UpdateTaskActions(TaskRow view)
    {
        var focused = false;
        if (view.Root.XamlRoot != null)
            for (var node = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(view.Root.XamlRoot) as DependencyObject; node != null; node = VisualTreeHelper.GetParent(node))
                if (node == view.Root) { focused = true; break; }
        var opacity = view.PointerInside || focused ? 1 : 0;
        view.Edit.Opacity = view.Delete.Opacity = opacity;
        view.Root.Background = view.PointerInside ? quiet : Brush("00000000");
    }
    async Task EditTask(FocusTask task)
    {
        var input = new TextBox { Text = task.Title, Header = "任务名称", MaxLength = 200, AcceptsReturn = false };
        if (await Dialog("编辑任务", input) == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(input.Text)) { task.Title = input.Text.Trim(); Save(); RenderTasks(); RenderTimer(); RefreshHeatmap(); }
    }
    void DeleteTask(FocusTask task)
    {
        task.Deleted = true;
        Save(); RenderTasks(); RenderTimer(); UpdateStatistics(); RefreshHeatmap();
    }
}
