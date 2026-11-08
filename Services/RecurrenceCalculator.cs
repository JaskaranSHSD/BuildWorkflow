using BuildWorkflow.Models;
using System;

namespace BuildWorkflow.Services;

public static class RecurrenceCalculator
{
    public static DateTimeOffset? GetNextRun(WorkflowSchedule s, DateTimeOffset after, bool includeAfter = false)
    {
        if (!s.Enabled) return null;
        if (s.EndAt.HasValue && after >= s.EndAt.Value) return null;

        DateTimeOffset? candidate = s.ScheduleType switch
        {
            "Once" => s.RunAt.HasValue && s.RunAt.Value > after ? s.RunAt.Value : (includeAfter && s.RunAt.HasValue && s.RunAt.Value == after ? s.RunAt.Value : null),
            "Interval" => after.AddMinutes(Math.Max(1, s.IntervalMinutes ?? 1)),
            "Daily" => NextDaily(s, after, includeAfter),
            "Weekly" => NextWeekly(s, after, includeAfter),
            "MonthlyDay" => NextMonthlyDay(s, after, includeAfter),
            "MonthlyNth" => NextMonthlyNth(s, after, includeAfter),
            "MonthlyLastDay" => NextMonthlyLastDay(s, after, includeAfter),
            _ => s.NextRunAt // fall back to stored
        };

        if (candidate.HasValue && s.EndAt.HasValue && candidate.Value > s.EndAt.Value)
            return null;
        return candidate;
    }

    private static TimeZoneInfo Tz(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return TimeZoneInfo.Utc;
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch { return TimeZoneInfo.Utc; }
    }

    private static int HourOf(WorkflowSchedule s) =>
        s.Hour ?? (s.RunAt.HasValue ? s.RunAt.Value.Hour : 9);

    private static int MinuteOf(WorkflowSchedule s) =>
        s.Minute ?? (s.RunAt.HasValue ? s.RunAt.Value.Minute : 0);

    private static DateTimeOffset ToUtc(DateTime local, TimeZoneInfo tz) =>
        new DateTimeOffset(local, tz.GetUtcOffset(local)).ToUniversalTime();

    private static DateTimeOffset? NextDaily(WorkflowSchedule s, DateTimeOffset after, bool includeAfter)
    {
        var tz = Tz(s.TimeZoneId);
        var localAfter = TimeZoneInfo.ConvertTime(after, tz);
        var hour = HourOf(s);
        var minute = MinuteOf(s);
        var date = localAfter.Date;
        for (int d = 0; d <= 1; d++)
        {
            var candDate = date.AddDays(d);
            var local = new DateTime(candDate.Year, candDate.Month, candDate.Day, hour, minute, 0);
            if (!includeAfter && local <= localAfter) continue;
            if (includeAfter && local < localAfter) continue;
            return ToUtc(local, tz);
        }
        return null;
    }

    private static DateTimeOffset? NextWeekly(WorkflowSchedule s, DateTimeOffset after, bool includeAfter)
    {
        var tz = Tz(s.TimeZoneId);
        var localAfter = TimeZoneInfo.ConvertTime(after, tz);
        var hour = HourOf(s);
        var minute = MinuteOf(s);
        var selected = ParseDays(s.DaysOfWeek);
        if (selected.Count == 0)
        {
            if (s.DayOfWeek.HasValue) selected.Add((DayOfWeek)s.DayOfWeek.Value);
            else selected.Add(localAfter.DayOfWeek);
        }
        var date = localAfter.Date;
        for (int d = 0; d <= 7; d++)
        {
            var candDate = date.AddDays(d);
            if (!selected.Contains(candDate.DayOfWeek)) continue;
            var local = new DateTime(candDate.Year, candDate.Month, candDate.Day, hour, minute, 0);
            if (!includeAfter && local <= localAfter) continue;
            if (includeAfter && local < localAfter) continue;
            return ToUtc(local, tz);
        }
        return null;
    }

