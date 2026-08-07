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
using ActivityTracker.Logging;
using ActivityTracker.Models;
using ActivityTracker.Stats;
using ActivityTracker.Tracking;
using LiveChartsCore;
using LiveChartsCore.Kernel;
using LiveChartsCore.SkiaSharpView;
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

        LoadSettingsIntoForm();
        ApplyWallpaper();
        ConfigureSummaryDatePicker();
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
            new() { Label = "Current Session Duration", Value = currentSessionDuration }
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

    // Only lets the user pick a day that actually has a log file, plus today
    // (even before a log file for today exists yet).
    private void ConfigureSummaryDatePicker()
    {
        var availableDates = JsonlSessionLogger.GetAvailableLogDates();

        if (!availableDates.Contains(DateTime.Today))
        {
            availableDates.Add(DateTime.Today);
        }

        var previouslySelected = SummaryDatePicker.SelectedDate;

        var sorted = availableDates.Distinct().OrderBy(d => d).ToList();
        var minDate = sorted.First();
        var maxDate = DateTime.Today;

        SummaryDatePicker.DisplayDateStart = minDate;
        SummaryDatePicker.DisplayDateEnd = maxDate;

        var availableSet = new HashSet<DateTime>(sorted);
        SummaryDatePicker.BlackoutDates.Clear();

        var current = minDate;
        DateTime? gapStart = null;

        while (current <= maxDate)
        {
            if (availableSet.Contains(current))
            {
                if (gapStart != null)
                {
                    SummaryDatePicker.BlackoutDates.Add(new CalendarDateRange(gapStart.Value, current.AddDays(-1)));
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
            SummaryDatePicker.BlackoutDates.Add(new CalendarDateRange(gapStart.Value, maxDate));
        }

        SummaryDatePicker.SelectedDate = previouslySelected ?? DateTime.Today;
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
