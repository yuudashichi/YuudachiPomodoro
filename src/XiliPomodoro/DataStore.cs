using System.Text.Json;
using Microsoft.Data.Sqlite;
using XiliPomodoro.Core;
namespace XiliPomodoro;

public sealed class DataStore : IDisposable
{
    // Resolve relative to the executable, independent of the shortcut's working directory.
    public static string DataDirectory { get; } = Path.GetFullPath(
        Environment.GetEnvironmentVariable("XILI_DATA_DIR") is { Length: > 0 } directory ? directory : "data",
        AppContext.BaseDirectory);
    readonly SqliteConnection connection;
    readonly Dictionary<string, string> saved = [];
    readonly SemaphoreSlim gate = new(1);
    public DataStore()
    {
        Directory.CreateDirectory(DataDirectory);
        connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.Combine(DataDirectory, "xili.db"), Pooling = false }.ToString());
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=FULL; CREATE TABLE IF NOT EXISTS records (id TEXT PRIMARY KEY, json TEXT NOT NULL);";
        cmd.ExecuteNonQuery();
    }
    public AppData Load()
    {
        var data = new AppData();
        using var cmd = connection.CreateCommand(); cmd.CommandText = "SELECT id,json FROM records";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var id = reader.GetString(0); var json = reader.GetString(1); saved[id] = json;
            if (id == "settings") data.Settings = JsonSerializer.Deserialize<Preferences>(json)!;
            else if (id == "timer") data.Timer = JsonSerializer.Deserialize<TimerSnapshot>(json)!;
            else if (id.StartsWith("task:")) data.Tasks.Add(JsonSerializer.Deserialize<FocusTask>(json)!);
            else if (id.StartsWith("slice:")) data.Slices.Add(JsonSerializer.Deserialize<FocusSlice>(json)!);
            else if (id.StartsWith("session:")) data.Sessions.Add(JsonSerializer.Deserialize<FocusSession>(json)!);
        }
        data.Tasks = data.Tasks.OrderBy(t => t.CreatedAt).ToList();
        return data;
    }
    // Snapshot on the UI thread; all disk work runs on a worker. Only changed rows are written.
    public Task SaveAsync(AppData data)
    {
        var rows = new Dictionary<string, string> { ["settings"] = JsonSerializer.Serialize(data.Settings), ["timer"] = JsonSerializer.Serialize(data.Timer) };
        foreach (var t in data.Tasks) rows["task:" + t.Id] = JsonSerializer.Serialize(t);
        foreach (var s in data.Sessions) rows["session:" + s.Id] = JsonSerializer.Serialize(s);
        foreach (var s in data.Slices) rows["slice:" + s.SessionId + ":" + s.Day] = JsonSerializer.Serialize(s);
        return WriteAsync(rows);
    }
    async Task WriteAsync(Dictionary<string, string> rows)
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await Task.Run(() =>
            {
                using var transaction = connection.BeginTransaction();
                foreach (var (id, json) in rows)
                {
                    if (saved.TryGetValue(id, out var old) && old == json) continue;
                    using var cmd = connection.CreateCommand(); cmd.Transaction = transaction;
                    cmd.CommandText = "INSERT INTO records(id,json) VALUES($id,$json) ON CONFLICT(id) DO UPDATE SET json=$json";
                    cmd.Parameters.AddWithValue("$id", id); cmd.Parameters.AddWithValue("$json", json); cmd.ExecuteNonQuery();
                }
                foreach (var id in saved.Keys.Except(rows.Keys).ToList())
                {
                    using var cmd = connection.CreateCommand(); cmd.Transaction = transaction;
                    cmd.CommandText = "DELETE FROM records WHERE id=$id"; cmd.Parameters.AddWithValue("$id", id); cmd.ExecuteNonQuery();
                }
                transaction.Commit(); saved.Clear(); foreach (var row in rows) saved.Add(row.Key, row.Value);
            }).ConfigureAwait(false);
        }
        finally { gate.Release(); }
    }
    public static AppData ReadBackup(string path)
    {
        if (new FileInfo(path).Length > 32 * 1024 * 1024) throw new InvalidDataException("备份文件过大。");
        var data = JsonSerializer.Deserialize<AppData>(File.ReadAllText(path)) ?? throw new InvalidDataException("无法读取备份。");
        if (data.SchemaVersion != 1 || data.Settings == null || data.Timer == null || data.Tasks == null || data.Sessions == null || data.Slices == null)
            throw new InvalidDataException("备份格式不受支持。");
        if (data.Tasks.Any(t => string.IsNullOrWhiteSpace(t.Id) || string.IsNullOrWhiteSpace(t.Title)) || data.Tasks.Select(t => t.Id).Distinct().Count() != data.Tasks.Count ||
            data.Slices.Any(s => !double.IsFinite(s.Seconds) || s.Seconds < 0 || !DateOnly.TryParseExact(s.Day, "yyyy-MM-dd", out _)) ||
            !double.IsFinite(data.Timer.RemainingSeconds) || !double.IsFinite(data.Timer.DurationSeconds) || data.Timer.DurationSeconds <= 0 || data.Timer.DurationSeconds > 10800 || data.Timer.RemainingSeconds < 0 || data.Timer.RemainingSeconds > data.Timer.DurationSeconds ||
            !Enum.IsDefined(data.Timer.Status) || !Enum.IsDefined(data.Timer.Phase)) throw new InvalidDataException("备份包含无效数据。");
        data.Settings.Validate(); return data;
    }
    public void Dispose() { connection.Dispose(); gate.Dispose(); }
}
