using System.Numerics;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using XiliPomodoro.Core;
using Windows.UI;
using Windows.UI.ViewManagement;
using WinRT.Interop;

namespace XiliPomodoro;

public sealed partial class MainWindow : Window
{
    readonly DataStore store;
    AppData data;
    TimerEngine engine;
    readonly NativeMethods native;
    readonly DispatcherTimer heartbeat = new();
    readonly UISettings uiSettings = new();
    Grid root = null!, focusPage = null!, settingsPage = null!, chartPage = null!;
    Grid pagesHost = null!;
    bool settingsBuilt, chartBuilt;
    readonly Dictionary<uint, SolidColorBrush> brushes = [];
    StackPanel taskList = null!;
    TextBox taskInput = null!, timeEditor = null!;
    TextBlock timeText = null!, stateText = null!, timerHint = null!, todayTime = null!, todayCount = null!, tasksCount = null!, subtitle = null!;
    Button startButton = null!, endButton = null!, focusNav = null!, settingsNav = null!, chartNav = null!;
    CompositionEllipseGeometry? ringGeometry;
    CompositionSpriteShape? ringShape;
    Microsoft.UI.Composition.ShapeVisual? ringVisual;
    bool ringAnimating;
    bool timeEditDirty, updatingTimeEditor, saveQueued;
    string lastTrayText = "";
    ControlTemplate? buttonTemplate;
    bool dark, hidden, quitting, settingsOpen, chartOpen, dialogOpen, windowWasMinimized;
    string day = DateTime.Now.ToString("yyyy-MM-dd");
    int displayedSecond = -1;
    long lastCheckpoint = System.Diagnostics.Stopwatch.GetTimestamp(), lastSummary = System.Diagnostics.Stopwatch.GetTimestamp();
    int year = DateTime.Now.Year;
    SolidColorBrush ink = null!, muted = null!, accent = null!, onAccent = null!, surface = null!, line = null!, quiet = null!;
    public MainWindow()
    {
        Trace("window constructor");
        Title = "惜立番茄钟";
        store = new DataStore(); data = store.Load(); engine = new TimerEngine(data, new SystemClock());
        Trace("data loaded");
        engine.FocusCompleted += OnFocusCompleted;
        var hwnd = WindowNative.GetWindowHandle(this);
        native = new NativeMethods(hwnd);
        Trace("native ready");
        native.ShowRequested += () => DispatcherQueue.TryEnqueue(ShowMain);
        native.ExitRequested += () => DispatcherQueue.TryEnqueue(async () => await ExitAsync());
        native.SuspendRequested += () => { engine.Pause(); Save(); RenderTimer(); AnimateRing(); };
        native.SessionEnding += () => { engine.Pause(); store.SaveAsync(data).GetAwaiter().GetResult(); };
        AppWindow.Closing += (sender, e) =>
        {
            if (quitting) return;
            e.Cancel = true;
            if (data.Settings.CloseToTray) { engine.Checkpoint(); Save(); hidden = true; StopRing(); native.Hide(); ScheduleHeartbeat(); }
            else _ = ExitAsync();
        };
        AppWindow.Changed += (_, e) =>
        {
            var minimized = native.IsMinimized;
            if (minimized == windowWasMinimized) return;
            windowWasMinimized = minimized;
            if (minimized) StopRing(); else if (!hidden) AnimateRing();
            ScheduleHeartbeat();
        };
        Activated += (_, e) => { if (e.WindowActivationState == WindowActivationState.Deactivated) { HideHeatTip(); HideChartTip(); } else if (!hidden) { CheckDate(); RenderTimer(); } };
        var scale = NativeMethods.GetDpiForWindow(hwnd) / 96.0;
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        var width = Math.Min((int)(840 * scale), area.Width - 48); var height = Math.Min((int)(700 * scale), area.Height - 48);
        AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(area.X + (area.Width - width) / 2, area.Y + (area.Height - height) / 2, width, height));
        Trace("window sized");
        BuildUI(); ApplyMaterial(); Save();
        Trace("ui ready");
        InitializeDiagnostics();
        heartbeat.Interval = TimeSpan.FromSeconds(1); heartbeat.Tick += Heartbeat; heartbeat.Start();
        uiSettings.ColorValuesChanged += (_, _) => DispatcherQueue.TryEnqueue(() => { if (data.Settings.Theme == "System") { BuildUI(); ApplyMaterial(); } });
    }
    partial void InitializeDiagnostics();
    partial void CaptureDiagnostic();
    static partial void Trace(string message);
    SolidColorBrush Brush(string hex)
    {
        hex = hex.TrimStart('#'); var n = Convert.ToUInt32(hex, 16);
        if (hex.Length != 8) n |= 0xFF000000;
        if (!brushes.TryGetValue(n, out var brush)) brushes[n] = brush = new SolidColorBrush(Color.FromArgb((byte)(n >> 24), (byte)(n >> 16), (byte)(n >> 8), (byte)n));
        return brush;
    }
    TextBlock Text(string text, double size = 14, bool secondary = false) => new() { Text = text, FontSize = size, Foreground = secondary ? muted : ink, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    FontIcon Icon(string glyph, double size = 18) => new() { Glyph = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = size };
    Button Button(string label, Action action, bool primary = false)
    {
        buttonTemplate ??= (ControlTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Button'><Border Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' CornerRadius='{TemplateBinding CornerRadius}' Padding='{TemplateBinding Padding}'><ContentPresenter Content='{TemplateBinding Content}' ContentTemplate='{TemplateBinding ContentTemplate}' Foreground='{TemplateBinding Foreground}' HorizontalAlignment='{TemplateBinding HorizontalContentAlignment}' VerticalAlignment='{TemplateBinding VerticalContentAlignment}'/></Border></ControlTemplate>");
        var b = new Button { Content = label, Template = buttonTemplate, UseSystemFocusVisuals = false, CornerRadius = new CornerRadius(10), Padding = new Thickness(16, 9, 16, 9), FontSize = 13, Background = primary ? accent : quiet, Foreground = primary ? onAccent : ink, BorderThickness = new Thickness(1), BorderBrush = Brush("00000000"), HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center };
        b.GotFocus += (_, _) => b.BorderBrush = b.FocusState == FocusState.Keyboard ? accent : Brush("00000000");
        b.LostFocus += (_, _) => b.BorderBrush = Brush("00000000");
        b.PointerPressed += (_, _) => b.Opacity = 0.82;
        b.PointerReleased += (_, _) => b.Opacity = 1;
        b.PointerCaptureLost += (_, _) => b.Opacity = 1;
        b.IsEnabledChanged += (_, _) => { if (b != startButton && b != pauseButton && b != endButton) b.Opacity = b.IsEnabled ? 1 : 0.35; };
        b.Click += (_, _) => action(); return b;
    }
    static bool Inside(FrameworkElement element, Windows.Foundation.Point p) => p.X >= 0 && p.Y >= 0 && p.X < element.ActualWidth && p.Y < element.ActualHeight;
    Button IconButton(string glyph, string label, Action action)
    {
        var b = Button("", action); b.Content = Icon(glyph); b.Padding = new Thickness(10); b.Background = Brush("00000000");
        ToolTipService.SetToolTip(b, label); Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(b, label); return b;
    }
    static void Place(Grid parent, UIElement child, int row = 0, int col = 0) { Grid.SetRow((FrameworkElement)child, row); Grid.SetColumn((FrameworkElement)child, col); parent.Children.Add(child); }
    Border Card(UIElement child, Thickness? padding = null) => new() { Child = child, Background = surface, CornerRadius = new CornerRadius(20), BorderBrush = line, BorderThickness = new Thickness(1), Padding = padding ?? new Thickness(24) };
    void BuildUI()
    {
        Trace("build UI");
        ResetChartResources();
        settingsBuilt = chartBuilt = false;
        if (timeEditDirty && timeEditor != null) CommitInlineDuration();
        timeEditDirty = false;
        HideHeatTip();
        StopRing();
        var sysDark = uiSettings.GetColorValue(UIColorType.Background).R < 128;
        dark = data.Settings.Theme == "Dark" || data.Settings.Theme == "System" && sysDark;
        ink = Brush(dark ? "EEF0E4" : "394235"); muted = Brush(dark ? "C1C8B2" : "68705E");
        accent = Brush(dark ? "C4CDA6" : "ADB995"); surface = Brush(dark ? "A83B4435" : "A3FDFEF6");
        line = Brush(dark ? "28D5DDC5" : "339DA984"); quiet = Brush(dark ? "FF4A5540" : "FFF0F2E5");
        onAccent = Brush("303D2A");
        root = new Grid { RequestedTheme = dark ? ElementTheme.Dark : ElementTheme.Light, Background = Brush(dark ? "38313B2C" : "28F3F5E9") };
        foreach (var key in new[] { "AccentFillColorDefaultBrush", "AccentFillColorSecondaryBrush", "AccentFillColorTertiaryBrush", "ToggleSwitchFillOn", "ToggleSwitchFillOnPointerOver", "ToggleSwitchFillOnPressed", "ToggleSwitchStrokeOn", "CheckBoxCheckBackgroundFillChecked", "CheckBoxCheckBackgroundFillCheckedPointerOver", "CheckBoxCheckBackgroundStrokeChecked", "CheckBoxCheckBackgroundFillCheckedPressed" }) root.Resources[key] = accent;
        root.Resources["TextControlBorderBrushFocused"] = accent;
        root.Resources["ComboBoxBorderBrushFocused"] = accent;
        root.Resources["TextOnAccentFillColorPrimaryBrush"] = onAccent;
        foreach (var key in new[] { "CheckBoxCheckGlyphForegroundChecked", "CheckBoxCheckGlyphForegroundCheckedPointerOver", "CheckBoxCheckGlyphForegroundCheckedPressed" }) root.Resources[key] = onAccent;
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(36) }); root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var titlebar = new Grid { Background = Brush("00000000") };
        root.Children.Add(titlebar);
        ExtendsContentIntoTitleBar = true; SetTitleBar(titlebar);
        AppWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent; AppWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        AppWindow.TitleBar.ButtonForegroundColor = dark ? Colors.White : Color.FromArgb(255, 41, 61, 50);
        var shell = new Grid(); shell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) }); shell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); Place(root, shell, 1);
        var rail = new Grid { BorderBrush = line, BorderThickness = new Thickness(0, 0, 1, 0), Padding = new Thickness(8, 16, 8, 16) };
        var top = new StackPanel { Spacing = 12 };
        focusNav = NavigationButton(BuildAlarmIcon(), "番茄钟", () => SwitchPage(false)); top.Children.Add(focusNav);
        chartNav = NavigationButton(BuildChartIcon(), "每日专注", () => SwitchPage(false, chart: true)); top.Children.Add(chartNav); rail.Children.Add(top);
        var bottom = new StackPanel { Spacing = 12, VerticalAlignment = VerticalAlignment.Bottom };
        var pin = BuildPinButton();
        settingsNav = IconButton("\uE713", "设置", () => SwitchPage(true)); bottom.Children.Add(pin); bottom.Children.Add(settingsNav); rail.Children.Add(bottom); shell.Children.Add(rail);
        var pages = pagesHost = new Grid { Margin = new Thickness(20, 6, 20, 16) }; Place(shell, pages, 0, 1);
        focusPage = new Grid(); focusPage.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); focusPage.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var header = focusHeader = new Grid { Margin = new Thickness(0, 0, 0, 16), RowSpacing = 10 }; header.RowDefinitions.Add(new RowDefinition()); header.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); header.ColumnDefinitions.Add(new ColumnDefinition()); header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var heading = new StackPanel { Spacing = 6 }; var titleText = Text("番茄钟", 25); titleText.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold; heading.Children.Add(titleText);
        subtitle = Text(DateTime.Now.ToString("M月d日 · dddd", System.Globalization.CultureInfo.GetCultureInfo("zh-CN")), 12, true); heading.Children.Add(subtitle); header.Children.Add(heading);
        var stats = focusStats = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 24, VerticalAlignment = VerticalAlignment.Center };
        var a = new StackPanel { Spacing = 3 }; todayTime = Text("0 分钟", 20); a.Children.Add(todayTime); a.Children.Add(Text("今日专注", 11, true));
        var b = new StackPanel { Spacing = 3 }; todayCount = Text("0 项", 20); b.Children.Add(todayCount); b.Children.Add(Text("今日完成", 11, true)); stats.Children.Add(a); stats.Children.Add(b); Place(header, stats, 0, 1); focusPage.Children.Add(header);
        var scroll = focusScroller = new ScrollViewer { HorizontalContentAlignment = HorizontalAlignment.Stretch, HorizontalScrollMode = ScrollMode.Disabled, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        scroll.ViewChanging += (_, _) => HideHeatTip();
        var content = new StackPanel { Spacing = 18, Padding = new Thickness(0, 0, 4, 0) }; scroll.Content = content; Place(focusPage, scroll, 1);
        var mainCard = focusMainCard = new Grid(); mainCard.RowDefinitions.Add(new RowDefinition()); mainCard.RowDefinitions.Add(new RowDefinition { Height = new GridLength(0) }); mainCard.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.12, GridUnitType.Star) }); mainCard.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        timerPanel = (Grid)BuildTimer(); tasksPanel = (Grid)BuildTasks();
        Place(mainCard, timerPanel); Place(mainCard, tasksPanel, 0, 1); content.Children.Add(Card(mainCard, new Thickness(0)));
        Trace("timer and tasks built");
        heatCard = BuildHeatmap(); content.Children.Add(heatCard);
        narrowFocus = compactHeader = null;
        focusPage.SizeChanged += (_, _) => UpdateResponsiveLayout();
        focusScroller.SizeChanged += (_, _) => UpdateResponsiveLayout();
        heatCard.SizeChanged += (_, _) => UpdateResponsiveLayout();
        Trace("heatmap built");
        settingsPage = new Grid(); chartPage = new Grid(); pages.Children.Add(focusPage); pages.Children.Add(settingsPage); pages.Children.Add(chartPage);
        Trace("settings built");
        BuildHeatTip();
        AttachTimerPointerHandling();
        Content = root; root.Loaded += (_, _) => { RenderTimer(); AnimateRing(); CaptureDiagnostic(); };
        SwitchPage(settingsOpen, false, chartOpen); RenderTasks(); RenderTimer(); UpdateStatistics();
    }
    void ApplyMaterial()
    {
        try { SystemBackdrop = Microsoft.UI.Composition.SystemBackdrops.DesktopAcrylicController.IsSupported() ? new DesktopAcrylicBackdrop() : null; } catch { SystemBackdrop = null; }
        if (SystemBackdrop == null) root.Background = Brush(dark ? "FF313B2C" : "FFF5F6ED");
        if (AppWindow.Presenter is OverlappedPresenter p) p.IsAlwaysOnTop = data.Settings.AlwaysOnTop;
        RefreshPin();
    }
    void SwitchPage(bool settings, bool animate = true, bool chart = false)
    {
        HideHeatTip(); HideChartTip();
        timerPointerInside = false; UpdateTimerHover();
        settingsOpen = settings; chartOpen = !settings && chart;
        if (settingsOpen && !settingsBuilt)
        {
            pagesHost.Children.Remove(settingsPage); settingsPage = BuildSettings(); pagesHost.Children.Add(settingsPage); settingsBuilt = true;
        }
        if (chartOpen && !chartBuilt)
        {
            pagesHost.Children.Remove(chartPage); chartPage = BuildChart(); pagesHost.Children.Add(chartPage); chartBuilt = true;
        }
        focusPage.Visibility = !settingsOpen && !chartOpen ? Visibility.Visible : Visibility.Collapsed;
        settingsPage.Visibility = settingsOpen ? Visibility.Visible : Visibility.Collapsed;
        chartPage.Visibility = chartOpen ? Visibility.Visible : Visibility.Collapsed;
        focusNav.Background = !settingsOpen && !chartOpen ? quiet : Brush("00000000");
        settingsNav.Background = settingsOpen ? quiet : Brush("00000000");
        chartNav.Background = chartOpen ? quiet : Brush("00000000");
        if (chartOpen) { engine.Checkpoint(); RefreshChart(); }
        if (animate) AnimateEntrance(settingsOpen ? settingsPage : chartOpen ? chartPage : focusPage);
        if (settingsOpen || chartOpen) StopRing(); else AnimateRing();
    }
    bool Motion => uiSettings.AnimationsEnabled;
    void AnimateEntrance(UIElement element)
    {
        if (!Motion) return;
        Trace("entrance animation");
        var visual = ElementCompositionPreview.GetElementVisual(element); var compositor = visual.Compositor;
        var opacity = compositor.CreateScalarKeyFrameAnimation(); opacity.InsertKeyFrame(0, 0.92f); opacity.InsertKeyFrame(1, 1); opacity.Duration = TimeSpan.FromMilliseconds(100); visual.StartAnimation("Opacity", opacity);
        ElementCompositionPreview.SetIsTranslationEnabled(element, true);
        var slide = compositor.CreateVector3KeyFrameAnimation(); slide.InsertKeyFrame(0, new Vector3(0, 4, 0)); slide.InsertKeyFrame(1, Vector3.Zero); slide.Duration = TimeSpan.FromMilliseconds(120); visual.StartAnimation("Translation", slide);
    }
    void Heartbeat(object? sender, object e)
    {
        engine.Tick();
        if (engine.State.Status == TimerStatus.Running && System.Diagnostics.Stopwatch.GetElapsedTime(lastCheckpoint).TotalSeconds >= 30) { engine.Checkpoint(); Save(); lastCheckpoint = System.Diagnostics.Stopwatch.GetTimestamp(); }
        CheckDate();
        if (System.Diagnostics.Stopwatch.GetElapsedTime(lastSummary).TotalSeconds >= 15) { if (!hidden && !native.IsMinimized) UpdateStatistics(); lastSummary = System.Diagnostics.Stopwatch.GetTimestamp(); }
        if (!hidden && !native.IsMinimized) RenderClock();
        ScheduleHeartbeat();
    }
    void ScheduleHeartbeat()
    {
        var seconds = engine.State.Status != TimerStatus.Running ? 30 : hidden || native.IsMinimized ? Math.Clamp(engine.Remaining, 0.05, 30) : 1;
        heartbeat.Interval = TimeSpan.FromSeconds(seconds);
    }
    void CheckDate()
    {
        var current = DateTime.Now.ToString("yyyy-MM-dd"); if (current == day) return;
        day = current; subtitle.Text = DateTime.Now.ToString("M月d日 · dddd", System.Globalization.CultureInfo.GetCultureInfo("zh-CN"));
        RenderTasks(); RefreshHeatmap(); UpdateStatistics();
    }
    void ToggleTimer()
    {
        var starting = engine.State.Status == TimerStatus.Ready;
        if (starting && !CommitInlineDuration()) return;
        if (engine.State.Status == TimerStatus.Running) engine.Pause(); else engine.Start();
        ScheduleHeartbeat();
        RenderTimer(); AnimateRing(starting); Save();
    }
    void RenderClock()
    {
        var seconds = (int)Math.Ceiling(engine.Remaining);
        if (seconds == displayedSecond) return; displayedSecond = seconds;
        timeText.Text = $"{seconds / 60:00}:{seconds % 60:00}";
        if (engine.State.Status == TimerStatus.Ready && !timeEditDirty) SyncTimeEditor();
        if (!ringAnimating) SetRingProgress();
    }
    void RenderTimer()
    {
        if (timeText == null) return;
        displayedSecond = -1; RenderClock();
        var status = engine.State.Status;
        stateText.Text = status == TimerStatus.Paused ? "已暂停" : "";
        if (ringShape?.StrokeBrush is CompositionColorBrush ringBrush) ringBrush.Color = accent.Color;
        if (!timeEditDirty) timerHint.Text = "";
        RenderTimerInteraction();
        ToolTipService.SetToolTip(endButton, "结束专注");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(endButton, "结束专注");
        var editable = status == TimerStatus.Ready;
        timeEditor.Visibility = editable ? Visibility.Visible : Visibility.Collapsed;
        timeText.Visibility = editable ? Visibility.Collapsed : Visibility.Visible;
        timeEditor.IsReadOnly = !editable;
        var trayText = "惜立番茄钟 · 专注" + (status == TimerStatus.Ready ? " · 等待开始" : status == TimerStatus.Paused ? " · 已暂停" : " · 进行中");
        if (lastTrayText != trayText) { lastTrayText = trayText; native.SetTrayText(trayText); }
    }
    void OnFocusCompleted()
    {
        Save(); RenderTimer(); UpdateStatistics(); RefreshHeatmap();
        RestoreWindow(showFocus: true);
        completionPending = true;
        _ = ShowCompletionReminderAsync();
    }
    void ShowMain() => RestoreWindow(showFocus: false);
    void RestoreWindow(bool showFocus)
    {
        // Prepare the retained page before showing it, without a navigation entrance.
        if (showFocus) SwitchPage(false, animate: false);
        foreach (var page in new[] { focusPage, settingsPage, chartPage })
        {
            ElementCompositionPreview.SetIsTranslationEnabled(page, true);
            var visual = ElementCompositionPreview.GetElementVisual(page);
            visual.StopAnimation("Translation"); visual.StopAnimation("Opacity");
            visual.Properties.InsertVector3("Translation", Vector3.Zero); visual.Opacity = 1;
        }
        CheckDate(); RenderTimer(); UpdateStatistics(); RefreshHeatmap();
        hidden = false; native.BringForward(); AnimateRing(); ScheduleHeartbeat();
    }
    void UpdateStatistics()
    {
        todayTime.Text = FormatDuration(engine.SecondsOn(day)); todayCount.Text = data.Tasks.Count(t => !t.Deleted && t.CompletedDay == day) + " 项";
        if (chartOpen) RefreshChart();
    }
    static string FormatDuration(double seconds) => seconds < 60 ? (seconds > 0 ? "不足 1 分钟" : "0 分钟") : seconds < 3600 ? $"{(int)(seconds / 60)} 分钟" : $"{(int)(seconds / 3600)} 小时 {(int)(seconds % 3600 / 60)} 分";
    async void ReportError(string title, string message)
    {
        Trace(title + ": " + message);
        await Dialog(title, Text(message), "", "关闭");
    }
    void Save()
    {
        if (saveQueued || quitting) return;
        saveQueued = true;
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, async () =>
        {
            saveQueued = false; if (quitting) return;
            try { await store.SaveAsync(data); }
            catch (Exception ex) { ReportError("暂时无法保存", ex.Message); }
        });
    }
    async Task ExitAsync()
    {
        if (quitting) return;
        if (timeEditDirty) CommitInlineDuration();
        engine.EndEarly();
        try { await store.SaveAsync(data); }
        catch (Exception ex) { ShowMain(); ReportError("无法保存，暂未退出", ex.Message); return; }
        quitting = true; heartbeat.Stop(); StopRing(); ReleaseTimerBlur(); ResetChartResources(); native.Dispose(); store.Dispose(); Close(); Application.Current.Exit();
    }
    async Task<ContentDialogResult> Dialog(string title, UIElement content, string primary = "保存", string close = "取消")
    {
        if (dialogOpen) return ContentDialogResult.None;
        dialogOpen = true;
        try { return await new ContentDialog { XamlRoot = root.XamlRoot, RequestedTheme = root.RequestedTheme, Title = title, Content = content, PrimaryButtonText = primary, CloseButtonText = close, DefaultButton = ContentDialogButton.Primary }.ShowAsync(); }
        finally { dialogOpen = false; if (completionPending) _ = ShowCompletionReminderAsync(); }
    }
    void SyncTimeEditor()
    {
        if (timeEditor == null) return;
        var seconds = (int)engine.State.DurationSeconds;
        var value = $"{seconds / 60:00}:{seconds % 60:00}";
        if (timeEditor.Text == value) return;
        updatingTimeEditor = true; timeEditor.Text = value; updatingTimeEditor = false;
    }
    bool CommitInlineDuration()
    {
        if (engine.State.Status != TimerStatus.Ready || !timeEditDirty) return true;
        if (!DurationInput.TryParse(timeEditor.Text, out var seconds)) { timerHint.Text = "输入 1–180 分钟或分:秒"; return false; }
        engine.SetDurationSeconds(seconds); timeEditDirty = false; timerHint.Text = ""; SyncTimeEditor(); RenderClock(); AnimateRing(); Save(); return true;
    }
    void EndTimer()
    {
        if (engine.State.Status == TimerStatus.Ready) return;
        engine.EndEarly(); RenderTimer(); AnimateRing(); ScheduleHeartbeat();
        UpdateStatistics(); RefreshHeatmap(); Save();
    }
}
