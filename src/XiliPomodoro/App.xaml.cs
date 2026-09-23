using Microsoft.UI.Xaml;
namespace XiliPomodoro;
public partial class App : Application
{
    MainWindow? window;
    Mutex? mutex;
    public App() { InitializeComponent(); ElementSoundPlayer.State = ElementSoundPlayerState.Off; UnhandledException += (_, e) => { try { File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "error.log"), DateTime.Now + " " + e.Exception + Environment.NewLine); } catch { } }; }
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var profile = Environment.GetEnvironmentVariable("XILI_DATA_DIR");
        mutex = new Mutex(true, "Local\\XiliPomodoro.Pomodoro." + (string.IsNullOrWhiteSpace(profile) ? "Main" : Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(profile)))[..12]), out var created);
        if (!created) { NativeMethods.WakeExisting(); Exit(); return; }
        window = new MainWindow(); window.Activate();
    }
}
