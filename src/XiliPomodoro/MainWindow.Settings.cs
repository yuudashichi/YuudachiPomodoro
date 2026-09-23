using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using XiliPomodoro.Core;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace XiliPomodoro;
public sealed partial class MainWindow
{
    Grid BuildSettings()
    {
        var page = new Grid(); var stack = new StackPanel { Spacing = 20, MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Left };
        var heading = Text("设置", 25); heading.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold; stack.Children.Add(heading);
        var appearance = new StackPanel { Spacing = 18 }; appearance.Children.Add(Text("外观与运行", 17));
        var theme = new ComboBox { Header = "主题", MinWidth = 180 }; theme.Items.Add("跟随系统"); theme.Items.Add("浅色"); theme.Items.Add("深色"); theme.SelectedIndex = data.Settings.Theme switch { "Light" => 1, "Dark" => 2, _ => 0 };
        theme.SelectionChanged += (_, _) => { data.Settings.Theme = new[] { "System", "Light", "Dark" }[theme.SelectedIndex]; Save(); BuildUI(); ApplyMaterial(); }; appearance.Children.Add(theme);
        appearance.Children.Add(Toggle("窗口置顶", "保持在普通窗口上方。", data.Settings.AlwaysOnTop, value => { data.Settings.AlwaysOnTop = value; Save(); ApplyMaterial(); }));
        appearance.Children.Add(Toggle("关闭窗口时收进托盘", "计时继续运行；右键托盘图标可以完全退出。", data.Settings.CloseToTray, value => { data.Settings.CloseToTray = value; Save(); }));
        stack.Children.Add(Card(appearance));
        var storage = new StackPanel { Spacing = 16 }; storage.Children.Add(Text("数据与备份", 17));
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 }; actions.Children.Add(Button("导出备份", async () => await ExportBackup())); actions.Children.Add(Button("恢复备份", async () => await ImportBackup())); storage.Children.Add(actions);
        storage.Children.Add(Text(DataStore.DataDirectory, 11, true)); stack.Children.Add(Card(storage));
        var footer = new StackPanel { Spacing = 10 }; footer.Children.Add(Text($"惜立番茄钟  ·  {typeof(App).Assembly.GetName().Version?.ToString(3)}", 12, true)); footer.Children.Add(Button("保存并退出", async () => await ExitAsync())); stack.Children.Add(footer);
        var scroller = new ScrollViewer { Content = stack, HorizontalContentAlignment = HorizontalAlignment.Stretch, HorizontalScrollMode = ScrollMode.Disabled, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(0, 0, 14, 20) };
        // Bound the content explicitly: a capped, left-aligned StackPanel otherwise
        // sizes to its text instead of filling the available settings column.
        scroller.SizeChanged += (_, _) => stack.Width = Math.Max(0, Math.Min(760, scroller.ActualWidth - 14));
        page.Children.Add(scroller); return page;
    }
    UIElement Toggle(string title, string description, bool value, Action<bool> changed)
    {
        var grid = new Grid { ColumnSpacing = 20 }; grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new StackPanel { Spacing = 4 }; text.Children.Add(Text(title, 14)); text.Children.Add(Text(description, 12, true)); grid.Children.Add(text);
        var toggle = new ToggleSwitch { IsOn = value, OnContent = "", OffContent = "", MinWidth = 44, VerticalAlignment = VerticalAlignment.Center }; Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(toggle, title); toggle.Toggled += (_, _) => changed(toggle.IsOn); Place(grid, toggle, 0, 1); return grid;
    }
    async Task ExportBackup()
    {
        try
        {
            var picker = new FileSavePicker { SuggestedFileName = "惜立番茄钟备份-" + DateTime.Now.ToString("yyyyMMdd-HHmm") }; picker.FileTypeChoices.Add("惜立番茄钟备份", new List<string> { ".json" }); InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
            var file = await picker.PickSaveFileAsync(); if (file == null) return;
            engine.Checkpoint(); await store.SaveAsync(data); var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }); await File.WriteAllTextAsync(file.Path, json);
        }
        catch (Exception ex) { ReportError("无法导出备份", ex.Message); }
    }
    async Task ImportBackup()
    {
        try
        {
            var picker = new FileOpenPicker(); picker.FileTypeFilter.Add(".json"); InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this)); var file = await picker.PickSingleFileAsync(); if (file == null) return;
            var imported = await Task.Run(() => DataStore.ReadBackup(file.Path));
            if (await Dialog("恢复这份备份？", Text($"包含 {imported.Tasks.Count(t => !t.Deleted)} 个任务和 {imported.Sessions.Count} 次专注记录。\n\n将替换当前记录。恢复前会自动保存一份当前数据备份。"), "恢复备份") != ContentDialogResult.Primary) return;
            engine.Pause(); var safety = System.IO.Path.Combine(DataStore.DataDirectory, "恢复前备份-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json"); await File.WriteAllTextAsync(safety, JsonSerializer.Serialize(data));
            await store.SaveAsync(imported); engine.FocusCompleted -= OnFocusCompleted; data = imported; engine = new TimerEngine(data, new SystemClock()); engine.FocusCompleted += OnFocusCompleted;
            BuildUI(); ApplyMaterial(); Save();
        }
        catch (Exception ex) { ReportError("未能恢复备份", ex.Message); }
    }
}
