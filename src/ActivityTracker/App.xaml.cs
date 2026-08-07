using System.Configuration;
using System.Data;
using System.Globalization;
using System.Windows;
using ActivityTracker.Config;
using ActivityTracker.Data;
using ActivityTracker.Native;
using ActivityTracker.Stats;
using Microsoft.EntityFrameworkCore;

namespace ActivityTracker;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        AppSettings.Load();

        // The DB file only exists if migrations have been applied to it - do
        // that here so a fresh install (or a wiped DB) self-heals instead of
        // crashing the moment something tries to query the Sessions table.
        using (var db = new AppDbContext())
        {
            db.Database.Migrate();
        }

        if (e.Args.Length > 0 && string.Equals(e.Args[0], "summary", StringComparison.OrdinalIgnoreCase))
        {
            RunSummaryCommand(e.Args);
            Shutdown();
            return;
        }

        base.OnStartup(e);
    }

    private static void RunSummaryCommand(string[] args)
    {
        Win32.AttachToParentConsole();

        var date = DateTime.Today;
        var dateFormat = AppSettings.Current.SummaryDateFormat;

        if (args.Length > 1)
        {
            if (!DateTime.TryParseExact(args[1], dateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
            {
                Console.WriteLine($"Could not parse date '{args[1]}'. Expected format: {dateFormat}");
                return;
            }
        }

        var stats = StatsCalculator.Calculate(date);
        Console.WriteLine(SummaryFormatter.Format(stats));
    }
}

