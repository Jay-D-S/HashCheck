using HashCheck.Core.HashFile;
using HashCheck.Core.Hashing;
using HashCheck.Core.Validation;
using HashCheck.Core.Volumes;

namespace HashCheck.Core.Repair;

/// <summary>Cross-drive repair engine. Uses completed <see cref="ValidationReport"/>s to identify corrupted files and copies intact copies from other online volumes.</summary>
public sealed class RepairEngine
{
    private readonly HashFileData _hashFile;
    private readonly IReadOnlyList<ValidationReport> _reports;
    private readonly IReadOnlyList<VolumeIdentity> _onlineVolumes;

    public RepairEngine(
        HashFileData hashFile,
        IReadOnlyList<ValidationReport> reports,
        IReadOnlyList<VolumeIdentity> onlineVolumes)
    {
        _hashFile = hashFile;
        _reports = reports;
        _onlineVolumes = onlineVolumes;
    }

    public async Task<RepairReport> RunAsync(IProgress<RepairProgress>? progress, CancellationToken ct)
    {
        var results = new List<RepairResult>();
        var hasher = HasherFactory.Create(_hashFile.Algorithm);

        var storedFiles = _hashFile.Files.ToDictionary(
            f => f.RelativePath,
            f => f,
            StringComparer.OrdinalIgnoreCase);

        // Collect all unique file paths that are Corrupted on at least one validated volume.
        var allCorrupted = _reports
            .SelectMany(r => r.NotMatchingFiles)
            .Where(f => f.Reason == NotMatchingReason.Corrupted)
            .Select(f => f.RelativePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        int filesProcessed = 0;
        int filesTotal = allCorrupted.Count;

        foreach (var filePath in allCorrupted)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report(new RepairProgress("Repairing", filePath, filesProcessed, filesTotal));

            if (!storedFiles.TryGetValue(filePath, out var storedEntry))
            {
                filesProcessed++;
                continue;
            }

            // Classify each validated volume as a good source or a corrupted target.
            var goodSerials = new List<string>();
            var corruptedSerials = new List<string>();

            foreach (var report in _reports)
            {
                bool isCorrupted = report.NotMatchingFiles.Any(f =>
                    f.Reason == NotMatchingReason.Corrupted &&
                    f.RelativePath.Equals(filePath, StringComparison.OrdinalIgnoreCase));

                bool isMissing = report.MissingFiles.Any(f =>
                    f.Equals(filePath, StringComparison.OrdinalIgnoreCase));

                bool hasError = report.ErrorFiles.Any(f =>
                    f.RelativePath.Equals(filePath, StringComparison.OrdinalIgnoreCase));

                if (isCorrupted)
                    corruptedSerials.Add(report.VolumeSerial);
                else if (!isMissing && !hasError)
                    goodSerials.Add(report.VolumeSerial);
                // Missing or unreadable: can't use as repair source.
            }

            if (goodSerials.Count == 0)
            {
                results.Add(new RepairResult(filePath, RepairStatus.Unrecoverable, null, null,
                    "No intact copy found on any online drive."));
                filesProcessed++;
                continue;
            }

            // Pick the first available good source.
            VolumeIdentity? sourceVolId = null;
            VolumeEntry? sourceVolumeEntry = null;
            string? sourceSerial = null;

            foreach (var serial in goodSerials)
            {
                var volId = _onlineVolumes.FirstOrDefault(v =>
                    v.SerialNumber.Equals(serial, StringComparison.OrdinalIgnoreCase));
                var volEntry = _hashFile.Volumes.FirstOrDefault(v =>
                    v.SerialNumber.Equals(serial, StringComparison.OrdinalIgnoreCase));
                if (volId != null && volEntry != null)
                {
                    sourceSerial = serial;
                    sourceVolId = volId;
                    sourceVolumeEntry = volEntry;
                    break;
                }
            }

            if (sourceSerial == null)
            {
                results.Add(new RepairResult(filePath, RepairStatus.Unrecoverable, null, null,
                    "Good source drive went offline."));
                filesProcessed++;
                continue;
            }

            var sourceScanRoot = sourceVolumeEntry!.GetFullScanPath(sourceVolId!.RootPath);
            var sourceAbsPath = BuildAbsPath(sourceScanRoot, filePath);

            foreach (var targetSerial in corruptedSerials)
            {
                ct.ThrowIfCancellationRequested();

                var targetVolId = _onlineVolumes.FirstOrDefault(v =>
                    v.SerialNumber.Equals(targetSerial, StringComparison.OrdinalIgnoreCase));
                var targetVolumeEntry = _hashFile.Volumes.FirstOrDefault(v =>
                    v.SerialNumber.Equals(targetSerial, StringComparison.OrdinalIgnoreCase));

                if (targetVolId == null || targetVolumeEntry == null)
                {
                    results.Add(new RepairResult(filePath, RepairStatus.Error,
                        sourceSerial, targetSerial, "Target drive went offline."));
                    continue;
                }

                // Optical discs are always read-only.
                try
                {
                    var driveInfo = new DriveInfo(targetVolId.RootPath);
                    if (driveInfo.DriveType == DriveType.CDRom)
                    {
                        results.Add(new RepairResult(filePath, RepairStatus.ReadOnlySkipped,
                            sourceSerial, targetSerial, "Drive is read-only (optical disc)."));
                        continue;
                    }
                }
                catch { /* fall through and let the copy attempt surface any error */ }

                var targetScanRoot = targetVolumeEntry.GetFullScanPath(targetVolId.RootPath);
                var targetAbsPath = BuildAbsPath(targetScanRoot, filePath);
                var tempPath = targetAbsPath + ".repair_tmp";

                try
                {
                    File.Copy(sourceAbsPath, tempPath, overwrite: true);

                    string computedHash;
                    using (var stream = new FileStream(
                        tempPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true))
                    {
                        computedHash = await hasher.ComputeHashAsync(stream, null, ct);
                    }

                    if (computedHash.Equals(storedEntry.Hash, StringComparison.OrdinalIgnoreCase))
                    {
                        File.Move(tempPath, targetAbsPath, overwrite: true);
                        results.Add(new RepairResult(filePath, RepairStatus.Repaired,
                            sourceSerial, targetSerial, null));
                    }
                    else
                    {
                        TryDelete(tempPath);
                        results.Add(new RepairResult(filePath, RepairStatus.VerificationFailed,
                            sourceSerial, targetSerial, "Hash mismatch after copy — copy may itself be corrupted."));
                    }
                }
                catch (UnauthorizedAccessException ex)
                {
                    TryDelete(tempPath);
                    results.Add(new RepairResult(filePath, RepairStatus.ReadOnlySkipped,
                        sourceSerial, targetSerial, ex.Message));
                }
                catch (IOException ex) when (IsWriteProtected(ex))
                {
                    TryDelete(tempPath);
                    results.Add(new RepairResult(filePath, RepairStatus.ReadOnlySkipped,
                        sourceSerial, targetSerial, ex.Message));
                }
                catch (OperationCanceledException)
                {
                    TryDelete(tempPath);
                    throw;
                }
                catch (Exception ex)
                {
                    TryDelete(tempPath);
                    results.Add(new RepairResult(filePath, RepairStatus.Error,
                        sourceSerial, targetSerial, ex.Message));
                }
            }

            filesProcessed++;
        }

        progress?.Report(new RepairProgress("Complete", "", filesTotal, filesTotal));

        return new RepairReport
        {
            Timestamp = DateTime.UtcNow,
            MediaName = _hashFile.MediaName,
            HashFilePath = _hashFile.FilePath,
            Results = results
        };
    }

    private static string BuildAbsPath(string scanRoot, string relativePath) =>
        Path.Combine(scanRoot.TrimEnd('\\'), relativePath.TrimStart('\\'));

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    // ERROR_WRITE_PROTECT = Win32 error 19 → HRESULT 0x80070013
    private static bool IsWriteProtected(IOException ex) =>
        ex.HResult == unchecked((int)0x80070013);
}
