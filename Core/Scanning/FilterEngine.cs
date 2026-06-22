namespace HashCheck.Core.Scanning;

public sealed class FilterEngine
{
    private readonly FilterMode _mode;
    private readonly IReadOnlyList<string> _patterns;

    public static readonly string[] DefaultExcludePatterns =
    [
        "Thumbs.db", "*.tmp", "$RECYCLE.BIN\\", "System Volume Information\\",
        "desktop.ini", "*.lnk"
    ];

    public FilterEngine(FilterMode mode, IEnumerable<string> patterns)
    {
        _mode = mode;
        _patterns = patterns.ToList();
    }

    public bool ShouldIncludeFile(string relativePath)
    {
        var fileName = Path.GetFileName(relativePath);
        bool matchesAny = _patterns.Any(p => Matches(p, fileName, relativePath));

        return _mode == FilterMode.Exclude ? !matchesAny : matchesAny;
    }

    public bool ShouldIncludeDirectory(string relativePath)
    {
        // Directory patterns end with \
        var dirPatterns = _patterns
            .Where(p => p.EndsWith('\\'))
            .Select(p => p.TrimEnd('\\'));

        var dirName = Path.GetFileName(relativePath.TrimEnd('\\'));
        bool matchesAny = dirPatterns.Any(p => Matches(p, dirName, relativePath));

        return _mode == FilterMode.Exclude ? !matchesAny : true;
    }

    private static bool Matches(string pattern, string fileName, string relativePath)
    {
        if (pattern.EndsWith('\\'))
            return false; // directory pattern, handled separately

        if (pattern.Contains('*'))
        {
            var parts = pattern.Split('*');
            if (parts.Length == 2)
            {
                var prefix = parts[0];
                var suffix = parts[1];
                return fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                    && fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
            }
            return false;
        }

        return string.Equals(pattern, fileName, StringComparison.OrdinalIgnoreCase);
    }
}
