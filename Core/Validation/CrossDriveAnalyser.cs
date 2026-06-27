using HashCheck.Core.HashFile;
using HashCheck.Core.Hashing;

namespace HashCheck.Core.Validation;

/// <summary>Compares a set of corrupted files against all other online volumes using majority-vote logic to determine whether the new mirror or the stored baseline is more likely to be correct.</summary>
public static class CrossDriveAnalyser
{
    /// <summary>
    /// For each <see cref="NotMatchingReason.Corrupted"/> file in <paramref name="newMirrorReport"/>,
    /// re-hashes the file on every volume in <paramref name="otherOnlineVolumes"/> and applies
    /// majority-vote classification.
    /// </summary>
    /// <param name="hashFile">The hash set data (provides stored hashes and algorithm).</param>
    /// <param name="newMirrorReport">Validation report for the newly registered mirror volume.</param>
    /// <param name="otherOnlineVolumes">Other online volumes — list of (serial, absolute scan root).</param>
    /// <param name="progress">Reports (filesProcessed, filesTotal, currentFile).</param>
    /// <param name="ct">Cancellation token.</param>
    public static async Task<CrossDriveAnalysisReport> AnalyseAsync(
        HashFileData hashFile,
        ValidationReport newMirrorReport,
        IReadOnlyList<(string serial, string scanRoot)> otherOnlineVolumes,
        IProgress<(int processed, int total, string currentFile)>? progress,
        CancellationToken ct)
    {
        var hasher = HasherFactory.Create(hashFile.Algorithm);

        // Build a fast lookup: relativePath → stored hash
        var storedHashes = hashFile.Files.ToDictionary(
            f => f.RelativePath,
            f => f.Hash,
            StringComparer.OrdinalIgnoreCase);

        // Only analyse Corrupted files — Modified files may have been intentionally changed
        // on one drive but not others, which would produce misleading vote results.
        var corruptedFiles = newMirrorReport.NotMatchingFiles
            .Where(f => f.Reason == NotMatchingReason.Corrupted)
            .Select(f => f.RelativePath)
            .ToList();

        var results = new List<CrossDriveFileResult>(corruptedFiles.Count);

        for (int i = 0; i < corruptedFiles.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            var relativePath = corruptedFiles[i];
            progress?.Report((i, corruptedFiles.Count, relativePath));

            if (!storedHashes.TryGetValue(relativePath, out var storedHash))
                continue;

            // Compute hash on each other online volume
            var otherHashes = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (var (serial, scanRoot) in otherOnlineVolumes)
            {
                ct.ThrowIfCancellationRequested();

                // Build the absolute path using \\?\ prefix for long-path support
                var subPath = relativePath.TrimStart('\\');
                var absPath = @"\\?\" + scanRoot.TrimEnd('\\') + '\\' + subPath;

                try
                {
                    using var stream = new FileStream(absPath, FileMode.Open, FileAccess.Read,
                        FileShare.Read, 4096, useAsync: true);
                    otherHashes[serial] = await hasher.ComputeHashAsync(stream, null, ct);
                }
                catch
                {
                    otherHashes[serial] = null;   // unreadable — excluded from vote
                }
            }

            var status = Classify(storedHash, otherHashes);
            results.Add(new CrossDriveFileResult(relativePath, storedHash, otherHashes, status));
        }

        // Report completion
        progress?.Report((corruptedFiles.Count, corruptedFiles.Count, ""));

        return new CrossDriveAnalysisReport
        {
            Results = results,
            TotalRegisteredVolumes = hashFile.Volumes.Count,
            OtherOnlineVolumeCount = otherOnlineVolumes.Count,
        };
    }

    private static CrossDriveFileStatus Classify(
        string storedHash,
        IReadOnlyDictionary<string, string?> otherHashes)
    {
        var readable = otherHashes.Values
            .Where(h => h != null)
            .Cast<string>()
            .ToList();

        if (readable.Count == 0)
            return CrossDriveFileStatus.Indeterminate;

        // At least one other drive agrees with stored → new mirror is the outlier
        if (readable.Any(h => h.Equals(storedHash, StringComparison.OrdinalIgnoreCase)))
            return CrossDriveFileStatus.NewMirrorCorrupted;

        // All other readable drives agree on the same hash (which differs from stored) →
        // the stored hash (created from the original drive) may be wrong
        if (readable.All(h => h.Equals(readable[0], StringComparison.OrdinalIgnoreCase)))
            return CrossDriveFileStatus.StoredHashSuspect;

        return CrossDriveFileStatus.Indeterminate;
    }
}
