namespace ActivityTracker.Models;

public class Session
{
    public int Id { get; set; }
    public DateTime Start { get; set; }
    public DateTime? End { get; set; }
    public TimeSpan? Duration { get; set; }
    public string Process { get; set; } = string.Empty;
    public string WindowTitle { get; set; } = string.Empty;
    public string? TabTitle { get; set; }
    public string? Url { get; set; }
    public string? Domain { get; set; }
    public int? MemoId { get; set; }
    public Memo? Memo { get; set; }
}
