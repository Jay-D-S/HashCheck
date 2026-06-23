namespace HashCheck.Core.Scanning;

/// <summary>A file found during a directory scan, with its relative path (relative to the scan root) and metadata.</summary>
public record ScanItem(string RelativePath, FileInfo Info);

/// <summary>Recursive directory walker that applies <see cref="FilterEngine"/> rules and skips symlinks/junctions by default.</summary>
public sealed class FileScanner
{
    private readonly FilterEngine _filter;
    // Symlink/junction traversal is deliberately disabled — following them risks infinite loops
    // and scanning locations outside the intended scan root.
    private readonly bool _followSymlinks = false;

    public FileScanner(FilterEngine filter)
    {
        _filter = filter;
    }

    /// <summary>
    /// Enumerates all files under the given scope paths on the media root.
    /// scopePaths are relative paths like "\" or "\Photos".
    /// </summary>
    public IEnumerable<ScanItem> Scan(string mediaRoot, IEnumerable<string> scopePaths, CancellationToken ct = default)
    {
        foreach (var scopePath in scopePaths)
        {
            ct.ThrowIfCancellationRequested();
            var absolutePath = ToAbsolute(mediaRoot, scopePath);
            if (!Directory.Exists(absolutePath)) continue;

            foreach (var item in ScanDirectory(mediaRoot, absolutePath, ct))
                yield return item;
        }
    }

    /// <summary>
    /// Collects all directories found under scopePaths, as relative paths.
    /// </summary>
    public IEnumerable<string> GetAllDirectories(string mediaRoot, IEnumerable<string> scopePaths, CancellationToken ct = default)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var scopePath in scopePaths)
        {
            ct.ThrowIfCancellationRequested();
            var absolutePath = ToAbsolute(mediaRoot, scopePath);
            if (!Directory.Exists(absolutePath)) continue;

            foreach (var dir in EnumerateDirectories(mediaRoot, absolutePath, ct))
            {
                var rel = ToRelative(mediaRoot, dir);
                if (seen.Add(rel)) yield return rel;
            }
        }
    }

    private IEnumerable<ScanItem> ScanDirectory(string mediaRoot, string dirPath, CancellationToken ct)
    {
        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(dirPath);
        }
        catch { yield break; }

        foreach (var filePath in files)
        {
            ct.ThrowIfCancellationRequested();
            var rel = ToRelative(mediaRoot, filePath);
            if (!_filter.ShouldIncludeFile(rel)) continue;

            FileInfo? info = null;
            try { info = new FileInfo(filePath); }
            catch { continue; }

            // Skip symlinks/junctions by default
            if (!_followSymlinks && (info.Attributes & FileAttributes.ReparsePoint) != 0)
                continue;

            yield return new ScanItem(rel, info);
        }

        IEnumerable<string> subdirs;
        try
        {
            subdirs = Directory.EnumerateDirectories(dirPath);
        }
        catch { yield break; }

        foreach (var subdir in subdirs)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var di = new DirectoryInfo(subdir);
                if (!_followSymlinks && (di.Attributes & FileAttributes.ReparsePoint) != 0)
                    continue;
            }
            catch { continue; }

            var relDir = ToRelative(mediaRoot, subdir);
            if (!_filter.ShouldIncludeDirectory(relDir)) continue;

            foreach (var item in ScanDirectory(mediaRoot, subdir, ct))
                yield return item;
        }
    }

    private IEnumerable<string> EnumerateDirectories(string mediaRoot, string dirPath, CancellationToken ct)
    {
        yield return dirPath;
        IEnumerable<string> subdirs;
        try { subdirs = Directory.EnumerateDirectories(dirPath); }
        catch { yield break; }

        foreach (var subdir in subdirs)
        {
            ct.ThrowIfCancellationRequested();
            var relDir = ToRelative(mediaRoot, subdir);
            if (!_filter.ShouldIncludeDirectory(relDir)) continue;
            foreach (var d in EnumerateDirectories(mediaRoot, subdir, ct))
                yield return d;
        }
    }

    private static string ToAbsolute(string mediaRoot, string relativePath)
    {
        var root = mediaRoot.TrimEnd('\\', '/');
        var rel = relativePath.TrimStart('\\', '/');
        return rel.Length == 0 ? root + "\\" : root + "\\" + rel;
    }

    /// <summary>Converts an absolute path to a path relative to <paramref name="mediaRoot"/> with a leading backslash. Returns <paramref name="absolutePath"/> unchanged if it is not under <paramref name="mediaRoot"/>.</summary>
    public static string ToRelative(string mediaRoot, string absolutePath)
    {
        var root = mediaRoot.TrimEnd('\\', '/');
        if (absolutePath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            var rel = absolutePath[root.Length..].TrimEnd('\\', '/');
            return rel.Length == 0 ? "\\" : rel.StartsWith('\\') ? rel : "\\" + rel;
        }
        return absolutePath;
    }
}
