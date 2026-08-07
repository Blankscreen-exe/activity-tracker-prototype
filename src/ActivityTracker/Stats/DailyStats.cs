using ActivityTracker.Models;

namespace ActivityTracker.Stats;

public class DailyStats
{
    public required DateTime Date { get; init; }
    public required TimeSpan CodingTime { get; init; }
    public required TimeSpan BrowsingTime { get; init; }
    public required TimeSpan TotalTrackedTime { get; init; }
    public required TimeSpan IdleTime { get; init; }
    public required int ContextSwitches { get; init; }
    public required TimeSpan AverageFocusSession { get; init; }
    public required TimeSpan LongestFocusSession { get; init; }
    public required IReadOnlyList<(string Domain, TimeSpan Time)> TopWebsites { get; init; }
    public required IReadOnlyList<(string Domain, int Visits)> MostDistractingWebsites { get; init; }
    public required IReadOnlyList<(string Process, string WindowTitle, TimeSpan Time)> WindowUsage { get; init; }
    public required (string Process, TimeSpan Time)? TopApp { get; init; }
    public required IReadOnlyList<Session> Sessions { get; init; }
}
