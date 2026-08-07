using System.Globalization;
using System.IO;
using System.Text.Json;
using ActivityTracker.Models;

namespace ActivityTracker.Logging;

public class JsonlSessionLogger
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = false
    };

    public static string LogsDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ActivityTracker",
            "logs");

    public JsonlSessionLogger()
    {
        Directory.CreateDirectory(LogsDirectory);
    }

    public void Append(Session session)
    {
        // Session.Start is stored in UTC; the log file itself is keyed by the
        // user's local calendar day, since that's what "today's log" means to a person.
        var filePath = GetLogFilePath(session.Start.ToLocalTime().Date);
        var line = JsonSerializer.Serialize(session, SerializerOptions);

        // Appending by date-stamped filename means a restart just resumes
        // writing new lines into today's file - no extra "resume" logic needed.
        File.AppendAllText(filePath, line + Environment.NewLine);
    }

    public static string GetLogFilePath(DateTime date)
    {
        var fileName = date.ToString("yyyy-MM-dd") + ".jsonl";
        return Path.Combine(LogsDirectory, fileName);
    }

    public static List<DateTime> GetAvailableLogDates()
    {
        if (!Directory.Exists(LogsDirectory))
        {
            return new List<DateTime>();
        }

        var dates = new List<DateTime>();

        foreach (var file in Directory.GetFiles(LogsDirectory, "*.jsonl"))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (DateTime.TryParseExact(name, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                dates.Add(date);
            }
        }

        return dates;
    }
}
