namespace XiliPomodoro.Core;

/// <summary>The clock owns elapsed time; rendering and persistence never drive it.</summary>
public sealed class TimerEngine
{
    readonly IClock clock;
    readonly TimeZoneInfo zone;
    double anchor;
    DateTimeOffset wallAnchor;
    public AppData Data { get; }
    public TimerSnapshot State => Data.Timer;
    public event Action? FocusCompleted;
    public TimerEngine(AppData data, IClock clock, TimeZoneInfo? zone = null)
    {
        Data = data; this.clock = clock; this.zone = zone ?? TimeZoneInfo.Local;
        data.Settings.Validate();
        // Restart is a fresh ready state. Preserve logged work, never credit offline time.
        if (!double.IsFinite(State.DurationSeconds) || State.DurationSeconds <= 0 || State.Phase != Phase.Focus)
            State.DurationSeconds = data.Settings.FocusMinutes * 60;
        State.DurationSeconds = Math.Clamp(State.DurationSeconds, 60, 10800);
        State.Status = TimerStatus.Paused;
        EndSession(false, clock.Now);
        ResetReady();
    }
    void ResetAnchor() { anchor = clock.MonotonicSeconds; wallAnchor = clock.Now; }
    public double Remaining => State.Status == TimerStatus.Running
        ? Math.Max(0, State.RemainingSeconds - Math.Max(0, clock.MonotonicSeconds - anchor)) : State.RemainingSeconds;
    public void SetDuration(int minutes)
        => SetDurationSeconds(Math.Clamp(minutes, 1, 180) * 60);
    public void SetDurationSeconds(int seconds)
    {
        if (State.Status != TimerStatus.Ready) return;
        State.DurationSeconds = State.RemainingSeconds = Math.Clamp(seconds, 60, 10800);
    }
    public void Start()
    {
        if (State.Status == TimerStatus.Running) return;
        if (State.SessionId == null)
        {
            var session = new FocusSession { StartedAt = clock.Now };
            Data.Sessions.Add(session); State.SessionId = session.Id;
        }
        State.Status = TimerStatus.Running; ResetAnchor();
    }
    public void Pause()
    {
        if (State.Status != TimerStatus.Running) return;
        Tick();
        if (State.Status == TimerStatus.Running) { Checkpoint(); State.Status = TimerStatus.Paused; }
    }
    public void Checkpoint()
    {
        if (State.Status != TimerStatus.Running) return;
        var elapsed = Math.Min(State.RemainingSeconds, Math.Max(0, clock.MonotonicSeconds - anchor));
        if (elapsed > 0 && State.SessionId != null)
            AddFocusTime(wallAnchor, elapsed, State.SessionId);
        State.RemainingSeconds = Math.Max(0, State.RemainingSeconds - elapsed);
        ResetAnchor();
    }
    void AddFocusTime(DateTimeOffset start, double seconds, string sessionId)
    {
        // Split at local midnight, using the offset of each day (also handles DST).
        var cursor = start;
        while (seconds > 0.0001)
        {
            var local = TimeZoneInfo.ConvertTime(cursor, zone);
            var nextDate = local.Date.AddDays(1);
            var midnight = new DateTimeOffset(nextDate, zone.GetUtcOffset(nextDate));
            var part = Math.Min(seconds, Math.Max(1, (midnight - cursor).TotalSeconds));
            var day = local.ToString("yyyy-MM-dd");
            var slice = Data.Slices.LastOrDefault(s => s.SessionId == sessionId && s.Day == day);
            if (slice == null) Data.Slices.Add(new FocusSlice { SessionId = sessionId, Day = day, Seconds = part });
            else slice.Seconds += part;
            seconds -= part; cursor = cursor.AddSeconds(part);
        }
    }
    public void Tick()
    {
        if (State.Status != TimerStatus.Running || Remaining > 0) return;
        // Preserve the scheduled end even if the UI thread was delayed.
        var overrun = Math.Max(0, clock.MonotonicSeconds - anchor - State.RemainingSeconds);
        Checkpoint();
        EndSession(true, clock.Now.AddSeconds(-overrun));
        ResetReady();
        FocusCompleted?.Invoke();
    }
    public void EndEarly()
    {
        Checkpoint();
        EndSession(false, clock.Now);
        ResetReady();
    }
    void EndSession(bool completed, DateTimeOffset end)
    {
        var session = Data.Sessions.FirstOrDefault(s => s.Id == State.SessionId);
        if (session != null) { session.Completed = completed; session.EndedAt = end; }
        State.SessionId = null;
    }
    void ResetReady()
    {
        State.Phase = Phase.Focus; State.Status = TimerStatus.Ready;
        State.RemainingSeconds = State.DurationSeconds;
        ResetAnchor();
    }
    public double SecondsOn(string day) => Data.Slices.Where(s => s.Day == day).Sum(s => s.Seconds);
    public static void SetCompleted(FocusTask task, bool completed, DateTimeOffset now)
    {
        task.CompletedAt = completed ? now : null;
        task.CompletedDay = completed ? now.ToString("yyyy-MM-dd") : null;
    }
    public IEnumerable<FocusTask> TodayTasks(DateTimeOffset now) => Data.Tasks.Where(t => !t.Deleted &&
        (t.CompletedAt == null || t.CompletedDay == now.ToString("yyyy-MM-dd")));
}
