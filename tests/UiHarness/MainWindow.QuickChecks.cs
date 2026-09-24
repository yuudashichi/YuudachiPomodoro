using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using XiliPomodoro.Core;

namespace XiliPomodoro;

public sealed partial class MainWindow
{
    bool quickStarted;
    async Task RunQuickChecks()
    {
        if (quickStarted || Environment.GetEnvironmentVariable("XILI_DATA_DIR") == null) return;
        quickStarted = true;
        var report = new List<string>();
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); report.Add("PASS " + message); }
        try
        {
            await Task.Delay(300);
            Check(!settingsBuilt && !chartBuilt && timerBlurBrush == null && ReferenceEquals(Brush("00000000"), Brush("00000000")), "Startup defers unused pages and blur; shared brushes are reused");
            engine.SetDuration(1); ToggleTimer(); await Task.Delay(150);
            timerPointerInside = true; UpdateTimerHover();
            Check(timerControlsShown && timerBlurBrush != null && timerTextSurface != null && timerBlurVisual!.Size.X > 0, "Running timer retains the original blur and hover controls");
            EndTimer();
            Check(timerBlurBrush == null && timerTextSurface == null && timerBlurVisual == null, "Ending releases offscreen blur resources");
            SwitchPage(false, animate: false, chart: true); await Task.Delay(150);
            Check(chartBuilt && !settingsBuilt && chartDays.Count > 0 && chartLine != null, "Chart is created on first use and still draws");
            SwitchPage(true, animate: false);
            var edit = Dialog("编辑任务", new TextBox { Text = "保留正在输入的内容" }); await Task.Delay(150);
            OnFocusCompleted();
            Check(completionPending && completionDialog == null, "Completion waits for an existing dialog instead of dropping it");
            // The active ContentDialog is the only popup child in the XamlRoot.
            var existing = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetOpenPopupsForXamlRoot(root.XamlRoot)
                .Select(p => p.Child).OfType<ContentDialog>().First();
            existing.Hide(); await edit; await Task.Delay(250);
            Check(completionDialog?.IsLoaded == true && !completionPending && !settingsOpen, "Queued completion displays once after the edit dialog closes");
            completionDialog!.Hide();
            for (var i = 0; dialogOpen && i < 30; i++) await Task.Delay(50);
            engine.State.DurationSeconds = engine.State.RemainingSeconds = 1;
            ToggleTimer(); SwitchPage(true, animate: false); hidden = true; StopRing(); native.Hide(); ScheduleHeartbeat();
            await Task.Delay(1400);
            Check(!hidden && !settingsOpen && completionDialog?.IsLoaded == true && (string)completionDialog.Title == "时间到了", "Actual countdown completion restores the window and shows the reminder");
            completionDialog!.Hide();
            for (var i = 0; dialogOpen && i < 30; i++) await Task.Delay(50);
            Check(!dialogOpen && completionDialog == null && engine.State.Status == TimerStatus.Ready, "Acknowledging the reminder waits for manual restart");
            report.Add("COMPLETE");
        }
        catch (Exception ex) { report.Add("FAIL " + ex); }
        await File.WriteAllLinesAsync(System.IO.Path.Combine(DataStore.DataDirectory, "smoke-results.txt"), report);
    }
}
