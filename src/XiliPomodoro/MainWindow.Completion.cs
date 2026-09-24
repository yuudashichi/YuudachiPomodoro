using Microsoft.UI.Xaml.Controls;

namespace XiliPomodoro;

public sealed partial class MainWindow
{
    bool completionPending;
    ContentDialog? completionDialog;

    async Task ShowCompletionReminderAsync()
    {
        // A task-edit or backup dialog may already be open. Its close handler
        // will show the pending reminder, without discarding the user's input.
        if (!completionPending || dialogOpen || quitting) return;
        completionPending = false;
        dialogOpen = true;
        completionDialog = new ContentDialog
        {
            XamlRoot = root.XamlRoot, RequestedTheme = root.RequestedTheme,
            Title = "时间到了", Content = "本轮专注已结束。",
            CloseButtonText = "知道了", DefaultButton = ContentDialogButton.Close
        };
        try { await completionDialog.ShowAsync(); }
        finally { completionDialog = null; dialogOpen = false; }
    }
}
