using System.Globalization;

namespace XiliPomodoro.Core;

public sealed record FocusDay(DateTime Date, double Seconds);

public static class FocusHistory
{
    public static IReadOnlyList<FocusDay> Month(IEnumerable<FocusSlice> slices, DateTime month, DateTime today)
    {
        var first = new DateTime(month.Year, month.Month, 1);
        var last = first.AddMonths(1).AddDays(-1);
        if (last > today.Date) last = today.Date;
        if (first > last) return [];
        var totals = slices.Where(s => double.IsFinite(s.Seconds) && s.Seconds > 0)
            .GroupBy(s => s.Day).ToDictionary(g => g.Key, g => g.Sum(s => s.Seconds));
        return Enumerable.Range(0, (last - first).Days + 1).Select(i => first.AddDays(i))
            .Select(date => new FocusDay(date, totals.GetValueOrDefault(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)))).ToArray();
    }
}
