namespace ActivityTracker.Models;

public class Memo
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;

    // Hex string like "#316AC5" - assigned a deterministic default on
    // creation (see ColorUtil.GetHashHexColor), editable afterward.
    public string Color { get; set; } = "#316AC5";
}
