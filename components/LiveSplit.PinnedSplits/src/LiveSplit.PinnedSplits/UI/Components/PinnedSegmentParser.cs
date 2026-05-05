namespace LiveSplit.UI.Components;

internal static class PinnedSegmentParser
{
    public static (bool IsPinned, string DisplayName) Parse(string name)
    {
        if (string.IsNullOrEmpty(name)) return (false, name);
        if (name[0] != '^') return (false, name);
        if (name.Length == 1) return (false, string.Empty);
        if (name[1] == '^' && (name.Length == 2 || name[2] != '^')) return (false, name.Substring(1));
        return (true, name.Substring(1));
    }
}
