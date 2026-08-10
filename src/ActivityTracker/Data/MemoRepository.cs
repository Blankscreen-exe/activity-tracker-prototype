using ActivityTracker.Models;

namespace ActivityTracker.Data;

public static class MemoRepository
{
    // Finds a memo by name (case-sensitive, trimmed) or creates it - shared by
    // the tracker's auto-tagging and the Timeline tab's bulk-apply, so typing
    // a memo name never creates near-duplicate rows for the same label.
    public static int? ResolveOrCreate(AppDbContext db, string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        name = name.Trim();

        var memo = db.Memos.FirstOrDefault(m => m.Name == name);
        if (memo == null)
        {
            memo = new Memo { Name = name, Color = ColorUtil.GetHashHexColor(name) };
            db.Memos.Add(memo);
            db.SaveChanges();
        }

        return memo.Id;
    }

    public static List<string> GetAllNames()
    {
        using var db = new AppDbContext();
        return db.Memos.OrderBy(m => m.Name).Select(m => m.Name).ToList();
    }
}
