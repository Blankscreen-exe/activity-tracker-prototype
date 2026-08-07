a windows cmdlet app which is an activity tracker.
it logs my active windows and in case of browser or ( any tab enabled app) which tab, and how much time i have spent on each. 
it should also keep a record in a formatted text file log or something 
for each day it will create a separate log file with date as name in a special folder. if the tracker stops and is restarted then it will continue to log inside the same log file for today.
it should also support a command where it gives me a summary of my activity up till the time i call it.  the command could work like this:
main.exe summary <no date for today's summary or a specific date in 00-00-0000 format>
the logs should be structured and easily readable

# Data Model
- Session
- id
- start
- end
- duration
- process
- window_title
- tab_title
- url(optional)
- domain

Read the browser's accessibility tree using Microsoft UI Automation.

You can obtain the active tab title from Chrome, Edge, Firefox, Brave, etc., without needing browser extensions in many cases.

Useful Statistics
- Daily coding time
- Daily browsing time
- Top websites
- Context switches
- Average uninterrupted focus session
- Idle time
- App usage by week
- Heatmap by hour
- Longest focus session
- Most distracting websites

Tech Stack
.NET 9
WPF (simple desktop UI)
SQLite (embedded database)
Entity Framework Core
Windows UI Automation (tab titles)
Win32 API (GetForegroundWindow, GetWindowText, etc.)
Background service for tracking
System.Text.Json for JSONL export

need sugesstions on:
- should the logs be in plain text or json? JSONL it is
- Polling every 5-10 second is sufficient?
- 