    private static DateTimeOffset? NextMonthlyDay(WorkflowSchedule s, DateTimeOffset after, bool includeAfter)
    {
        var tz = Tz(s.TimeZoneId);
        var localAfter = TimeZoneInfo.ConvertTime(after, tz);
        var hour = HourOf(s);
        var minute = MinuteOf(s);
        var dom = Math.Clamp(s.DayOfMonth ?? (s.RunAt?.Day ?? 1), 1, 31);
        for (int m = 0; m <= 12; m++)
        {
            var ym = localAfter.AddMonths(m);
            var year = ym.Year;
            var month = ym.Month;
            var day = Math.Min(dom, DateTime.DaysInMonth(year, month));
            var local = new DateTime(year, month, day, hour, minute, 0);
            if (m == 0 && !includeAfter && local <= localAfter) continue;
            if (m == 0 && includeAfter && local < localAfter) continue;
            return ToUtc(local, tz);
        }
        return null;
    }

    private static DateTimeOffset? NextMonthlyNth(WorkflowSchedule s, DateTimeOffset after, bool includeAfter)
    {
        var tz = Tz(s.TimeZoneId);
        var localAfter = TimeZoneInfo.ConvertTime(after, tz);
        var hour = HourOf(s);
        var minute = MinuteOf(s);
        var targetDow = s.DayOfWeek.HasValue ? (DayOfWeek)s.DayOfWeek.Value : (s.RunAt?.DayOfWeek ?? DayOfWeek.Monday);
        var week = s.WeekOfMonth ?? 1; // 1-4 or 5 for last
        for (int m = 0; m <= 12; m++)
        {
            var ym = localAfter.AddMonths(m);
            var year = ym.Year;
            var month = ym.Month;
            DateTime local;
            if (week >= 5)
            {
                // last occurrence in month
                var lastDay = DateTime.DaysInMonth(year, month);
                var d = new DateTime(year, month, lastDay);
                while (d.DayOfWeek != targetDow) d = d.AddDays(-1);
                local = new DateTime(d.Year, d.Month, d.Day, hour, minute, 0);
            }
            else
            {
                var first = new DateTime(year, month, 1);
                while (first.DayOfWeek != targetDow) first = first.AddDays(1);
                var nth = first.AddDays(7 * (week - 1));
                if (nth.Month != month) return null;
                local = new DateTime(nth.Year, nth.Month, nth.Day, hour, minute, 0);
            }
            if (m == 0 && !includeAfter && local <= localAfter) continue;
            if (m == 0 && includeAfter && local < localAfter) continue;
            return ToUtc(local, tz);
        }
        return null;
    }

    private static DateTimeOffset? NextMonthlyLastDay(WorkflowSchedule s, DateTimeOffset after, bool includeAfter)
    {
        var tz = Tz(s.TimeZoneId);
        var localAfter = TimeZoneInfo.ConvertTime(after, tz);
        var hour = HourOf(s);
        var minute = MinuteOf(s);
        for (int m = 0; m <= 12; m++)
        {
            var ym = localAfter.AddMonths(m);
            var year = ym.Year;
            var month = ym.Month;
            var day = DateTime.DaysInMonth(year, month);
            var local = new DateTime(year, month, day, hour, minute, 0);
            if (m == 0 && !includeAfter && local <= localAfter) continue;
            if (m == 0 && includeAfter && local < localAfter) continue;
            return ToUtc(local, tz);
        }
        return null;
    }

    private static HashSet<DayOfWeek> ParseDays(string? csv)
    {
        var set = new HashSet<DayOfWeek>();
        if (string.IsNullOrWhiteSpace(csv)) return set;
        foreach (var part in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (int.TryParse(part, out var n) && n >= 0 && n <= 6) set.Add((DayOfWeek)n);
        }
        return set;
    }
}
