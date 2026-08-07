using System.Windows.Automation;
using ActivityTracker.Config;

namespace ActivityTracker.Native;

public static class BrowserTabReader
{
    // Chromium/Firefox accessibility trees can be huge; cap the walk so a focus
    // change on a heavy page can't stall the tracking thread.
    private const int MaxNodesToVisit = 800;
    private const int MaxDepth = 12;

    public static bool IsBrowserProcess(string processName) =>
        AppSettings.Current.BrowserProcessNames.Contains(processName, StringComparer.OrdinalIgnoreCase);

    public static string? TryGetAddressBarText(IntPtr hwnd)
    {
        try
        {
            var root = AutomationElement.FromHandle(hwnd);
            if (root == null)
            {
                return null;
            }

            var addressBar = FindAddressBar(root);
            if (addressBar == null)
            {
                return null;
            }

            if (addressBar.TryGetCurrentPattern(ValuePattern.Pattern, out var patternObj)
                && patternObj is ValuePattern valuePattern)
            {
                return valuePattern.Current.Value;
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    public static string? ExtractDomain(string? urlText)
    {
        if (string.IsNullOrWhiteSpace(urlText))
        {
            return null;
        }

        if (Uri.TryCreate(urlText, UriKind.Absolute, out var uri))
        {
            return uri.Host;
        }

        if (Uri.TryCreate("https://" + urlText, UriKind.Absolute, out var uriWithScheme))
        {
            return uriWithScheme.Host;
        }

        return null;
    }

    private static AutomationElement? FindAddressBar(AutomationElement root)
    {
        var stack = new Stack<(AutomationElement Element, int Depth)>();
        stack.Push((root, 0));
        int visited = 0;

        while (stack.Count > 0 && visited < MaxNodesToVisit)
        {
            var (element, depth) = stack.Pop();
            visited++;

            if (IsAddressBar(element))
            {
                return element;
            }

            if (depth >= MaxDepth)
            {
                continue;
            }

            AutomationElement? child;
            try
            {
                child = TreeWalker.ControlViewWalker.GetFirstChild(element);
            }
            catch
            {
                continue;
            }

            while (child != null)
            {
                stack.Push((child, depth + 1));
                try
                {
                    child = TreeWalker.ControlViewWalker.GetNextSibling(child);
                }
                catch
                {
                    break;
                }
            }
        }

        return null;
    }

    private static bool IsAddressBar(AutomationElement element)
    {
        try
        {
            if (element.Current.ControlType != ControlType.Edit)
            {
                return false;
            }

            var name = element.Current.Name ?? string.Empty;
            var automationId = element.Current.AutomationId ?? string.Empty;

            return name.Contains("address", StringComparison.OrdinalIgnoreCase)
                || automationId.Contains("url", StringComparison.OrdinalIgnoreCase)
                || automationId.Contains("omnibox", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
