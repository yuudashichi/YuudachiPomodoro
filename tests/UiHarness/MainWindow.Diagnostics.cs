using System.Diagnostics;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using XiliPomodoro.Core;
using Path = System.IO.Path;

namespace XiliPomodoro;
public sealed partial class MainWindow
{
    partial void InitializeDiagnostics()
    {
        if (Environment.GetEnvironmentVariable("XILI_RUN_SMOKE") == "1") root.Loaded += async (_, _) => await RunSmoke();
    }
    static partial void Trace(string message) { if (Environment.GetEnvironmentVariable("XILI_DATA_DIR") != null) { Directory.CreateDirectory(DataStore.DataDirectory); File.AppendAllText(Path.Combine(DataStore.DataDirectory, "diagnostic.log"), DateTime.Now.ToString("O") + " " + message + Environment.NewLine); } }
    partial void CaptureDiagnostic() { _ = CaptureDiagnosticAsync(); }
    async Task CaptureDiagnosticAsync(Action? prepare = null, int delayMilliseconds = 500)
    {
        if (Environment.GetEnvironmentVariable("XILI_DATA_DIR") == null) return;
        try
        {
            await Task.Delay(delayMilliseconds);
            prepare?.Invoke();
            root.UpdateLayout();
            var bitmap = new Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap();
            await bitmap.RenderAsync(root);
            var pixels = await bitmap.GetPixelsAsync();
            var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(CreateCapturePath());
            using var stream = await file.OpenAsync(Windows.Storage.FileAccessMode.ReadWrite);
            var encoder = await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId, stream);
            using var reader = Windows.Storage.Streams.DataReader.FromBuffer(pixels); var bytes = new byte[pixels.Length]; reader.ReadBytes(bytes);
            encoder.SetPixelData(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, bytes);
            await encoder.FlushAsync();
        }
        catch (Exception ex) { Trace("capture: " + ex.Message); }
    }
    string CreateCapturePath() { var path = Path.Combine(DataStore.DataDirectory, "ui-" + (settingsOpen ? "settings" : chartOpen ? "chart" : "focus") + (dark ? "-dark" : "-light") + ".png"); File.WriteAllBytes(path, []); return path; }
    bool smokeStarted;
    // Opt-in test harness: only available with an explicit, isolated data directory.
    async Task RunSmoke()
    {
        if (smokeStarted || Environment.GetEnvironmentVariable("XILI_DATA_DIR") == null) return;
        smokeStarted = true;
        var report = new List<string>();
        void Check(bool value, string message)
        {
            if (!value) throw new InvalidOperationException(message);
            report.Add("PASS " + message);
            File.WriteAllLines(System.IO.Path.Combine(DataStore.DataDirectory, "smoke-results.txt"), report);
        }
        try
        {
            await Task.Delay(700);
            var focusIcon = (FrameworkElement)focusNav.Content; var chartIcon = (FrameworkElement)chartNav.Content;
            var focusCenter = focusIcon.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point(focusIcon.ActualWidth / 2, focusIcon.ActualHeight / 2));
            var chartCenter = chartIcon.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point(chartIcon.ActualWidth / 2, chartIcon.ActualHeight / 2));
            Check(Math.Abs(focusCenter.X - chartCenter.X) < 0.1 && focusIcon.ActualWidth == chartIcon.ActualWidth && focusNav.ActualWidth == chartNav.ActualWidth, "Navigation vector icons share the same size and horizontal center");
            Check(Title == "惜立番茄钟" && ElementSoundPlayer.State == ElementSoundPlayerState.Off, "Renamed window disables all built-in control sounds");
            Check(data.Settings.Theme == "Light" && !JsonSerializer.Serialize(data.Settings).Contains("Notifications"), "Fresh profile starts light without notification preferences");
            taskInput.Text = "阅读二十页，整理三个想法"; AddTask();
            taskInput.Text = "完成一个小目标"; AddTask();
            var first = data.Tasks[^2]; var second = data.Tasks[^1];
            Check(engine.TodayTasks(DateTimeOffset.Now).Count() >= 2, "Task add renders and persists");
            var originalCell = heatButtons[day]; var originalRow = taskRows[second.Id].Root;
            TimerEngine.SetCompleted(second, true, DateTimeOffset.Now); RenderTasks(); UpdateStatistics(); RefreshHeatmap();
            Check(tasksCount.Text.StartsWith("1 /"), "Completed task checkbox/count state");
            Check(ReferenceEquals(originalCell, heatButtons[day]) && ReferenceEquals(originalRow, taskRows[second.Id].Root), "Task completion retains task and heatmap controls");
            ShowHeatTip(originalCell);
            Check(heatTip!.Visibility == Visibility.Visible && heatTipText.Text.Contains(second.Title) && !heatTipText.Text.Contains(first.Title), "Heatmap details appear synchronously and list completed tasks only");
            ShowHeatTip(originalCell, new Windows.Foundation.Point(160, 180));
            Check(Canvas.GetLeft(heatTip!) == 176 && Canvas.GetTop(heatTip!) == 200, "Heatmap tooltip appears below and right of pointer");
            PositionHeatTip(new Windows.Foundation.Point(180, 195));
            Check(Canvas.GetLeft(heatTip!) == 196 && Canvas.GetTop(heatTip!) == 215, "Tooltip tracks pointer movement within the same cell");
            PositionHeatTip(new Windows.Foundation.Point(root.ActualWidth - 4, root.ActualHeight - 4));
            Check(Canvas.GetLeft(heatTip!) + heatTip!.DesiredSize.Width <= root.ActualWidth - 8 && Canvas.GetTop(heatTip!) + heatTip.DesiredSize.Height <= root.ActualHeight - 8, "Tooltip stays inside window at right and bottom edges");
            ShowHeatTip(originalCell);
            var compact = heatTip!.DesiredSize;
            var originalTitle = second.Title; second.Title = new string('长', 70); ShowHeatTip(originalCell);
            Check(heatTip.DesiredSize.Width > compact.Width && heatTip.DesiredSize.Width <= 292 && heatTip.Background is Microsoft.UI.Xaml.Media.AcrylicBrush && double.IsNaN(heatTip.Width), "Acrylic heatmap card fits short and long content naturally");
            second.Title = originalTitle; ShowHeatTip(originalCell);
            var bounds = originalCell.TransformToVisual(root).TransformBounds(new Windows.Foundation.Rect(0, 0, originalCell.ActualWidth, originalCell.ActualHeight));
            DismissHeatTipOutside(new Windows.Foundation.Point(bounds.Left + bounds.Width / 2, bounds.Top + 0.01));
            Check(heatTip.Visibility == Visibility.Visible, "Pointer inside top edge retains tooltip");
            DismissHeatTipOutside(new Windows.Foundation.Point(bounds.Left + bounds.Width / 2, bounds.Top - 0.01));
            Check(heatTip.Visibility == Visibility.Collapsed && hoveredHeatCell == null, "Slow upward exit closes tooltip at subpixel boundary");
            ShowHeatTip(originalCell); DismissHeatTipOutside(new Windows.Foundation.Point(bounds.Right + 0.1, bounds.Top + bounds.Height / 2));
            Check(heatTip.Visibility == Visibility.Collapsed, "Pointer in calendar gaps clears tooltip");
            var futureCell = heatButtons.Values.FirstOrDefault(c => (DateTime)c.Tag > DateTime.Today);
            if (futureCell != null) { ShowHeatTip(originalCell); ShowHeatTip(futureCell); Check(heatTip.Visibility == Visibility.Collapsed && !futureCell.IsEnabled, "Future dates never display statistics"); }
            ShowHeatTip(originalCell);
            var before = originalCell.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point(0, 0));
            var border = originalCell.BorderThickness;
            for (int n = 0; n < 20; n++) { HideHeatTip(); ShowHeatTip(originalCell); }
            root.UpdateLayout();
            var after = originalCell.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point(0, 0));
            Check(before == after && border == originalCell.BorderThickness && !heatTip.IsHitTestVisible, "Repeated heatmap hover leaves bounds and border thickness unchanged");
            await CaptureDiagnosticAsync(() => ShowHeatTip(originalCell));
            File.Copy(System.IO.Path.Combine(DataStore.DataDirectory, "ui-focus-light.png"), System.IO.Path.Combine(DataStore.DataDirectory, "hover.png"), true);
            HideHeatTip();
            var size = AppWindow.Size;
            Check(narrowFocus == false && focusPage.ActualWidth >= FocusStackBreakpoint && focusPage.ActualWidth < FocusStackBreakpoint + 100, "Compact startup remains just above the horizontal layout breakpoint");
            Check(((Grid)root.Children[0]).Children.Count == 0, "Title bar has no visible app name");
            var layoutScale = root.XamlRoot.RasterizationScale;
            var retainedHeatWidth = heatCells.Width; var retainedHeatHeight = heatCells.Height;
            focusNav.Focus(FocusState.Programmatic);
            foreach (var dipWidth in new[] { 520, 740, 790, 840, 1100 })
            {
                AppWindow.Resize(new Windows.Graphics.SizeInt32((int)(dipWidth * layoutScale), (int)(700 * layoutScale))); await Task.Delay(250);
                focusScroller.ChangeView(null, 0, null, true); await Task.Delay(100);
                var stacked = focusPage.ActualWidth < FocusStackBreakpoint;
                Check(narrowFocus == stacked && Grid.GetRow(tasksPanel) == (stacked ? 1 : 0) && Grid.GetColumn(tasksPanel) == (stacked ? 0 : 1), $"Responsive task placement follows the breakpoint at {dipWidth} DIP");
                var taskBounds = tasksPanel.TransformToVisual(focusMainCard).TransformBounds(new Windows.Foundation.Rect(0, 0, tasksPanel.ActualWidth, tasksPanel.ActualHeight));
                var inputBounds = taskInput.TransformToVisual(tasksPanel).TransformBounds(new Windows.Foundation.Rect(0, 0, taskInput.ActualWidth, taskInput.ActualHeight));
                Check(taskBounds.Right <= focusMainCard.ActualWidth + 1 && inputBounds.Right <= tasksPanel.ActualWidth && inputBounds.Bottom <= tasksPanel.ActualHeight && taskInput.ActualWidth > 130, $"Task card and input remain inside the layout at {dipWidth} DIP");
                Check(heatButtons.Values.All(c => double.IsNaN(c.Width) && double.IsNaN(c.Height) && c.ActualWidth > 0 && Math.Abs(c.ActualWidth - c.ActualHeight) < 0.01), $"Heatmap uses automatic square sizing at {dipWidth} DIP");
                var cellBounds = originalCell.TransformToVisual(root).TransformBounds(new Windows.Foundation.Rect(0, 0, originalCell.ActualWidth, originalCell.ActualHeight));
                Check(heatCells.Width == retainedHeatWidth && heatCells.Height == retainedHeatHeight && Math.Abs(cellBounds.Width - heatSide) < 0.1 && Math.Abs(cellBounds.Width - cellBounds.Height) < 0.01, $"Heatmap scales on screen without changing internal layout at {dipWidth} DIP");
                if (dipWidth is 520 or 740 or 840 or 1100)
                {
                    await CaptureDiagnosticAsync();
                    File.Copy(System.IO.Path.Combine(DataStore.DataDirectory, "ui-focus-light.png"), System.IO.Path.Combine(DataStore.DataDirectory, $"layout-{dipWidth}.png"), true);
                }
                if (stacked)
                {
                    focusScroller.ChangeView(null, focusScroller.ScrollableHeight, null, true); await Task.Delay(100);
                    Check(focusScroller.HorizontalOffset == 0 && focusScroller.ScrollableWidth < 1, $"Narrow layout scrolls vertically without horizontal overflow at {dipWidth} DIP");
                    if (dipWidth == 520) { await CaptureDiagnosticAsync(); File.Copy(System.IO.Path.Combine(DataStore.DataDirectory, "ui-focus-light.png"), System.IO.Path.Combine(DataStore.DataDirectory, "layout-520-bottom.png"), true); }
                    focusScroller.ChangeView(null, 0, null, true);
                }
            }
            AppWindow.Resize(size); await Task.Delay(200);
            foreach (var width in new[] { 980, 1280, 1600 })
            {
                AppWindow.Resize(new Windows.Graphics.SizeInt32(width, size.Height)); await Task.Delay(200);
                Check(heatButtons.Values.All(c => c.ActualWidth > 0 && Math.Abs(c.ActualWidth - c.ActualHeight) < 0.01), $"Heatmap stays square at window width {width}");
            }
            AppWindow.Resize(size); await Task.Delay(200);
            var taskView = taskRows[first.Id]; taskView.PointerInside = true; UpdateTaskActions(taskView);
            taskView.Edit.Focus(FocusState.Programmatic); taskView.Delete.Focus(FocusState.Programmatic); UpdateTaskActions(taskView);
            Check(taskView.Edit.Opacity == 1 && taskView.Delete.Opacity == 1 && ReferenceEquals(originalRow, taskRows[second.Id].Root), "Task actions remain visible across child focus changes");
            Check(taskView.Label.FontSize == 16 && taskView.Root.Children.OfType<Button>().All(b => !b.UseSystemFocusVisuals && ((Microsoft.UI.Xaml.Media.SolidColorBrush)b.BorderBrush).Color.A == 0), "Task text enlarged and pointer/programmatic focus has no outline");
            taskView.PointerInside = false; startButton.Focus(FocusState.Programmatic); UpdateTaskActions(taskView);
            var initialPin = data.Settings.AlwaysOnTop; TogglePin();
            Check(data.Settings.AlwaysOnTop != initialPin && ((Microsoft.UI.Xaml.Media.SolidColorBrush)pinBody.Fill).Color.A == (data.Settings.AlwaysOnTop ? 255 : 0) && ((Microsoft.UI.Windowing.OverlappedPresenter)AppWindow.Presenter).IsAlwaysOnTop == data.Settings.AlwaysOnTop, "Pin fill and native topmost state toggle together");
            TogglePin();
            Check(data.Settings.AlwaysOnTop == initialPin && !root.Children.OfType<InfoBar>().Any(), "Pin toggles back without an operation banner");
            Check(stateText.Text == "", "Timer header is empty before starting");
            Check(ringGeometry!.TrimStart == 0 && ringGeometry.TrimEnd == 0 && ringVisual!.Opacity == 0 && ringShape!.StrokeThickness == 8, "Ready ring is empty with an eight-DIP track");
            Check(!JsonSerializer.Serialize(data.Settings).Contains("Animations") && !JsonSerializer.Serialize(data.Settings).Contains("Transparency"), "Old disabled appearance preferences no longer control the UI");
            timeEditor.Focus(FocusState.Pointer); timeEditor.Text = "25:30";
            DismissTimeEditorFromPointer(timeEditor);
            Check(timeEditor.FocusState != FocusState.Unfocused, "Clicking inside the duration editor preserves focus");
            DismissTimeEditorFromPointer(root);
            Check(timeEditor.FocusState == FocusState.Unfocused && engine.State.DurationSeconds == 1530 && !timeEditDirty, "Clicking a blank surface dismisses the editor and applies its duration");
            timeEditor.Focus(FocusState.Pointer); timeEditor.Text = "25:00";
            DismissTimeEditorFromPointer(todayTime);
            Check(timeEditor.FocusState == FocusState.Unfocused && engine.State.DurationSeconds == 1500, "Clicking a non-focusable label also dismisses the editor");
            timeEditor.Focus(FocusState.Pointer); DismissTimeEditorFromPointer(taskInput);
            Check(ReferenceEquals(Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(root.XamlRoot), taskInput), "Clicking another input preserves focus on that input");
            var startWidth = startButton.ActualWidth;
            SetTimerButtonHovered(startButton, true);
            await CaptureDiagnosticAsync();
            File.Copy(System.IO.Path.Combine(DataStore.DataDirectory, "ui-focus-light.png"), System.IO.Path.Combine(DataStore.DataDirectory, "start-hover.png"), true);
            var hoverScale = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(startButton).Scale.X;
            Check(!Motion || Math.Abs(hoverScale - 1.14) < 0.02, "Play hover reaches fourteen percent enlargement");
            Check(hoveredTimerButtons.Contains(startButton) && startButton.ActualWidth == startWidth, "Start hover enlarges visually without changing layout width");
            SetTimerButtonHovered(startButton, false);
            timeEditor.Text = "0"; ToggleTimer();
            Check(engine.State.Status == TimerStatus.Ready && !dialogOpen, "Invalid inline duration blocks start without opening a dialog");
            timeEditor.Text = "180:00"; CommitInlineDuration(); timeEditor.Select(2, 0); timeEditor.Focus(FocusState.Pointer);
            Check(timeEditor.SelectionLength == 0 && timeEditor.SelectionStart == 2, "Focusing time preserves caret instead of selecting all");
            await CaptureDiagnosticAsync();
            File.Copy(System.IO.Path.Combine(DataStore.DataDirectory, "ui-focus-light.png"), System.IO.Path.Combine(DataStore.DataDirectory, "inline-edit.png"), true);
            startButton.Focus(FocusState.Programmatic); timerPointerInside = false; UpdateTimerHover();
            timeEditor.Text = "01:30"; SetTimerButtonHovered(startButton, true); ToggleTimer();
            Check(!Motion || ringFilling && ringGeometry.TrimStart == 1 && ringGeometry.TrimEnd == 1, "Starting begins a counterclockwise zero-to-full ring fill");
            await CaptureDiagnosticAsync(delayMilliseconds: 120);
            File.Copy(System.IO.Path.Combine(DataStore.DataDirectory, "ui-focus-light.png"), System.IO.Path.Combine(DataStore.DataDirectory, "ring-start-fill.png"), true);
            Check(!hoveredTimerButtons.Contains(startButton), "Starting clears hover on the disappearing play button");
            Check(engine.State.Status == TimerStatus.Running && engine.State.DurationSeconds == 90 && timeEditor.IsReadOnly && timeEditor.Visibility == Visibility.Collapsed, "Inline duration applies immediately and locks during countdown");
            // Start can transfer keyboard focus to Pause. Test the non-hover state explicitly.
            taskInput.Focus(FocusState.Programmatic); timerPointerInside = false; UpdateTimerHover();
            Check(timerActive == true && !startButton.IsHitTestVisible && !startButton.IsEnabled && !timerActions.IsHitTestVisible, "Start switches to centered time and hides ready controls");
            await CaptureDiagnosticAsync(); File.Copy(System.IO.Path.Combine(DataStore.DataDirectory, "ui-focus-light.png"), System.IO.Path.Combine(DataStore.DataDirectory, "running.png"), true);
            Check(!ringFilling && ringGeometry.TrimEnd == 1 && engine.Remaining < 90, "Fill hands off to countdown without delaying elapsed time");
            var resizeGeneration = ringGeneration; var beforeResizeRemaining = engine.Remaining;
            AppWindow.Resize(new Windows.Graphics.SizeInt32(size.Width + 80, size.Height)); await Task.Delay(160);
            AppWindow.Resize(size); await Task.Delay(160);
            Check(ringGeneration == resizeGeneration && ringAnimating && engine.Remaining < beforeResizeRemaining, "Resizing a running timer keeps the existing composition animation and clock");
            UpdateTimerPointer(new Windows.Foundation.Point(110, 15));
            Check(timerControlsShown, "Circle interior above the digits reveals timer controls");
            UpdateTimerPointer(new Windows.Foundation.Point(110, TimerControlsBottom - 0.1));
            Check(timerControlsShown, "Hover remains active just above the new lower boundary");
            UpdateTimerPointer(new Windows.Foundation.Point(110, TimerControlsBottom));
            Check(!timerControlsShown, "Hover stops eight DIP above the start button");
            UpdateTimerPointer(new Windows.Foundation.Point(110, StartButtonTop + 22));
            Check(!timerControlsShown, "The start button area no longer reveals pause and stop");
            UpdateTimerPointer(new Windows.Foundation.Point(80, 110));
            Check(timerControlsShown, "The pause and stop row remains inside the hover area");
            UpdateTimerPointer(new Windows.Foundation.Point(3, 3));
            Check(!timerControlsShown, "Outside circular bounds hides timer controls");
            timerPointerInside = true; UpdateTimerHover();
            SetTimerButtonHovered(pauseButton, true); SetTimerButtonHovered(endButton, true);
            Check(timerControlsShown && timerActions.IsHitTestVisible && timerBlurBrush != null, "Hover reveals pause and stop with composition blur");
            await CaptureDiagnosticAsync(); File.Copy(System.IO.Path.Combine(DataStore.DataDirectory, "ui-focus-light.png"), System.IO.Path.Combine(DataStore.DataDirectory, "timer-hover.png"), true);
            timerPointerInside = false; UpdateTimerHover();
            Check(!hoveredTimerButtons.Contains(pauseButton) && !hoveredTimerButtons.Contains(endButton), "Hidden timer actions reset their hover scale state");
            pauseButton.Focus(FocusState.Keyboard); UpdateTimerHover();
            Check(timerControlsShown, "Keyboard can reveal timer controls without hover");
            taskInput.Focus(FocusState.Programmatic); UpdateTimerHover();
            Check(!timerControlsShown, "Leaving pointer and keyboard focus restores unobstructed time");
            Check(stateText.Text == "", "Running timer has no focus-time label");
            var geometry = ringGeometry; await Task.Delay(250); ToggleTimer(); var trim = ringGeometry!.TrimStart;
            Check(stateText.Text == "已暂停" && stateText.FontSize == 18, "Paused timer shows only the larger paused label");
            Check(engine.State.Status == TimerStatus.Paused && timeEditor.IsReadOnly && trim > 0 && trim < 0.2 && ringGeometry.TrimEnd == 1 && ringVisual!.RotationAngleInDegrees == 0, "Paused ring preserves elapsed trim and fixed origin");
            timerPointerInside = true; UpdateTimerHover(); SetTimerButtonHovered(pauseButton, true);
            await CaptureDiagnosticAsync();
            File.Copy(System.IO.Path.Combine(DataStore.DataDirectory, "ui-focus-light.png"), System.IO.Path.Combine(DataStore.DataDirectory, "continue-hover.png"), true);
            // A real pointer can leave the synthetic hover position while a capture awaits.
            // Exercise both glyph changes synchronously from a known in-bounds position.
            UpdateTimerPointer(new Windows.Foundation.Point(80, 110)); SetTimerButtonHovered(pauseButton, true);
            ToggleTimer(); var retainedOnResume = hoveredTimerButtons.Contains(pauseButton);
            ToggleTimer();
            Check(retainedOnResume && hoveredTimerButtons.Contains(pauseButton), "Pause and continue retain hover while switching glyphs");
            SetTimerButtonHovered(pauseButton, false); timerPointerInside = false; UpdateTimerHover();
            Check(ReferenceEquals(geometry, ringGeometry) && ringGeometry.TrimStart >= trim && ringGeometry.TrimEnd == 1 && ringVisual!.RotationAngleInDegrees == 0, "Resume keeps the same ring renderer, origin and direction");
            engine.State.RemainingSeconds = engine.State.DurationSeconds * 0.875; RenderTimer(); AnimateRing(); await CaptureDiagnosticAsync();
            File.Copy(System.IO.Path.Combine(DataStore.DataDirectory, "ui-focus-light.png"), System.IO.Path.Combine(DataStore.DataDirectory, "ring-eighth.png"), true);
            engine.State.RemainingSeconds = engine.State.DurationSeconds * 0.75; RenderTimer(); AnimateRing(); await CaptureDiagnosticAsync();
            File.Copy(System.IO.Path.Combine(DataStore.DataDirectory, "ui-focus-light.png"), System.IO.Path.Combine(DataStore.DataDirectory, "ring-quarter.png"), true);
            EndTimer();
            Check(!dialogOpen && engine.State.Status == TimerStatus.Ready && stateText.Text == "", "Stop ends immediately without confirmation and clears paused label");
            Check(startButton.IsEnabled && !timerActions.IsHitTestVisible && timerActive == false, "Ending returns to editable time and play button");
            Check(!ringFilling && ringGeometry.TrimEnd == 0 && ringVisual!.Opacity == 0, "Ending restores an empty ring");
            ToggleTimer(); ToggleTimer();
            Check(engine.State.Status == TimerStatus.Paused && !ringFilling && ringGeometry.TrimEnd == 1, "Pause during start fill cancels it and preserves actual countdown progress");
            await Task.Delay(450);
            Check(engine.State.Status == TimerStatus.Paused && !ringAnimating, "Cancelled fill completion cannot restart a paused ring");
            EndTimer(); ToggleTimer(); EndTimer(); await Task.Delay(450);
            Check(engine.State.Status == TimerStatus.Ready && !ringFilling && ringGeometry.TrimEnd == 0 && ringVisual!.Opacity == 0, "Stop during start fill leaves the ring empty after stale callbacks");
            await CaptureDiagnosticAsync(); File.Copy(System.IO.Path.Combine(DataStore.DataDirectory, "ui-focus-light.png"), System.IO.Path.Combine(DataStore.DataDirectory, "ready.png"), true);
            first.CreatedAt = DateTimeOffset.Now.AddDays(-1); RenderTasks();
            Check(taskList.Children.Count >= 2, "Unfinished task from yesterday remains visible");
            engine.State.DurationSeconds = engine.State.RemainingSeconds = 3;
            ToggleTimer(); hidden = true; StopRing(); native.Hide();
            await Task.Delay(4800);
            Check(engine.State.Phase == Phase.Focus && engine.State.Status == TimerStatus.Ready, "Focus completion waits for manual start without rest");
            Check(!hidden, "Focus completion restores full main window");
            Check(data.Sessions.Last().Completed, "Completed session is recorded");
            Check(data.Slices.Sum(s => s.Seconds) >= 3, "Focused seconds are saved, without task completion dependency");
            await Task.Delay(1000);
            Check(engine.State.Status == TimerStatus.Ready && startButton.IsEnabled && timerActive == false, "Completed focus remains ready until user starts it");
            engine.State.DurationSeconds = engine.State.RemainingSeconds = 60;
            ToggleTimer(); await Task.Delay(1600); ToggleTimer();
            var remaining = engine.Remaining; await Task.Delay(700);
            Check(engine.State.Status == TimerStatus.Paused && Math.Abs(engine.Remaining - remaining) < 0.01, "Pause freezes remaining time");
            engine.EndEarly(); RenderTimer(); AnimateRing();
            await store.SaveAsync(data);
            using (var readback = new DataStore())
            {
                var loaded = readback.Load(); Check(loaded.Tasks.Any(t => t.Title == first.Title) && loaded.Slices.Count > 0, "SQLite write/read round-trip");
            }
            var backup = System.IO.Path.Combine(DataStore.DataDirectory, "smoke-backup.json"); await File.WriteAllTextAsync(backup, JsonSerializer.Serialize(data));
            var imported = DataStore.ReadBackup(backup); Check(imported.Tasks.Count == data.Tasks.Count, "Backup round-trip validates");
            var timeBefore = engine.SecondsOn(day); DeleteTask(second);
            Check(second.Deleted && !taskRows.ContainsKey(second.Id) && !root.Children.OfType<InfoBar>().Any(), "Deleting a task removes it without an operation banner");
            await store.SaveAsync(data);
            Check(engine.SecondsOn(day) == timeBefore, "Deleting a task does not change focused duration");
            RenderTasks(); UpdateStatistics(); RefreshHeatmap(); await CaptureDiagnosticAsync();
            Check(taskRows[first.Id].Root.Children.OfType<Button>().Count() == 2 && ((Microsoft.UI.Xaml.Media.SolidColorBrush)taskRows[first.Id].Root.Background).Color.A == 0, "Task title is plain text with no persistent selection");
            SwitchPage(false, chart: true); await Task.Delay(250);
            Check(chartPage.Visibility == Visibility.Visible && focusPage.Visibility == Visibility.Collapsed && !ringAnimating, "Chart navigation opens dedicated page and stops invisible ring animation");
            Check(chartDays.Count == DateTime.Today.Day && chartDays[^1].Date == DateTime.Today && !chartNext.IsEnabled, "Current month ends today with no future dates");
            Check(chartDays[^1].Seconds == engine.SecondsOn(day), "Chart uses the same focus history as the heatmap");
            ChangeChartMonth(-1); await Task.Delay(150);
            Check(chartDays.Count == DateTime.DaysInMonth(chartMonth.Year, chartMonth.Month) && chartDays.All(d => d.Seconds == 0), "Empty previous month renders every day at zero");
            await CaptureDiagnosticAsync(); File.Copy(System.IO.Path.Combine(DataStore.DataDirectory, "ui-chart-light.png"), System.IO.Path.Combine(DataStore.DataDirectory, "chart-empty.png"), true);
            ChangeChartMonth(1);
            for (int i = 1; i < DateTime.Today.Day; i++) data.Slices.Add(new FocusSlice { SessionId = "chart-fixture", Day = new DateTime(DateTime.Today.Year, DateTime.Today.Month, i).ToString("yyyy-MM-dd"), Seconds = i % 5 == 0 ? 0 : (i * 37 % 180) * 60 });
            RefreshChart(); await CaptureDiagnosticAsync(() => ShowChartTip(Math.Max(0, chartDays.Count - 2)));
            Check(chartTip!.Visibility == Visibility.Visible && chartTipText.Text.Contains("月"), "Chart point displays date and duration immediately");
            HideChartTip(); Check(chartTip.Visibility == Visibility.Collapsed, "Leaving chart hides point details");
            var chartSize = AppWindow.Size;
            var retainedChartLine = chartLine; var retainedChartDot = chartDayDots[0]; var retainedChartTip = chartTip;
            foreach (var width in new[] { 980, 1600 }) {
                AppWindow.Resize(new Windows.Graphics.SizeInt32(width, chartSize.Height)); await Task.Delay(200);
                Check(chartPoints.Count == chartDays.Count && chartPoints.All(p => p.X >= 52 && p.X <= chartHost.ActualWidth - 19 && p.Y >= 17 && p.Y <= chartHost.ActualHeight - 37), $"Chart points remain within axes at width {width}");
            }
            AppWindow.Resize(chartSize); await Task.Delay(200);
            Check(ReferenceEquals(retainedChartLine, chartLine) && ReferenceEquals(retainedChartDot, chartDayDots[0]) && ReferenceEquals(retainedChartTip, chartTip), "Resizing reuses chart line, dots and tooltip instead of rebuilding controls");
            data.Settings.Theme = "Dark"; BuildUI(); ApplyMaterial(); await Task.Delay(600); await CaptureDiagnosticAsync(() => ShowChartTip(Math.Max(0, chartDays.Count - 2)));
            Check(chartOpen && chartPage.Visibility == Visibility.Visible, "Theme rebuild preserves chart navigation");
            SwitchPage(false);
            data.Settings.Theme = "Dark"; BuildUI(); ApplyMaterial(); await Task.Delay(600); await CaptureDiagnosticAsync();
            Check(root.RequestedTheme == ElementTheme.Dark, "Dark theme builds");
            await CaptureDiagnosticAsync(() => ShowHeatTip(heatButtons[day]));
            File.Copy(System.IO.Path.Combine(DataStore.DataDirectory, "ui-focus-dark.png"), System.IO.Path.Combine(DataStore.DataDirectory, "hover-dark.png"), true);
            HideHeatTip();
            SwitchPage(true); await CaptureDiagnosticAsync();
            data.Settings.Theme = "Light"; BuildUI(); ApplyMaterial(); await Task.Delay(600); await CaptureDiagnosticAsync();
            var settingsStack = (StackPanel)((ScrollViewer)settingsPage.Children[0]).Content;
            var settingsBounds = settingsStack.TransformToVisual(settingsPage).TransformBounds(new Windows.Foundation.Rect(0, 0, settingsStack.ActualWidth, settingsStack.ActualHeight));
            Check(settingsBounds.Left >= -0.5 && settingsBounds.Right <= settingsPage.ActualWidth + 0.5 && settingsStack.ActualWidth > 400, $"Settings content stays within the page without horizontal clipping (left={settingsBounds.Left:F1}, right={settingsBounds.Right:F1}, page={settingsPage.ActualWidth:F1})");
            AppWindow.Resize(new Windows.Graphics.SizeInt32((int)(520 * layoutScale), (int)(500 * layoutScale))); await Task.Delay(250);
            await CaptureDiagnosticAsync();
            File.Copy(System.IO.Path.Combine(DataStore.DataDirectory, "ui-settings-light.png"), System.IO.Path.Combine(DataStore.DataDirectory, "minimum-settings.png"), true);
            var settingsScroll = (ScrollViewer)settingsPage.Children[0];
            Check(settingsScroll.ScrollableWidth < 1 && settingsScroll.ScrollableHeight > 0, "Minimum window keeps settings accessible with vertical scrolling");
            SwitchPage(false, chart: true); await CaptureDiagnosticAsync();
            File.Copy(System.IO.Path.Combine(DataStore.DataDirectory, "ui-chart-light.png"), System.IO.Path.Combine(DataStore.DataDirectory, "minimum-chart.png"), true);
            var chartBounds = chartHost.TransformToVisual(chartPage).TransformBounds(new Windows.Foundation.Rect(0, 0, chartHost.ActualWidth, chartHost.ActualHeight));
            Check(chartBounds.Right <= chartPage.ActualWidth + 1 && chartBounds.Bottom <= chartPage.ActualHeight + 1, "Minimum window contains chart axes and plot");
            AppWindow.Resize(size); await Task.Delay(200);
            SwitchPage(false); await CaptureDiagnosticAsync();
            if (Environment.GetEnvironmentVariable("XILI_SKIP_IDLE_METRICS") != "1")
            {
            await Task.Delay(15000);
            var process = Process.GetCurrentProcess(); var initial = process.TotalProcessorTime; var sw = Stopwatch.StartNew();
            await Task.Delay(10000); process.Refresh(); report.Add($"METRIC visible idle CPU seconds={(process.TotalProcessorTime-initial).TotalSeconds:F4} wall={sw.Elapsed.TotalSeconds:F2} workingMB={process.WorkingSet64/1048576.0:F1} privateMB={process.PrivateMemorySize64/1048576.0:F1}");
            hidden=true;StopRing();native.Hide();process.Refresh();initial=process.TotalProcessorTime;sw.Restart();await Task.Delay(10000);process.Refresh();
            report.Add($"METRIC tray idle CPU seconds={(process.TotalProcessorTime-initial).TotalSeconds:F4} wall={sw.Elapsed.TotalSeconds:F2} workingMB={process.WorkingSet64/1048576.0:F1} privateMB={process.PrivateMemorySize64/1048576.0:F1}");
            }
            engine.EndEarly(); engine.SetDuration(25); RenderTimer(); AnimateRing();
            ShowMain(); await CaptureDiagnosticAsync();
            File.Copy(Path.Combine(DataStore.DataDirectory, "ui-focus-light.png"), Path.Combine(DataStore.DataDirectory, "readme-focus.png"), true);
            SwitchPage(false, chart: true); await CaptureDiagnosticAsync();
            File.Copy(Path.Combine(DataStore.DataDirectory, "ui-chart-light.png"), Path.Combine(DataStore.DataDirectory, "readme-history.png"), true);
            await store.SaveAsync(data); report.Add("COMPLETE");
        }
        catch (Exception ex) { report.Add("FAIL " + ex); }
        await File.WriteAllLinesAsync(System.IO.Path.Combine(DataStore.DataDirectory, "smoke-results.txt"), report);
    }
}
