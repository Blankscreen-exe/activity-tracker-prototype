using System.Diagnostics;
using System.Windows.Threading;
using ActivityTracker.Config;
using ActivityTracker.Data;
using ActivityTracker.Logging;
using ActivityTracker.Models;
using ActivityTracker.Native;

namespace ActivityTracker.Tracking;

public class TrackingService
{
    private static TimeSpan IdleThreshold => TimeSpan.FromSeconds(AppSettings.Current.IdleThresholdSeconds);

    private readonly ForegroundWindowWatcher _watcher = new();
    private readonly JsonlSessionLogger _logger = new();
    private readonly DispatcherTimer _pollTimer;

    private Session? _currentSession;
    private IntPtr _currentHwnd;
    private bool _isIdle;

    public event Action<Session>? SessionStarted;

    public TrackingService()
    {
        _pollTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(AppSettings.Current.IdlePollIntervalSeconds)
        };
        _pollTimer.Tick += (_, _) => OnPollTick();
    }

    public void Start()
    {
        _watcher.FocusChanged += OnFocusChanged;
        _watcher.Start();
        _pollTimer.Start();

        StartNewSession(Win32.GetForegroundWindow());
    }

    // Called after the Settings tab saves changes, so a running tracker
    // picks up a new poll interval without needing an app restart.
    public void ApplySettings()
    {
        _pollTimer.Interval = TimeSpan.FromSeconds(AppSettings.Current.IdlePollIntervalSeconds);
    }

    public void Stop()
    {
        _pollTimer.Stop();
        _watcher.FocusChanged -= OnFocusChanged;
        _watcher.Dispose();

        FinalizeCurrentSession(DateTime.UtcNow);
    }

    private void OnFocusChanged(IntPtr hwnd)
    {
        if (_isIdle)
        {
            return;
        }

        FinalizeCurrentSession(DateTime.UtcNow);
        StartNewSession(hwnd);
    }

    private void OnPollTick()
    {
        var idleTime = Win32.GetIdleTime();

        if (!_isIdle && idleTime >= IdleThreshold)
        {
            _isIdle = true;
            var lastActiveMoment = DateTime.UtcNow - idleTime;
            FinalizeCurrentSession(lastActiveMoment);
            return;
        }

        if (_isIdle && idleTime < IdleThreshold)
        {
            _isIdle = false;
            StartNewSession(Win32.GetForegroundWindow());
            return;
        }

        if (_isIdle)
        {
            return;
        }

        // EVENT_SYSTEM_FOREGROUND only fires on a window-handle change, so
        // switching tabs inside the same browser window never triggers
        // OnFocusChanged. Catch that here by polling the title of the window
        // we're already tracking, since Chromium/Firefox put the active
        // tab's title in the window title.
        CheckForInPlaceTabChange();
    }

    private void CheckForInPlaceTabChange()
    {
        if (_currentSession == null || _currentHwnd == IntPtr.Zero)
        {
            return;
        }

        if (!BrowserTabReader.IsBrowserProcess(_currentSession.Process))
        {
            return;
        }

        var hwnd = Win32.GetForegroundWindow();
        if (hwnd != _currentHwnd)
        {
            return;
        }

        var title = Win32.GetWindowTitle(hwnd);
        if (title == _currentSession.WindowTitle)
        {
            return;
        }

        FinalizeCurrentSession(DateTime.UtcNow);
        StartNewSession(hwnd);
    }

    private void StartNewSession(IntPtr hwnd)
    {
        _currentHwnd = hwnd;

        if (hwnd == IntPtr.Zero)
        {
            _currentSession = null;
            return;
        }

        var processId = Win32.GetProcessId(hwnd);
        var processName = TryGetProcessName(processId);
        var windowTitle = Win32.GetWindowTitle(hwnd);

        var session = new Session
        {
            Start = DateTime.UtcNow,
            Process = processName,
            WindowTitle = windowTitle
        };

        _currentSession = session;
        SessionStarted?.Invoke(session);

        if (BrowserTabReader.IsBrowserProcess(processName))
        {
            AttachBrowserInfoAsync(session, hwnd);
        }
    }

    private async void AttachBrowserInfoAsync(Session session, IntPtr hwnd)
    {
        try
        {
            var url = await Task.Run(() => BrowserTabReader.TryGetAddressBarText(hwnd));

            // The session may already have ended (and been persisted) by the
            // time this UI Automation lookup finishes - only apply it if it's
            // still the live session.
            if (!ReferenceEquals(_currentSession, session))
            {
                return;
            }

            session.Url = url;
            session.Domain = BrowserTabReader.ExtractDomain(url);
            session.TabTitle = session.WindowTitle;
        }
        catch
        {
            // Best-effort UI Automation lookup; leave Url/Domain/TabTitle unset on failure.
        }
    }

    private void FinalizeCurrentSession(DateTime endTime)
    {
        var session = _currentSession;
        _currentSession = null;

        if (session == null)
        {
            return;
        }

        session.End = endTime;
        session.Duration = endTime - session.Start;

        using var db = new AppDbContext();

        // Auto-tag with whatever memo is currently "active" (see
        // AppSettings.ActiveMemoName), if any - resolved here rather than at
        // StartNewSession so the frequent focus-change path stays DB-free.
        session.MemoId = MemoRepository.ResolveOrCreate(db, AppSettings.Current.ActiveMemoName);

        db.Sessions.Add(session);
        db.SaveChanges();

        _logger.Append(session);
    }

    private static string TryGetProcessName(uint processId)
    {
        try
        {
            return Process.GetProcessById((int)processId).ProcessName;
        }
        catch
        {
            return "unknown";
        }
    }
}
