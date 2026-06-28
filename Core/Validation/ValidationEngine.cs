using HashCheck.Core.HashFile;
using HashCheck.Core.Hashing;
using HashCheck.Core.Scanning;

namespace HashCheck.Core.Validation;

/// <summary>Compares media files against a <see cref="HashFileData"/> baseline and produces a <see cref="ValidationReport"/>.</summary>
public sealed class ValidationEngine
{
    /// <summary>Scans <paramref name="mediaRoot"/> and compares each file's hash against the stored baseline. Supports cooperative pause via <paramref name="pauseToken"/>.</summary>
    public async Task<ValidationReport> ValidateAsync(
        HashFileData hashFile,
        string mediaRoot,
        IProgress<ScanProgress>? progress,
        CancellationToken ct,
        PauseToken? pauseToken = null)
    {
        var report = new ValidationReport
        {
            Timestamp = DateTime.UtcNow,
            MediaName = hashFile.MediaName,
            HashFilePath = hashFile.FilePath,
            TotalFilesInHashSet = hashFile.Files.Count
        };

        var hasher = HasherFactory.Create(hashFile.Algorithm);
        var filter = new FilterEngine(hashFile.FilterMode, hashFile.Filters);
        var scanner = new FileScanner(filter);

        // Build lookup of stored files
        var stored = hashFile.Files.ToDictionary(
            f => f.RelativePath,
            f => f,
            StringComparer.OrdinalIgnoreCase);

        var scannedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        long bytesTotal = hashFile.Files.Sum(f => f.SizeBytes);
        long bytesProcessed = 0;
        int filesProcessed = 0;
        int filesTotal = hashFile.Files.Count;
        var startTime = DateTime.UtcNow;

        // Use only top-level scope paths so FileScanner doesn't visit every
        // subdirectory twice (it already recurses into children).
        var scopePaths = GetTopLevelPaths(hashFile.Paths);

        // Enumerate files on the thread pool so the UI stays responsive during directory scanning.
        var allItems = await Task.Run(() => scanner.Scan(mediaRoot, scopePaths, ct).ToList(), ct);
        filesTotal = allItems.Count;
        bytesTotal = allItems.Sum(i => i.Info.Length);
        report.TotalFilesFound = allItems.Count;
        report.TotalBytesFound = allItems.Sum(i => i.Info.Length);
        report.TotalDirectoriesFound = hashFile.Paths.Count(p =>
            Directory.Exists(Path.Combine(mediaRoot.TrimEnd('\\'), p.TrimStart('\\'))));

        foreach (var item in allItems)
        {
            ct.ThrowIfCancellationRequested();
            if (pauseToken != null)
                await pauseToken.WaitIfPausedAsync(ct);
            scannedPaths.Add(item.RelativePath);

            var elapsed = DateTime.UtcNow - startTime;
            TimeSpan? eta = bytesProcessed > 0 && bytesTotal > 0
                ? TimeSpan.FromSeconds(elapsed.TotalSeconds * (bytesTotal - bytesProcessed) / bytesProcessed)
                : null;

            progress?.Report(new ScanProgress(
                filesProcessed, filesTotal,
                bytesProcessed, bytesTotal,
                item.RelativePath, elapsed, eta));

            if (!stored.TryGetValue(item.RelativePath, out var storedEntry))
            {
                report.TotalNew++;
                report.NewFiles.Add(item.RelativePath);
                filesProcessed++;
                bytesProcessed += item.Info.Length;
                continue;
            }

            string computedHash;
            try
            {
                using var stream = new FileStream(
                    item.Info.FullName, FileMode.Open, FileAccess.Read,
                    FileShare.Read, 4096, useAsync: true);

                var bytesReporter = new Progress<long>(n => bytesProcessed += n);
                computedHash = await hasher.ComputeHashAsync(stream, bytesReporter, ct);
            }
            catch (Exception ex)
            {
                report.TotalErrors++;
                report.ErrorFiles.Add(new ErrorFile(item.RelativePath, ex.Message));
                filesProcessed++;
                continue;
            }

            if (computedHash.Equals(storedEntry.Hash, StringComparison.OrdinalIgnoreCase))
            {
                report.TotalMatching++;
            }
            else
            {
                var sizeChanged = item.Info.Length != storedEntry.SizeBytes;
                var mtimeChanged = Math.Abs((item.Info.LastWriteTimeUtc - storedEntry.ModifiedUtc).TotalSeconds) > 2;
                var reason = (sizeChanged || mtimeChanged) ? NotMatchingReason.Modified : NotMatchingReason.Corrupted;
                report.TotalCorrupted += reason == NotMatchingReason.Corrupted ? 1 : 0;
                report.TotalModified += reason == NotMatchingReason.Modified ? 1 : 0;
                report.NotMatchingFiles.Add(new NotMatchingFile(item.RelativePath, reason));
            }

            filesProcessed++;
        }

        // Missing = in hash set but not scanned
        foreach (var storedFile in hashFile.Files)
        {
            if (!scannedPaths.Contains(storedFile.RelativePath))
            {
                report.TotalMissing++;
                report.MissingFiles.Add(storedFile.RelativePath);
            }
        }

        return report;
    }

    /// <summary>
    /// Returns the subset of <paramref name="paths"/> that are not children of any other path in the list.
    /// Because <c>[PATHS]</c> always starts with <c>\</c> (the scan root), this typically returns <c>["\"]</c>,
    /// so <see cref="FileScanner"/> visits every subdirectory exactly once without double-counting.
    /// </summary>
    internal static IReadOnlyList<string> GetTopLevelPaths(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0) return new[] { "\\" };
        var top = paths.Where(p => !paths.Any(other =>
            !string.Equals(other, p, StringComparison.OrdinalIgnoreCase) &&
            p.StartsWith(other.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase))).ToList();
        return top.Count > 0 ? (IReadOnlyList<string>)top : new[] { "\\" };
    }
}
