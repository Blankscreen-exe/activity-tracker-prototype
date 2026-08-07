namespace ActivityTracker.Stats;

public static class SummaryFormatter
{
    public static string Format(DailyStats stats)
    {
        var lines = new List<string>
        {
            $"Activity Summary for {stats.Date:yyyy-MM-dd}",
            new string('-', 40),
            $"Coding time:        {FormatSpan(stats.CodingTime)}",
            $"Browsing time:      {FormatSpan(stats.BrowsingTime)}",
            $"Idle time:          {FormatSpan(stats.IdleTime)}",
            $"Context switches:   {stats.ContextSwitches}",
            $"Avg focus session:  {FormatSpan(stats.AverageFocusSession)}",
            $"Longest focus:      {FormatSpan(stats.LongestFocusSession)}",
            string.Empty,
            "Top websites (by time):"
        };

        AppendRows(lines, stats.TopWebsites, item => $"  {item.Domain,-30} {FormatSpan(item.Time)}");

        lines.Add(string.Empty);
        lines.Add("Most distracting (by switches):");

        AppendRows(lines, stats.MostDistractingWebsites, item => $"  {item.Domain,-30} {item.Visits} visits");

        lines.Add(string.Empty);
        lines.Add("Time per window:");

        AppendRows(lines, stats.WindowUsage, item => $"  {Truncate($"{item.Process} - {item.WindowTitle}", 60),-60} {FormatSpan(item.Time)}");

        lines.Add(string.Empty);
        lines.Add("Window switch log:");

        AppendRows(lines, stats.Sessions, item =>
            $"  {FormatClock(item.Start)} - {FormatClock(item.End)}  {Truncate($"{item.Process} - {item.WindowTitle}", 55),-55} {FormatSpan(item.Duration ?? TimeSpan.Zero)}");

        return string.Join(Environment.NewLine, lines);
    }

    private static string Truncate(string text, int maxLength)
    {
        return text.Length <= maxLength ? text : text[..(maxLength - 1)] + "…";
    }

    // Session.Start/End are stored in UTC; convert to the machine's local
    // timezone and use a 12-hour clock, since that's what's readable to a person.
    private static string FormatClock(DateTime? utc) =>
        utc?.ToLocalTime().ToString("h:mm:ss tt") ?? "-";

    private static void AppendRows<T>(List<string> lines, IReadOnlyList<T> items, Func<T, string> formatRow)
    {
        if (items.Count == 0)
        {
            lines.Add("  (none)");
            return;
        }

        foreach (var item in items)
        {
            lines.Add(formatRow(item));
        }
    }

    private static string FormatSpan(TimeSpan span) => span.ToString(@"hh\:mm\:ss");
}
