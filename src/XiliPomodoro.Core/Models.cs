namespace XiliPomodoro.Core;

// Retained only to recognize and migrate early development rest snapshots.
public enum Phase { Focus, ShortBreak, LongBreak }
public enum TimerStatus { Ready, Running, Paused }
public sealed class Preferences
{
    public int FocusMinutes { get; set; } = 25;
    public string Theme { get; set; } = "Light";
    public bool AlwaysOnTop { get; set; }
    public bool CloseToTray { get; set; } = true;
    public void Validate()
    {
        FocusMinutes = Math.Clamp(FocusMinutes, 1, 180);
        if (Theme is not ("System" or "Light" or "Dark")) Theme = "Light";
    }
}
public sealed class FocusTask
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset? CompletedAt { get; set; }
    public string? CompletedDay { get; set; }
    public bool Deleted { get; set; }
}
public sealed class FocusSlice
{
    public string SessionId { get; set; } = "";
    public string Day { get; set; } = "";
    public double Seconds { get; set; }
}
public sealed class FocusSession
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string? TaskId { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public bool Completed { get; set; }
}
public sealed class TimerSnapshot
{
    public Phase Phase { get; set; }
    public TimerStatus Status { get; set; }
    public double DurationSeconds { get; set; } = 1500;
    public double RemainingSeconds { get; set; } = 1500;
    public string? SessionId { get; set; }
}
public sealed class AppData
{
    public int SchemaVersion { get; set; } = 1;
    public Preferences Settings { get; set; } = new();
    public TimerSnapshot Timer { get; set; } = new();
    public List<FocusTask> Tasks { get; set; } = [];
    public List<FocusSlice> Slices { get; set; } = [];
    public List<FocusSession> Sessions { get; set; } = [];
}
public interface IClock
{
    DateTimeOffset Now { get; }
    double MonotonicSeconds { get; }
}
public sealed class SystemClock : IClock
{
    public DateTimeOffset Now => DateTimeOffset.Now;
    public double MonotonicSeconds => System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency;
}
