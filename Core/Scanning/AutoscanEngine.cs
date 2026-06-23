using HashCheck.Core.HashFile;
using HashCheck.Core.Hashing;
using HashCheck.Core.Validation;

namespace HashCheck.Core.Scanning;

/// <summary>Result of an autoscan run: only newly discovered files and directories are returned (existing entries are never re-hashed).</summary>
public record AutoscanResult(
    IReadOnlyList<FileEntry> AddedFiles,
    IReadOnlyList<string> AddedPaths,
    int TotalScanned,
    int TotalErrors);

/// <summary>Add-only incremental scanner: hashes files not already recorded in the hash set and returns them for merging. Never re-hashes or removes existing entries.</summary>
public sealed class AutoscanEngine
{
    /// <summary>Scans <paramref name="mediaRoot"/> for files absent from <paramref name="hashFile"/> and returns their hashes. Only new files are hashed; existing entries are untouched.</summary>
    public async Task<AutoscanResult> ScanAsync(
        HashFileData hashFile,
        string mediaRoot,
        IProgress<ScanProgress>? progress,
        CancellationToken ct)
    {
        var knownPaths = new HashSet<string>(
            hashFile.Files.Select(f => f.RelativePath),
            StringComparer.OrdinalIgnoreCase);

        var filter = new FilterEngine(hashFile.FilterMode, hashFile.Filters);
        var scanner = new FileScanner(filter);

        var scopePaths = ValidationEngine.GetTopLevelPaths(hashFile.Paths);
        var allItems = scanner.Scan(mediaRoot, scopePaths, ct).ToList();
        var newItems = allItems.Where(i => !knownPaths.Contains(i.RelativePath)).ToList();

        var hasher = HasherFactory.Create(hashFile.Algorithm);
        var addedFiles = new List<FileEntry>(newItems.Count);
        int errors = 0;

        long bytesTotal = newItems.Sum(i => i.Info.Length);
        long bytesProcessed = 0;
        int filesProcessed = 0;
        var startTime = DateTime.UtcNow;

        foreach (var item in newItems)
        {
            ct.ThrowIfCancellationRequested();

            var elapsed = DateTime.UtcNow - startTime;
            TimeSpan? eta = bytesProcessed > 0 && bytesTotal > 0
                ? TimeSpan.FromSeconds(elapsed.TotalSeconds * (bytesTotal - bytesProcessed) / bytesProcessed)
                : null;

            progress?.Report(new ScanProgress(
                filesProcessed, newItems.Count,
                bytesProcessed, bytesTotal,
                item.RelativePath, elapsed, eta));

            try
            {
                using var stream = new FileStream(
                    item.Info.FullName, FileMode.Open, FileAccess.Read,
                    FileShare.Read, 4096, useAsync: true);

                var bytesReporter = new Progress<long>(n => bytesProcessed += n);
                var hash = await hasher.ComputeHashAsync(stream, bytesReporter, ct);
                addedFiles.Add(new FileEntry(
                    item.RelativePath, hash,
                    item.Info.Length, item.Info.LastWriteTimeUtc));
            }
            catch
            {
                errors++;
            }

            filesProcessed++;
        }

        // Discover directories that weren't in [PATHS] previously
        var knownDirs = new HashSet<string>(hashFile.Paths, StringComparer.OrdinalIgnoreCase);
        var allDirs = scanner.GetAllDirectories(mediaRoot, scopePaths, ct);
        var newDirs = allDirs.Where(d => !knownDirs.Contains(d)).ToList();

        return new AutoscanResult(addedFiles, newDirs, newItems.Count, errors);
    }
}
