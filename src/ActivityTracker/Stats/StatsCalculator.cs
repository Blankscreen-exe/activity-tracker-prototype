using ActivityTracker.Config;
using ActivityTracker.Data;
using ActivityTracker.Models;
using ActivityTracker.Native;

namespace ActivityTracker.Stats;

public static class StatsCalculator
{
    public static DailyStats Calculate(DateTime date)
    {
        using var db = new AppDbContext();

        // Session.Start/End are stored in UTC, but "date" is the user's local
        // calendar day - convert the day boundary to UTC before querying.
        // DateTime.ToUniversalTime() treats an Unspecified-kind value (e.g. a
        // CLI-parsed date) as local, which is exactly what we want here.
        var dayStart = date.Date.ToUniversalTime();
        var dayEnd = dayStart.AddDays(1);

        var sessions = db.Sessions
            .Where(s => s.Start >= dayStart && s.Start < dayEnd && s.Duration != null)
            .OrderBy(s => s.Start)
            .ToList();

        var codingTime = Sum(sessions.Where(s =>
            AppSettings.Current.CodingProcessNames.Contains(s.Process, StringComparer.OrdinalIgnoreCase)));
        var browsingTime = Sum(sessions.Where(s => BrowserTabReader.IsBrowserProcess(s.Process)));
        var trackedTime = Sum(sessions);

        var durations = sessions.Select(s => s.Duration!.Value).ToList();

        var idleTime = TimeSpan.Zero;
        if (sessions.Count > 0)
        {
            var nowUtc = DateTime.UtcNow;
            var spanEnd = dayEnd < nowUtc ? sessions[^1].End!.Value : nowUtc;
            var totalSpan = spanEnd - sessions[0].Start;
            idleTime = totalSpan - trackedTime;
            if (idleTime < TimeSpan.Zero)
            {
                idleTime = TimeSpan.Zero;
            }
        }

        var topWebsites = sessions
            .Where(s => s.Domain != null)
            .GroupBy(s => s.Domain!)
            .Select(g => (Domain: g.Key, Time: Sum(g)))
            .OrderByDescending(x => x.Time)
            .Take(5)
            .ToList();

        var mostDistracting = sessions
            .Where(s => s.Domain != null)
            .GroupBy(s => s.Domain!)
            .Select(g => (Domain: g.Key, Visits: g.Count()))
            .OrderByDescending(x => x.Visits)
            .Take(5)
            .ToList();

        var windowUsage = sessions
            .GroupBy(s => (s.Process, s.WindowTitle))
            .Select(g => (Process: g.Key.Process, WindowTitle: g.Key.WindowTitle, Time: Sum(g)))
            .OrderByDescending(x => x.Time)
            .ToList();

        // Same idea as windowUsage but grouped by process alone, ignoring
        // which window/tab - "what app did I use the most" rather than
        // "what specific window/tab did I use the most".
        var topApp = sessions.Count > 0
            ? sessions
                .GroupBy(s => s.Process)
                .Select(g => (Process: g.Key, Time: Sum(g)))
                .OrderByDescending(x => x.Time)
                .Select(x => ((string Process, TimeSpan Time)?)x)
                .First()
            : null;

        return new DailyStats
        {
            Date = date.Date,
            CodingTime = codingTime,
            BrowsingTime = browsingTime,
            TotalTrackedTime = trackedTime,
            IdleTime = idleTime,
            ContextSwitches = sessions.Count,
            AverageFocusSession = durations.Count > 0
                ? TimeSpan.FromSeconds(durations.Average(d => d.TotalSeconds))
                : TimeSpan.Zero,
            LongestFocusSession = durations.Count > 0 ? durations.Max() : TimeSpan.Zero,
            TopWebsites = topWebsites,
            MostDistractingWebsites = mostDistracting,
            WindowUsage = windowUsage,
            TopApp = topApp,
            Sessions = sessions
        };
    }

    // Total tracked time per hour-of-day (0-23, local time), summed across
    // every day of history - "what hours am I usually active".
    public static TimeSpan[] GetHourlyBreakdown()
    {
        using var db = new AppDbContext();

        var sessions = db.Sessions
            .Where(s => s.Duration != null)
            .Select(s => new { s.Start, s.Duration })
            .ToList();

        var buckets = new TimeSpan[24];

        foreach (var session in sessions)
        {
            var hour = session.Start.ToLocalTime().Hour;
            buckets[hour] += session.Duration ?? TimeSpan.Zero;
        }

        return buckets;
    }

    // Coding/browsing/other time for each of the last 7 days (oldest first).
    public static List<(DateTime Date, TimeSpan Coding, TimeSpan Browsing, TimeSpan Other)> GetWeeklyUsage()
    {
        var result = new List<(DateTime Date, TimeSpan Coding, TimeSpan Browsing, TimeSpan Other)>();

        for (var i = 6; i >= 0; i--)
        {
            var date = DateTime.Today.AddDays(-i);
            var stats = Calculate(date);

            var other = stats.TotalTrackedTime - stats.CodingTime - stats.BrowsingTime;
            if (other < TimeSpan.Zero)
            {
                other = TimeSpan.Zero;
            }

            result.Add((date, stats.CodingTime, stats.BrowsingTime, other));
        }

        return result;
    }

    private static TimeSpan Sum(IEnumerable<Session> sessions)
    {
        var total = TimeSpan.Zero;
        foreach (var session in sessions)
        {
            total += session.Duration ?? TimeSpan.Zero;
        }

        return total;
    }
}
