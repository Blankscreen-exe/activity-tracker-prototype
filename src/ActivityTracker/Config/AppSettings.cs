using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace ActivityTracker.Config;

public class AppSettings
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public int IdlePollIntervalSeconds { get; set; } = 7;
    public int IdleThresholdSeconds { get; set; } = 120;
    public string SummaryDateFormat { get; set; } = "MM-dd-yyyy";

    public List<string> CodingProcessNames { get; set; } = new()
    {
        "Code", "devenv", "rider64", "pycharm64", "sublime_text", "notepad++", "Cursor"
    };

    public List<string> BrowserProcessNames { get; set; } = new()
    {
        "chrome", "msedge", "brave", "firefox"
    };

    public string? WallpaperPath { get; set; }

    // The memo automatically applied to every new session until changed -
    // see TrackingService.StartNewSession.
    public string? ActiveMemoName { get; set; }

    public static AppSettings Current { get; private set; } = new();

    private static string ConfigPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ActivityTracker",
            "config.json");

    public static void Load()
    {
        var path = ConfigPath;

        if (!File.Exists(path))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(new AppSettings(), SerializerOptions));
            Current = new AppSettings();
            return;
        }

        try
        {
            var json = File.ReadAllText(path);
            Current = JsonSerializer.Deserialize<AppSettings>(json, SerializerOptions) ?? new AppSettings();
        }
        catch
        {
            Current = new AppSettings();
        }
    }

    public static void Save()
    {
        var path = ConfigPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(Current, SerializerOptions));
    }
}
