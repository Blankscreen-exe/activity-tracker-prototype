using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using ActivityTracker.Config;
using ActivityTracker.Data;
using ActivityTracker.Logging;
using ActivityTracker.Models;
using ActivityTracker.Stats;
using ActivityTracker.Tracking;
using LiveChartsCore;
using LiveChartsCore.Kernel;
using LiveChartsCore.SkiaSharpView;
using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;

namespace ActivityTracker;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private readonly TrackingService _trackingService = new();
    private readonly DispatcherTimer _refreshTimer;
    private bool _isTracking;
    private string _currentActivityDisplay = "-";
    private DateTime? _currentSessionStartUtc;
    private List<Session> _timelineSessions = new();
    private DateTime _timelineDate = DateTime.Today;

    public MainWindow()
    {
        InitializeComponent();

        _trackingService.SessionStarted += OnSessionStarted;

        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(10)
        };
        _refreshTimer.Tick += (_, _) =>
        {
            RefreshTrackerTab();
            ConfigureSummaryDatePicker();
            RefreshSummaryTab();
        };

        Closing += (_, _) => StopTracking();

        TimelineSortOrderBox.SelectedIndex = 0;

        LoadSettingsIntoForm();
        ApplyWallpaper();
        ConfigureSummaryDatePicker();
        ConfigureTimelineDatePicker();
        RefreshMemoPickers();
        RefreshTimelineTab();
        RefreshTrendsTab();
        StartTracking();
    }

    private void ToggleButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isTracking)
        {
            StopTracking();
        }
        else
        {
            StartTracking();
        }
    }

    private void StartTracking()
    {
        _trackingService.Start();
        _refreshTimer.Start();
        _isTracking = true;
        ToggleButton.Content = "Stop Tracking";
        RefreshTrackerTab();
    }

    private void StopTracking()
    {
        if (!_isTracking)
        {
            return;
        }

        _trackingService.Stop();
        _refreshTimer.Stop();
        _isTracking = false;
        _currentActivityDisplay = "-";
        _currentSessionStartUtc = null;
        ToggleButton.Content = "Start Tracking";
        RefreshTrackerTab();
    }

    private void OnSessionStarted(Session session)
    {
        _currentActivityDisplay = $"{session.Process} - {session.WindowTitle}";
        _currentSessionStartUtc = session.Start;
        RefreshTrackerTab();
    }

    // Tracker tab always shows "today", live.
    private void RefreshTrackerTab()
    {
        var stats = StatsCalculator.Calculate(DateTime.Today);

        var currentSessionDuration = _currentSessionStartUtc.HasValue
            ? (DateTime.UtcNow - _currentSessionStartUtc.Value).ToString(@"hh\:mm\:ss")
            : "-";

        StatusList.ItemsSource = new List<KeyValueRow>
        {
            new() { Label = "Status", Value = _isTracking ? "Tracking" : "Stopped" },
            new() { Label = "Currently Tracking", Value = _currentActivityDisplay },
            new() { Label = "Current Session Duration", Value = currentSessionDuration },
            new() { Label = "Current Memo", Value = AppSettings.Current.ActiveMemoName ?? "(none)" }
        };

        var topWebsite = stats.TopWebsites.Count > 0
            ? $"{stats.TopWebsites[0].Domain} ({stats.TopWebsites[0].Time:hh\\:mm\\:ss})"
            : "-";

        // "Top app" ignores which window/tab (grouped by process only); "top
        // task" treats each browser tab and each app window as its own thing
        // (same Process+WindowTitle grouping the Summary tab uses).
        var topApp = stats.TopApp.HasValue
            ? $"{stats.TopApp.Value.Process} ({stats.TopApp.Value.Time:hh\\:mm\\:ss})"
            : "-";

        var topTask = stats.WindowUsage.Count > 0
            ? $"{stats.WindowUsage[0].Process} - {stats.WindowUsage[0].WindowTitle} ({stats.WindowUsage[0].Time:hh\\:mm\\:ss})"
            : "-";

        var trackingSince = stats.Sessions.Count > 0
            ? stats.Sessions.Min(s => s.Start).ToLocalTime().ToString("h:mm:ss tt")
            : "-";

        TodayStatsList.ItemsSource = new List<KeyValueRow>
        {
            new() { Label = "Coding time", Value = stats.CodingTime.ToString(@"hh\:mm\:ss") },
            new() { Label = "Browsing time", Value = stats.BrowsingTime.ToString(@"hh\:mm\:ss") },
            new() { Label = "Total tracked time", Value = stats.TotalTrackedTime.ToString(@"hh\:mm\:ss") },
            new() { Label = "Idle time", Value = stats.IdleTime.ToString(@"hh\:mm\:ss") },
            new() { Label = "Context switches", Value = stats.ContextSwitches.ToString() },
            new() { Label = "Longest focus session", Value = stats.LongestFocusSession.ToString(@"hh\:mm\:ss") },
            new() { Label = "Average focus session", Value = stats.AverageFocusSession.ToString(@"hh\:mm\:ss") },
            new() { Label = "Top website", Value = topWebsite },
            new() { Label = "Top app", Value = topApp },
            new() { Label = "Top task", Value = topTask },
            new() { Label = "Tracking since", Value = trackingSince }
        };

        var other = stats.TotalTrackedTime - stats.CodingTime - stats.BrowsingTime;
        if (other < TimeSpan.Zero)
        {
            other = TimeSpan.Zero;
        }

        TodayPieChart.Series = new ISeries[]
        {
            PieSlice("Coding", stats.CodingTime),
            PieSlice("Browsing", stats.BrowsingTime),
            PieSlice("Other", other),
            PieSlice("Idle", stats.IdleTime)
        };
    }

    // A pie slice whose tooltip shows a rounded, human-readable duration
    // ("1h 23m") instead of the raw decimal seconds LiveCharts2 shows by
    // default. The tooltip already prefixes this with a marker + the
    // series Name on its own, so the formatter must NOT repeat the name -
    // doing that was producing "Coding: Coding: 1h 23m"-style duplication.
    private static PieSeries<double> PieSlice(string name, TimeSpan value)
    {
        return new PieSeries<double>
        {
            Values = new[] { value.TotalSeconds },
            Name = name,
            ToolTipLabelFormatter = point => FormatDuration(TimeSpan.FromSeconds(point.Coordinate.PrimaryValue))
        };
    }

    private static string FormatDuration(TimeSpan span)
    {
        if (span.TotalHours >= 1)
        {
            return $"{(int)span.TotalHours}h {span.Minutes}m";
        }

        if (span.TotalMinutes >= 1)
        {
            return $"{(int)span.TotalMinutes}m {span.Seconds}s";
        }

        return $"{(int)span.TotalSeconds}s";
    }

    // Summary tab shows whichever date is selected in the date picker.
    private void RefreshSummaryTab()
    {
        var date = SummaryDatePicker.SelectedDate ?? DateTime.Today;
        var stats = StatsCalculator.Calculate(date);

        WindowUsageHeaderText.Text = $"Time per window ({date:yyyy-MM-dd})";
        SwitchLogHeaderText.Text = $"Window switch log ({date:yyyy-MM-dd})";

        WindowUsageList.ItemsSource = stats.WindowUsage
            .Select(w => new WindowUsageRow
            {
                Process = w.Process,
                WindowTitle = w.WindowTitle,
                TimeDisplay = w.Time.ToString(@"hh\:mm\:ss")
            })
            .ToList();

        SwitchLogList.ItemsSource = stats.Sessions
            .OrderByDescending(s => s.Start)
            .Select(s => new SwitchLogRow
            {
                // Sessions are stored in UTC; show them in the machine's local time, 12-hour clock.
                StartDisplay = s.Start.ToLocalTime().ToString("h:mm:ss tt"),
                EndDisplay = s.End?.ToLocalTime().ToString("h:mm:ss tt") ?? "-",
                Process = s.Process,
                WindowTitle = s.WindowTitle,
                DurationDisplay = (s.Duration ?? TimeSpan.Zero).ToString(@"hh\:mm\:ss")
            })
            .ToList();

        var topWindows = stats.WindowUsage.Take(10).Reverse().ToList();

        // The tooltip already shows a marker + Series.Name on the value line,
        // so the two formatters below must stay complementary, not
        // duplicates: X (secondary/category) shows which window this bar is;
        // Y (primary/value) shows only the duration, since "Time" gets
        // prefixed onto it automatically.
        WindowUsageChart.Series = new ISeries[]
        {
            new RowSeries<double>
            {
                Values = topWindows.Select(w => w.Time.TotalMinutes).ToArray(),
                Name = "Time",
                XToolTipLabelFormatter = point => $"{topWindows[point.Index].Process} - {topWindows[point.Index].WindowTitle}",
                YToolTipLabelFormatter = point => FormatDuration(TimeSpan.FromMinutes(point.Coordinate.PrimaryValue))
            }
        };

        WindowUsageChart.YAxes = new[]
        {
            new Axis
            {
                // Keep labels short and small - the full Process/Window Title
                // pair is still visible in the table below; the chart is just
                // for a quick visual scan, and long labels were eating most
                // of the chart's width, squeezing the bars and their value
                // labels into an overlapping mess.
                Labels = topWindows.Select(w => Truncate(w.WindowTitle, 18)).ToArray(),
                TextSize = 11
            }
        };

        WindowUsageChart.XAxes = new[]
        {
            new Axis { TextSize = 11 }
        };

        TopWebsitesChart.Series = stats.TopWebsites
            .Select(w => PieSlice(w.Domain, w.Time))
            .ToArray();
    }

    private static string Truncate(string text, int maxLength)
    {
        return text.Length <= maxLength ? text : text[..(maxLength - 1)] + "…";
    }

    private void SummaryDatePicker_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
    {
        RefreshSummaryTab();
    }

    private void ConfigureSummaryDatePicker()
    {
        ConfigureDatePicker(SummaryDatePicker);
    }

    private void RefreshSummaryButton_Click(object sender, RoutedEventArgs e)
    {
        ConfigureSummaryDatePicker();
        RefreshSummaryTab();
    }

    // LiveCharts2 controls only get a real layout/paint pass while their tab
    // is actually visible, so a chart whose data was refreshed by the 10s
    // timer while a different tab was showing can end up not repainting.
    // Force a refresh the moment each tab becomes visible to guarantee it.
    private void MainTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.Source != MainTabControl)
        {
            return;
        }

        if (MainTabControl.SelectedItem == TrackerTabItem)
        {
            RefreshTrackerTab();
        }
        else if (MainTabControl.SelectedItem == SummaryTabItem)
        {
            ConfigureSummaryDatePicker();
            RefreshSummaryTab();
        }
        else if (MainTabControl.SelectedItem == TimelineTabItem)
        {
            ConfigureTimelineDatePicker();
            RefreshMemoPickers();
            RefreshTimelineTab();
        }
        else if (MainTabControl.SelectedItem == TrendsTabItem)
        {
            RefreshTrendsTab();
        }
    }

    private void RefreshTrendsButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshTrendsTab();
    }

    private void RefreshTrendsTab()
    {
        var hourly = StatsCalculator.GetHourlyBreakdown();

        HourlyChart.Series = new ISeries[]
        {
            new ColumnSeries<double>
            {
                Values = hourly.Select(h => h.TotalMinutes).ToArray(),
                Name = "Activity",
                XToolTipLabelFormatter = point => $"{point.Index:00}:00 - {(point.Index + 1) % 24:00}:00",
                YToolTipLabelFormatter = point => FormatDuration(TimeSpan.FromMinutes(point.Coordinate.PrimaryValue))
            }
        };

        HourlyChart.XAxes = new[]
        {
            new Axis
            {
                Labels = Enumerable.Range(0, 24).Select(h => h.ToString("00")).ToArray()
            }
        };

        var weekly = StatsCalculator.GetWeeklyUsage();

        string WeeklyDayLabel(ChartPoint point) => weekly[point.Index].Date.ToString("ddd MM/dd");
        string WeeklyDuration(ChartPoint point) => FormatDuration(TimeSpan.FromMinutes(point.Coordinate.PrimaryValue));

        WeeklyChart.Series = new ISeries[]
        {
            new StackedColumnSeries<double>
            {
                Values = weekly.Select(w => w.Coding.TotalMinutes).ToArray(),
                Name = "Coding",
                XToolTipLabelFormatter = WeeklyDayLabel,
                YToolTipLabelFormatter = WeeklyDuration
            },
            new StackedColumnSeries<double>
            {
                Values = weekly.Select(w => w.Browsing.TotalMinutes).ToArray(),
                Name = "Browsing",
                XToolTipLabelFormatter = WeeklyDayLabel,
                YToolTipLabelFormatter = WeeklyDuration
            },
            new StackedColumnSeries<double>
            {
                Values = weekly.Select(w => w.Other.TotalMinutes).ToArray(),
                Name = "Other",
                XToolTipLabelFormatter = WeeklyDayLabel,
                YToolTipLabelFormatter = WeeklyDuration
            }
        };

        WeeklyChart.XAxes = new[]
        {
            new Axis
            {
                Labels = weekly.Select(w => w.Date.ToString("ddd MM/dd")).ToArray()
            }
        };
    }

    private void OpenLogsFolderButton_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(JsonlSessionLogger.LogsDirectory);
        Process.Start(new ProcessStartInfo(JsonlSessionLogger.LogsDirectory) { UseShellExecute = true });
    }

    private void OpenTodayLogButton_Click(object sender, RoutedEventArgs e)
    {
        var date = SummaryDatePicker.SelectedDate ?? DateTime.Today;
        var path = JsonlSessionLogger.GetLogFilePath(date);

        if (!File.Exists(path))
        {
            MessageBox.Show(this, $"No log file for {date:yyyy-MM-dd} yet.", "Activity Tracker", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        Process.Start(new ProcessStartInfo("notepad.exe", $"\"{path}\"") { UseShellExecute = true });
    }

    private void LoadSettingsIntoForm()
    {
        var settings = AppSettings.Current;

        IdlePollIntervalBox.Text = settings.IdlePollIntervalSeconds.ToString();
        IdleThresholdBox.Text = settings.IdleThresholdSeconds.ToString();
        CodingProcessNamesBox.Text = string.Join(Environment.NewLine, settings.CodingProcessNames);
        BrowserProcessNamesBox.Text = string.Join(Environment.NewLine, settings.BrowserProcessNames);
        WallpaperPathBox.Text = settings.WallpaperPath ?? string.Empty;
        CurrentMemoBox.Text = settings.ActiveMemoName ?? string.Empty;
    }

    private void RefreshMemoPickers()
    {
        var names = MemoRepository.GetAllNames();
        CurrentMemoBox.ItemsSource = names;
        TimelineMemoBox.ItemsSource = names;
    }

    private void SetCurrentMemoButton_Click(object sender, RoutedEventArgs e)
    {
        var name = (CurrentMemoBox.Text ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            AppSettings.Current.ActiveMemoName = null;
        }
        else
        {
            using var db = new AppDbContext();
            MemoRepository.ResolveOrCreate(db, name);
            AppSettings.Current.ActiveMemoName = name;
        }

        AppSettings.Save();
        RefreshMemoPickers();
        RefreshTrackerTab();
    }

    private void ClearCurrentMemoButton_Click(object sender, RoutedEventArgs e)
    {
        AppSettings.Current.ActiveMemoName = null;
        CurrentMemoBox.Text = string.Empty;
        AppSettings.Save();
        RefreshTrackerTab();
    }

    // Timeline tab shows whichever date is selected, chronological, with
    // multi-select bulk memo tagging. Deliberately NOT part of the 10s
    // ambient refresh timer - resetting the list would drop the user's
    // in-progress selection, which is exactly the friction we want to avoid.
    private void RefreshTimelineTab()
    {
        var date = TimelineDatePicker.SelectedDate ?? DateTime.Today;
        _timelineDate = date;

        var dayStart = date.Date.ToUniversalTime();
        var dayEnd = dayStart.AddDays(1);

        var descending = (TimelineSortOrderBox.SelectedItem as ComboBoxItem)?.Content as string == "Latest first";

        using (var db = new AppDbContext())
        {
            var query = db.Sessions
                .Include(s => s.Memo)
                .Where(s => s.Start >= dayStart && s.Start < dayEnd && s.Duration != null);

            _timelineSessions = descending
                ? query.OrderByDescending(s => s.Start).ToList()
                : query.OrderBy(s => s.Start).ToList();
        }

        TimelineList.ItemsSource = _timelineSessions
            .Select(s => new TimelineRow
            {
                SessionId = s.Id,
                StartDisplay = s.Start.ToLocalTime().ToString("h:mm:ss tt"),
                EndDisplay = s.End?.ToLocalTime().ToString("h:mm:ss tt") ?? "-",
                DurationDisplay = (s.Duration ?? TimeSpan.Zero).ToString(@"hh\:mm\:ss"),
                Process = s.Process,
                WindowTitle = s.WindowTitle,
                MemoDisplay = s.Memo?.Name ?? "-"
            })
            .ToList();

        DeleteTimelineButton.IsEnabled = false;

        RefreshTimelineStrip();
    }

    private void TimelineList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        DeleteTimelineButton.IsEnabled = TimelineList.SelectedItems.Count > 0;
    }

    private void DeleteTimelineButton_Click(object sender, RoutedEventArgs e)
    {
        var selectedIds = TimelineList.SelectedItems.Cast<TimelineRow>().Select(r => r.SessionId).ToList();
        if (selectedIds.Count == 0)
        {
            return;
        }

        var confirm = MessageBox.Show(
            this,
            $"Delete {selectedIds.Count} selected entr{(selectedIds.Count == 1 ? "y" : "ies")}? This cannot be undone.",
            "Activity Tracker",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        using (var db = new AppDbContext())
        {
            var sessions = db.Sessions.Where(s => selectedIds.Contains(s.Id)).ToList();
            db.Sessions.RemoveRange(sessions);
            db.SaveChanges();
        }

        RefreshTimelineTab();
    }

    private void TimelineStripCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        RefreshTimelineStrip();
    }

    // Draws each session as a colored rectangle positioned by start time and
    // sized by duration, spanning from the day's earliest session to either
    // "now" (if viewing today) or the day's latest session end. Hovering a
    // rectangle shows a tooltip with its label and duration.
    private void RefreshTimelineStrip()
    {
        TimelineStripCanvas.Children.Clear();

        var width = TimelineStripCanvas.ActualWidth;
        if (width <= 0 || _timelineSessions.Count == 0)
        {
            TimelineStripStartText.Text = string.Empty;
            TimelineStripEndText.Text = string.Empty;
            return;
        }

        var spanStart = _timelineSessions.Min(s => s.Start);
        var spanEnd = _timelineDate.Date == DateTime.Today
            ? DateTime.UtcNow
            : _timelineSessions.Max(s => s.End ?? s.Start);

        var totalSeconds = Math.Max(1, (spanEnd - spanStart).TotalSeconds);
        const double stripHeight = 40;
        const double minWidth = 3;

        foreach (var session in _timelineSessions)
        {
            var end = session.End ?? spanEnd;
            var left = (session.Start - spanStart).TotalSeconds / totalSeconds * width;
            var rectWidth = Math.Max(minWidth, Math.Min(width - left, (end - session.Start).TotalSeconds / totalSeconds * width));

            var label = session.Memo?.Name ?? session.Domain ?? $"{session.Process} - {session.WindowTitle}";

            var rect = new Rectangle
            {
                Width = rectWidth,
                Height = stripHeight,
                Fill = ColorForLabel(label),
                ToolTip = $"{label} - {FormatDurationLong(end - session.Start)}"
            };

            Canvas.SetLeft(rect, left);
            Canvas.SetTop(rect, 0);
            TimelineStripCanvas.Children.Add(rect);
        }

        TimelineStripStartText.Text = spanStart.ToLocalTime().ToString("h:mm tt");
        TimelineStripEndText.Text = spanEnd.ToLocalTime().ToString("h:mm tt");
    }

    // Deterministic color per label - the same website/app/memo always gets
    // the same color, so the strip is scannable at a glance across refreshes.
    private static Brush ColorForLabel(string label)
    {
        unchecked
        {
            var hash = 17;
            foreach (var c in label)
            {
                hash = hash * 31 + c;
            }

            var hue = Math.Abs(hash) % 360;
            return new SolidColorBrush(HsvToColor(hue, 0.55, 0.85));
        }
    }

    private static Color HsvToColor(double hue, double saturation, double value)
    {
        var c = value * saturation;
        var x = c * (1 - Math.Abs(hue / 60.0 % 2 - 1));
        var m = value - c;

        double r, g, b;
        if (hue < 60) { r = c; g = x; b = 0; }
        else if (hue < 120) { r = x; g = c; b = 0; }
        else if (hue < 180) { r = 0; g = c; b = x; }
        else if (hue < 240) { r = 0; g = x; b = c; }
        else if (hue < 300) { r = x; g = 0; b = c; }
        else { r = c; g = 0; b = x; }

        return Color.FromRgb((byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
    }

    private static string FormatDurationLong(TimeSpan span)
    {
        var hours = (int)span.TotalHours;
        var minutes = span.Minutes;

        if (hours > 0)
        {
            return minutes > 0 ? $"{hours} hr(s) {minutes} min(s)" : $"{hours} hr(s)";
        }

        if (span.TotalMinutes >= 1)
        {
            return $"{(int)span.TotalMinutes} min(s)";
        }

        return $"{(int)span.TotalSeconds} sec(s)";
    }

    private void TimelineDatePicker_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
    {
        RefreshTimelineTab();
    }

    private void TimelineSortOrderBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RefreshTimelineTab();
    }

    private void RefreshTimelineButton_Click(object sender, RoutedEventArgs e)
    {
        ConfigureTimelineDatePicker();
        RefreshTimelineTab();
    }

    private void ApplyMemoButton_Click(object sender, RoutedEventArgs e)
    {
        var name = (TimelineMemoBox.Text ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show(this, "Type or pick a memo name first.", "Activity Tracker", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var selectedIds = TimelineList.SelectedItems.Cast<TimelineRow>().Select(r => r.SessionId).ToList();
        if (selectedIds.Count == 0)
        {
            MessageBox.Show(this, "Select one or more rows first.", "Activity Tracker", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        using (var db = new AppDbContext())
        {
            var memoId = MemoRepository.ResolveOrCreate(db, name);
            var sessions = db.Sessions.Where(s => selectedIds.Contains(s.Id)).ToList();

            foreach (var session in sessions)
            {
                session.MemoId = memoId;
            }

            db.SaveChanges();
        }

        RefreshMemoPickers();
        RefreshTimelineTab();
    }

    private void ClearMemoButton_Click(object sender, RoutedEventArgs e)
    {
        var selectedIds = TimelineList.SelectedItems.Cast<TimelineRow>().Select(r => r.SessionId).ToList();
        if (selectedIds.Count == 0)
        {
            MessageBox.Show(this, "Select one or more rows first.", "Activity Tracker", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        using (var db = new AppDbContext())
        {
            var sessions = db.Sessions.Where(s => selectedIds.Contains(s.Id)).ToList();

            foreach (var session in sessions)
            {
                session.MemoId = null;
            }

            db.SaveChanges();
        }

        RefreshTimelineTab();
    }

    // Only lets the user pick a day that actually has a log file, plus today
    // (even before a log file for today exists yet). Shared by Summary and Timeline.
    private static void ConfigureDatePicker(DatePicker picker)
    {
        var availableDates = JsonlSessionLogger.GetAvailableLogDates();

        if (!availableDates.Contains(DateTime.Today))
        {
            availableDates.Add(DateTime.Today);
        }

        var previouslySelected = picker.SelectedDate;

        var sorted = availableDates.Distinct().OrderBy(d => d).ToList();
        var minDate = sorted.First();
        var maxDate = DateTime.Today;

        picker.DisplayDateStart = minDate;
        picker.DisplayDateEnd = maxDate;

        var availableSet = new HashSet<DateTime>(sorted);
        picker.BlackoutDates.Clear();

        var current = minDate;
        DateTime? gapStart = null;

        while (current <= maxDate)
        {
            if (availableSet.Contains(current))
            {
                if (gapStart != null)
                {
                    picker.BlackoutDates.Add(new CalendarDateRange(gapStart.Value, current.AddDays(-1)));
                    gapStart = null;
                }
            }
            else
            {
                gapStart ??= current;
            }

            current = current.AddDays(1);
        }

        if (gapStart != null)
        {
            picker.BlackoutDates.Add(new CalendarDateRange(gapStart.Value, maxDate));
        }

        picker.SelectedDate = previouslySelected ?? DateTime.Today;
    }

    private void ConfigureTimelineDatePicker()
    {
        ConfigureDatePicker(TimelineDatePicker);
    }

    private void BrowseWallpaperButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Image files (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|All files (*.*)|*.*"
        };

        if (dialog.ShowDialog(this) == true)
        {
            WallpaperPathBox.Text = dialog.FileName;
        }
    }

    private void ClearWallpaperButton_Click(object sender, RoutedEventArgs e)
    {
        WallpaperPathBox.Text = string.Empty;
    }

    private void SaveSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(IdlePollIntervalBox.Text, out var pollSeconds) || pollSeconds <= 0)
        {
            MessageBox.Show(this, "Idle poll interval must be a positive whole number of seconds.", "Activity Tracker", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!int.TryParse(IdleThresholdBox.Text, out var idleSeconds) || idleSeconds <= 0)
        {
            MessageBox.Show(this, "Idle threshold must be a positive whole number of seconds.", "Activity Tracker", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        AppSettings.Current.IdlePollIntervalSeconds = pollSeconds;
        AppSettings.Current.IdleThresholdSeconds = idleSeconds;
        AppSettings.Current.CodingProcessNames = SplitLines(CodingProcessNamesBox.Text);
        AppSettings.Current.BrowserProcessNames = SplitLines(BrowserProcessNamesBox.Text);
        AppSettings.Current.WallpaperPath = string.IsNullOrWhiteSpace(WallpaperPathBox.Text) ? null : WallpaperPathBox.Text;

        AppSettings.Save();
        _trackingService.ApplySettings();
        ApplyWallpaper();

        SettingsSavedText.Text = "Saved.";
    }

    private void ApplyWallpaper()
    {
        var path = AppSettings.Current.WallpaperPath;

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            RootGrid.Background = null;
            return;
        }

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();

            RootGrid.Background = new ImageBrush(bitmap)
            {
                Stretch = Stretch.UniformToFill
            };
        }
        catch
        {
            RootGrid.Background = null;
        }
    }

    private static List<string> SplitLines(string text)
    {
        return text
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToList();
    }
}

public class KeyValueRow
{
    public required string Label { get; init; }
    public required string Value { get; init; }
}

public class WindowUsageRow
{
    public required string Process { get; init; }
    public required string WindowTitle { get; init; }
    public required string TimeDisplay { get; init; }
}

public class SwitchLogRow
{
    public required string StartDisplay { get; init; }
    public required string EndDisplay { get; init; }
    public required string Process { get; init; }
    public required string WindowTitle { get; init; }
    public required string DurationDisplay { get; init; }
}

public class TimelineRow
{
    public required int SessionId { get; init; }
    public required string StartDisplay { get; init; }
    public required string EndDisplay { get; init; }
    public required string DurationDisplay { get; init; }
    public required string Process { get; init; }
    public required string WindowTitle { get; init; }
    public required string MemoDisplay { get; init; }
}
