using System.IO;
using Microsoft.EntityFrameworkCore;
using ActivityTracker.Models;

namespace ActivityTracker.Data;

public class AppDbContext : DbContext
{
    public DbSet<Session> Sessions => Set<Session>();
    public DbSet<Memo> Memos => Set<Memo>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        var dbPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ActivityTracker",
            "activitytracker.db");

        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

        optionsBuilder.UseSqlite($"Data Source={dbPath}");
    }
}
