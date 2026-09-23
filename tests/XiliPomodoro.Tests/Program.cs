using XiliPomodoro.Core;

var tests = new (string Name, Action Test)[]
{
    ("Legacy disabled appearance flags are ignored on load and backup round-trip", () => {
        var prefs=System.Text.Json.JsonSerializer.Deserialize<Preferences>("{\"Animations\":false,\"Transparency\":false,\"Theme\":\"Dark\",\"AlwaysOnTop\":true}")!;
        prefs.Validate();Is(prefs.Theme=="Dark" && prefs.AlwaysOnTop);
        var json=System.Text.Json.JsonSerializer.Serialize(prefs);Is(!json.Contains("Animations") && !json.Contains("Transparency"));
    }),
    ("Fresh settings default to light and ignore retired notification settings", () => {
        var p = System.Text.Json.JsonSerializer.Deserialize<Preferences>("{\"Notifications\":true}")!;
        Is(p.Theme=="Light");Is(!System.Text.Json.JsonSerializer.Serialize(p).Contains("Notifications"));
    }),
    ("Restart from ready, paused or running keeps custom duration but resets remaining", () => {
        foreach(var status in new[]{TimerStatus.Ready,TimerStatus.Paused,TimerStatus.Running}) {
            var data=new AppData { Timer=new TimerSnapshot { Status=status, DurationSeconds=1530, RemainingSeconds=642 } };
            var e=new TimerEngine(data,new FakeClock());Is(e.State.Status==TimerStatus.Ready);Near(e.Remaining,1530);
        }
    }),
    ("Inline duration accepts minutes and mm:ss, rejects invalid limits", () => {
        foreach(var (text,seconds) in new[]{("1",60),("25",1500),("25:30",1530),("180:00",10800),(" 01：01 ",61)}) { Is(DurationInput.TryParse(text,out var result));Near(result,seconds); }
        foreach(var text in new[]{"","0","00:59","181","180:01","1:60","-1","1.5","abc","1:2:3","999999999999"}) Is(!DurationInput.TryParse(text,out _));
    }),
    ("Duration cannot be changed while running or paused", () => {
        var (e,c)=Setup();e.SetDurationSeconds(91);Near(e.Remaining,91);e.Start();c.Advance(1);e.SetDurationSeconds(600);Near(e.Remaining,90);e.Pause();e.SetDurationSeconds(300);Near(e.Remaining,90);Near(e.State.DurationSeconds,91);e.EndEarly();e.SetDurationSeconds(120);Near(e.Remaining,120);
    }),
    ("Monotonic timing ignores system clock changes", () => {
        var (e,c) = Setup(); e.Start(); c.Advance(37); c.Now = c.Now.AddHours(-2); Near(e.Remaining,1463); e.Checkpoint(); Near(e.Data.Slices.Sum(s=>s.Seconds),37);
    }),
    ("Pause and resume exclude paused time", () => {
        var (e,c) = Setup(); e.Start(); c.Advance(42); e.Pause(); c.Advance(100); Near(e.Remaining,1458); e.Start(); c.Advance(18); e.EndEarly(); Near(e.Data.Slices.Sum(s=>s.Seconds),60); Is(!e.Data.Sessions.Single().Completed); Is(e.State.Status==TimerStatus.Ready);
    }),
    ("Completion waits for manual start and retains chosen duration", () => {
        var (e,c)=Setup();var events=0;e.FocusCompleted+=()=>events++;e.SetDurationSeconds(91);e.Start();c.Advance(100);e.Tick();Is(e.State.Status==TimerStatus.Ready);Near(e.Remaining,91);Is(e.Data.Sessions.Single().Completed);Near((e.Data.Sessions.Single().EndedAt!.Value-e.Data.Sessions.Single().StartedAt).TotalSeconds,91);c.Advance(3600);e.Tick();Is(events==1 && e.Data.Sessions.Count==1);Near(e.Data.Slices.Sum(s=>s.Seconds),91);
    }),
    ("Repeated sessions never enter a rest phase", () => {
        var (e,c)=Setup();e.SetDuration(1);for(int i=0;i<6;i++){e.Start();c.Advance(60);e.Tick();Is(e.State.Status==TimerStatus.Ready && e.State.Phase==Phase.Focus);}Is(e.Data.Sessions.Count==6);Near(e.SecondsOn("2026-09-20"),360);
    }),
    ("Early end preserves actual time and the configured duration", () => {
        var (e,c)=Setup();e.SetDurationSeconds(123);e.Start();c.Advance(100);e.EndEarly();Near(e.Remaining,123);Near(e.Data.Slices.Single().Seconds,100);Is(!e.Data.Sessions.Single().Completed);
    }),
    ("Legacy rest and selection migrate without changing historical records", () => {
        foreach(var phase in new[]{1,2}) {
            var json="""
            {"Settings":{"FocusMinutes":40,"ShortBreakMinutes":5,"LongBreakEvery":4},"Timer":{"Phase":PHASE,"Status":1,"DurationSeconds":300,"RemainingSeconds":123,"SelectedTaskId":"old","CompletedInCycle":2},"Slices":[{"Day":"2026-09-20","Seconds":60}],"Tasks":[{"Id":"old","Title":"keep"}]}
            """;
            var data=System.Text.Json.JsonSerializer.Deserialize<AppData>(json.Replace("PHASE",phase.ToString()))!;
            var e=new TimerEngine(data,new FakeClock(),TimeZoneInfo.Utc);Is(e.State.Status==TimerStatus.Ready && e.State.Phase==Phase.Focus);Near(e.Remaining,2400);Near(e.SecondsOn("2026-09-20"),60);Is(e.Data.Tasks.Single().Title=="keep");e.Start();Is(e.Data.Sessions.Single().TaskId==null);
        }
    }),
    ("Daily history merges records and fills missing dates, excluding future dates", () => {
        var series=FocusHistory.Month(new[]{new FocusSlice{Day="2026-09-01",Seconds=60},new FocusSlice{Day="2026-09-01",Seconds=90},new FocusSlice{Day="2026-09-04",Seconds=600},new FocusSlice{Day="2026-08-31",Seconds=100}},new DateTime(2026,9,1),new DateTime(2026,9,3));
        Is(series.Count==3);Near(series[0].Seconds,150);Near(series[1].Seconds,0);Near(series[2].Seconds,0);
    }),
    ("History supports leap months, year boundaries, empty and future months", () => {
        Is(FocusHistory.Month([],new DateTime(2024,2,1),new DateTime(2026,9,20)).Count==29);
        Is(FocusHistory.Month([],new DateTime(2025,12,1),new DateTime(2026,1,1)).Count==31);
        Is(FocusHistory.Month([],new DateTime(2026,2,1),new DateTime(2026,1,1)).Count==0);
    }),
    ("Midnight splits actual work across calendar days", () => {
        var (e,c)=Setup();c.Now=new DateTimeOffset(2026,9,20,23,59,30,TimeSpan.Zero);e.Start();c.Advance(90);e.Checkpoint();Near(e.SecondsOn("2026-09-20"),30);Near(e.SecondsOn("2026-09-21"),60);
    }),
    ("Checkpoint is idempotent and caps at duration", () => {
        var (e,c)=Setup();e.SetDuration(1);e.Start();c.Advance(20);e.Checkpoint();e.Checkpoint();Near(e.Data.Slices.Sum(s=>s.Seconds),20);c.Advance(50);e.Tick();Near(e.Data.Slices.Sum(s=>s.Seconds),60);Near(e.Remaining,60);
    }),
    ("Restart resets configured duration without losing logged work", () => {
        var (e,c)=Setup();e.Start();c.Advance(32);e.Checkpoint();c.Advance(3600);var resumed=new TimerEngine(e.Data,c,TimeZoneInfo.Utc);Is(resumed.State.Status==TimerStatus.Ready && resumed.State.SessionId==null);Near(resumed.Remaining,1500);Is(!resumed.Data.Sessions.Single().Completed && resumed.Data.Sessions.Single().EndedAt!=null);Near(resumed.Data.Slices.Sum(s=>s.Seconds),32);
    }),
    ("Unfinished tasks carry; completed tasks stay on completion day", () => {
        var (e,c)=Setup();var todo=new FocusTask{Title="unfinished",CreatedAt=c.Now.AddDays(-4)};var done=new FocusTask{Title="done"};TimerEngine.SetCompleted(done,true,c.Now.AddDays(-1));e.Data.Tasks.AddRange([todo,done]);Is(e.TodayTasks(c.Now).Single()==todo);TimerEngine.SetCompleted(done,false,c.Now);Is(e.TodayTasks(c.Now).Count()==2);TimerEngine.SetCompleted(done,true,c.Now);Is(done.CompletedDay=="2026-09-20");
    }),
    ("Deleting a task leaves focused time intact", () => {
        var (e,c)=Setup();var t=new FocusTask{Title="task"};e.Data.Tasks.Add(t);e.Start();c.Advance(100);e.EndEarly();t.Deleted=true;Near(e.SecondsOn("2026-09-20"),100);Is(!e.TodayTasks(c.Now).Any());
    }),
    ("Long UI delay never starts another focus automatically", () => {
        var (e,c)=Setup();e.SetDuration(1);e.Start();c.Advance(600);e.Tick();Is(e.State.Status==TimerStatus.Ready && e.State.Phase==Phase.Focus);Near(e.Data.Slices.Sum(s=>s.Seconds),60);Is(e.Data.Sessions.Single().Completed);
    })
};
var failed=0;foreach(var (name,test) in tests){try{test();Console.WriteLine("PASS "+name);}catch(Exception ex){failed++;Console.WriteLine("FAIL "+name+": "+ex.Message);}}Console.WriteLine($"{tests.Length-failed}/{tests.Length} passed");return failed==0?0:1;
static (TimerEngine,FakeClock) Setup(){var c=new FakeClock();return(new TimerEngine(new AppData(),c,TimeZoneInfo.Utc),c);}
static void Is(bool condition){if(!condition)throw new Exception("Assertion failed");}
static void Near(double a,double b){if(Math.Abs(a-b)>0.01)throw new Exception($"Expected {b}; actual {a}");}
sealed class FakeClock:IClock{public DateTimeOffset Now{get;set;}=new(2026,9,20,12,0,0,TimeSpan.Zero);public double MonotonicSeconds{get;set;}public void Advance(double seconds){Now=Now.AddSeconds(seconds);MonotonicSeconds+=seconds;}}
