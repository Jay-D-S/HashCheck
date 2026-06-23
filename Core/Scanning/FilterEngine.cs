namespace HashCheck.Core.Scanning;

/// <summary>Evaluates per-set include/exclude filter patterns against relative file and directory paths.</summary>
public sealed class FilterEngine
{
    private readonly FilterMode _mode;
    private readonly IReadOnlyList<string> _patterns;

    /// <summary>Default patterns applied when a new hash set is created with <see cref="FilterMode.Exclude"/>.</summary>
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

    /// <summary>Returns <c>true</c> if a file at <paramref name="relativePath"/> passes the current filter rules.</summary>
    public bool ShouldIncludeFile(string relativePath)
    {
        var fileName = Path.GetFileName(relativePath);
        bool matchesAny = _patterns.Any(p => Matches(p, fileName, relativePath));

        return _mode == FilterMode.Exclude ? !matchesAny : matchesAny;
    }

    /// <summary>Returns <c>true</c> if a directory at <paramref name="relativePath"/> should be entered during a scan. In Include mode, directories are always entered (files inside are checked individually).</summary>
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
