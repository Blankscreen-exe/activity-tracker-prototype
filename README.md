# Activity Tracker

A Windows desktop app that tracks which window (and, for browsers, which tab) is in
the foreground and how long you spend on it, with a WPF UI, a CLI summary command,
and both a SQLite database and structured JSONL logs.

## Preview

![preview](docs/images/preview.png)

## Features

- Tracks the active window/process, and for supported browsers (Chrome, Edge, Brave,
  Firefox) the active tab title, URL, and domain via Windows UI Automation.
- Event-driven focus tracking (`SetWinEventHook`) plus periodic idle detection
  (`GetLastInputInfo`) - idle time is excluded from active session durations.
- Every session is persisted to a SQLite database (via EF Core) **and** appended to a
  per-day JSONL log file, so you always have both a queryable DB and a plain-text log.
- `ActivityTracker.exe summary [date]` prints a detailed daily report to the console.
- WPF UI with four tabs: **Tracker** (live status + today's stats + pie chart),
  **Summary** (per-day breakdown with charts, tables, and a date picker limited to
  days that actually have data), **Trends** (activity by hour, app usage over the
  last 7 days), and **Settings** (poll interval, idle threshold, process-name lists,
  wallpaper).
- Retro Windows XP / Windows Media Player-styled theme (Tahoma font, Luna-blue
  gradients, rounded buttons) applied across the whole UI.

## Requirements

- Windows (the app relies on Win32 APIs and UI Automation, so it will not run on
  other platforms).
- .NET 9 SDK.

## Build & run

From the repo root:

```
dotnet build
```

The built executable lands at `src\ActivityTracker\bin\Debug\net9.0-windows\ActivityTracker.exe`.
Just run it - on first launch it automatically applies EF Core migrations, so no
manual `dotnet ef database update` step is required.

## Usage

**GUI**: launch `ActivityTracker.exe` with no arguments. Hit "Start Tracking" and it
runs in the background while you use your PC normally.

**CLI summary**:

```
ActivityTracker.exe summary                  # today
ActivityTracker.exe summary 08-07-2026       # a specific date (MM-DD-YYYY by default)
```

The date format is controlled by `summaryDateFormat` in `config.json` (see below);
it's not editable from the Settings tab, only by hand-editing the config file.

## Configuration

Settings live in `%LOCALAPPDATA%\ActivityTracker\config.json`, created with defaults
on first run:

| Field | Meaning |
|---|---|
| `idlePollIntervalSeconds` | How often the tracker polls for idle time and in-window tab changes. |
| `idleThresholdSeconds` | How long without input before a session is cut off as idle. |
| `summaryDateFormat` | .NET date format string used to parse the CLI `summary` command's date argument. |
| `codingProcessNames` | Process names counted toward "coding time" (e.g. `Code`, `devenv`). |
| `browserProcessNames` | Process names treated as browsers for tab/URL extraction and "browsing time". |
| `wallpaperPath` | Optional background image for the main window. |

Most of these (aside from the date format) are also editable from the Settings tab,
which writes back to the same file.

## Data storage

- SQLite database: `%LOCALAPPDATA%\ActivityTracker\activitytracker.db`
- JSONL logs: `%LOCALAPPDATA%\ActivityTracker\logs\yyyy-MM-dd.jsonl` (one file per
  local calendar day; restarting the tracker just resumes appending to today's file)

Timestamps are stored in UTC on disk (both DB and JSONL) but always converted to the
machine's local timezone for display in the UI and CLI output.

## Project structure

| Folder | Contents |
|---|---|
| `Models` | `Session`, the core tracked-activity record. |
| `Data` | EF Core `AppDbContext` and migrations. |
| `Native` | Win32 P/Invoke, the foreground-window event hook, and UI Automation browser-tab reading. |
| `Tracking` | `TrackingService`, which ties the hook, idle polling, DB, and JSONL logger together. |
| `Logging` | The JSONL session logger. |
| `Stats` | Daily/weekly/hourly stats calculations and the CLI summary text formatter. |
| `Config` | `AppSettings`, loaded from and saved to `config.json`. |
| `Themes` | The retro XP/WMP WPF style resources. |

## Known limitations

- Browser tab/URL extraction is best-effort: it walks the browser's UI Automation
  tree looking for an address-bar-like edit control, which can fail silently on
  browser versions/localizations that don't match the expected element names.
- The Trends tab's "activity by hour" chart is a column/bar chart (24 hourly totals
  across all history), not a literal day-of-week x hour-of-day heatmap grid.
- Tab switches *within* the same browser window rely on polling (window title
  changes), not an instant event, so detection latency is bounded by
  `idlePollIntervalSeconds`.
