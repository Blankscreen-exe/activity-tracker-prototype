namespace ActivityTracker.Data;

public static class ColorUtil
{
    // Deterministic color per label - the same label always gets the same
    // color. Used both as a new memo's default color and as a fallback for
    // labels (domains/processes) that aren't tied to any memo.
    public static string GetHashHexColor(string label)
    {
        unchecked
        {
            var hash = 17;
            foreach (var c in label)
            {
                hash = hash * 31 + c;
            }

            var hue = Math.Abs(hash) % 360;
            var (r, g, b) = HsvToRgb(hue, 0.55, 0.85);
            return $"#{r:X2}{g:X2}{b:X2}";
        }
    }

    private static (byte R, byte G, byte B) HsvToRgb(double hue, double saturation, double value)
    {
        var c = value * saturation;
        var x = c * (1 - Math.Abs(hue / 60.0 % 2 - 1));
        var m = value - c;

        double r, g, b;
        if (hue < 60) { r = c; g = x; b = 0; }
        else if (hue < 120) { r = x; g = c; b = 0; }
        else if (hue < 180) { r = 0; g = c; b = x; }
        else if (hue < 240) { r = 0; g = x; b = c; }
        else if (hue < 300) { r = x; g = 0; b = c; }
        else { r = c; g = 0; b = x; }

        return ((byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
    }
}
