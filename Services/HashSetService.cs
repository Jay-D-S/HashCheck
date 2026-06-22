using HashCheck.Core;
using HashCheck.Core.HashFile;
using HashCheck.Core.Hashing;
using HashCheck.Core.Scanning;
using HashCheck.Core.Settings;
using HashCheck.Core.Validation;
using HashCheck.Core.Volumes;

namespace HashCheck.Services;

public record CreateOptions(
    string MediaRoot,
    string SerialNumber,
    string VolumeLabel,
    long MediaTotalBytes,
    List<string> ScopePaths,
    string Description,
    HashAlgorithmType Algorithm,
    int ReminderDays,
    FilterMode FilterMode,
    List<string> Filters,
    string StoragePath,
    bool Autoscan = false);

public sealed class HashSetService
{
    private readonly SettingsStore _settings;

    public HashSetService(SettingsStore settings)
    {
        _settings = settings;
    }

    public async Task<HashFileData> CreateAsync(
        CreateOptions options,
        IProgress<ScanProgress>? progress,
        CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var filter = new FilterEngine(options.FilterMode, options.Filters);
        var scanner = new FileScanner(filter);
        var hasher = HasherFactory.Create(options.Algorithm);

        // First pass: count files/bytes for progress
        var allItems = scanner.Scan(options.MediaRoot, options.ScopePaths, ct).ToList();
        var allDirs = scanner.GetAllDirectories(options.MediaRoot, options.ScopePaths, ct).ToList();

        int filesTotal = allItems.Count;
        long bytesTotal = allItems.Sum(i => i.Info.Length);
        int filesProcessed = 0;
        long bytesProcessed = 0;
        var startTime = DateTime.UtcNow;

        var files = new List<FileEntry>(allItems.Count);

        foreach (var item in allItems)
        {
            ct.ThrowIfCancellationRequested();

            var elapsed = DateTime.UtcNow - startTime;
            TimeSpan? eta = bytesProcessed > 0 && bytesTotal > 0
                ? TimeSpan.FromSeconds(elapsed.TotalSeconds * (bytesTotal - bytesProcessed) / bytesProcessed)
                : null;

            progress?.Report(new ScanProgress(
                filesProcessed, filesTotal,
                bytesProcessed, bytesTotal,
                item.RelativePath, elapsed, eta));

            string hash;
            try
            {
                using var stream = new FileStream(
                    item.Info.FullName, FileMode.Open, FileAccess.Read,
                    FileShare.Read, 4096, useAsync: true);

                var bytesReporter = new Progress<long>(n => bytesProcessed += n);
                hash = await hasher.ComputeHashAsync(stream, bytesReporter, ct);
            }
            catch
            {
                filesProcessed++;
                continue;
            }

            files.Add(new FileEntry(
                item.RelativePath,
                hash,
                item.Info.Length,
                item.Info.LastWriteTimeUtc));

            filesProcessed++;
        }

        var mediaName = string.IsNullOrWhiteSpace(options.VolumeLabel)
            ? options.SerialNumber
            : options.VolumeLabel;

        var data = new HashFileData
        {
            Description = options.Description,
            MediaName = mediaName,
            Algorithm = options.Algorithm,
            ReminderDays = options.ReminderDays,
            FilterMode = options.FilterMode,
            Autoscan = options.Autoscan,
            Filters = new List<string>(options.Filters),
            DateCreated = now,
            DateModified = now,
            Volumes = new List<VolumeEntry>
            {
                new(options.SerialNumber, options.VolumeLabel, options.MediaTotalBytes, now,
                    ComputeScanSubPath(options.MediaRoot))
            },
            Paths = allDirs.OrderBy(d => d, StringComparer.OrdinalIgnoreCase).ToList(),
            Files = files
        };

        // Build a unique output file path — timestamp prevents conflicts when
        // the same media has more than one hash set
        var dateStamp = now.ToString("yyyyMMdd_HHmmss");
        var fileName = SanitizeFileName(mediaName) + "_" + dateStamp + ".hash";
        var outPath = Path.Combine(options.StoragePath, fileName);
        data.FilePath = outPath;

        await HashFileWriter.WriteAsync(data, outPath);
        _settings.AddKnownHashFile(outPath);

        return data;
    }

    public async Task<ValidationReport> ValidateAsync(
        string hashFilePath,
        string mediaRoot,
        string volumeSerial,
        IProgress<ScanProgress>? progress,
        CancellationToken ct,
        PauseToken? pauseToken = null)
    {
        var hashFile = await HashFileReader.ReadAsync(hashFilePath, verifyIntegrity: true);
        var engine = new ValidationEngine();
        var report = await engine.ValidateAsync(hashFile, mediaRoot, progress, ct, pauseToken);

        // Append validation record — note which copy of the group was validated
        hashFile.Validations.Add(new ValidationEntry(
            report.Timestamp,
            report.Status,
            volumeSerial,
            report.TotalFilesFound,
            report.TotalBytesFound,
            report.TotalMatching,
            report.TotalNotMatching,
            report.TotalMissing,
            report.TotalErrors));

        // Run autoscan after validation if enabled
        if (hashFile.Autoscan)
        {
            var autoscanEngine = new AutoscanEngine();
            var autoscanResult = await autoscanEngine.ScanAsync(hashFile, mediaRoot, progress, ct);
            report.AutoscanResult = autoscanResult;

            if (autoscanResult.AddedFiles.Count > 0 || autoscanResult.AddedPaths.Count > 0)
            {
                hashFile.Files.AddRange(autoscanResult.AddedFiles);
                foreach (var dir in autoscanResult.AddedPaths)
                {
                    if (!hashFile.Paths.Contains(dir, StringComparer.OrdinalIgnoreCase))
                        hashFile.Paths.Add(dir);
                }
                hashFile.DateModified = DateTime.UtcNow;
            }
        }

        await HashFileWriter.WriteAsync(hashFile, hashFilePath);
        return report;
    }

    public async Task<AutoscanResult> AutoscanAsync(
        string hashFilePath,
        string mediaRoot,
        IProgress<ScanProgress>? progress,
        CancellationToken ct)
    {
        var hashFile = await HashFileReader.ReadAsync(hashFilePath, verifyIntegrity: false);
        var autoscanEngine = new AutoscanEngine();
        var result = await autoscanEngine.ScanAsync(hashFile, mediaRoot, progress, ct);

        if (result.AddedFiles.Count > 0 || result.AddedPaths.Count > 0)
        {
            hashFile.Files.AddRange(result.AddedFiles);
            foreach (var dir in result.AddedPaths)
            {
                if (!hashFile.Paths.Contains(dir, StringComparer.OrdinalIgnoreCase))
                    hashFile.Paths.Add(dir);
            }
            hashFile.DateModified = DateTime.UtcNow;
            await HashFileWriter.WriteAsync(hashFile, hashFilePath);
        }

        return result;
    }

    public async Task<HashFileData> ReCreateAsync(
        string hashFilePath,
        string mediaRoot,
        IProgress<ScanProgress>? progress,
        CancellationToken ct)
    {
        var existing = await HashFileReader.ReadAsync(hashFilePath, verifyIntegrity: false);

        // Back up existing file
        var backupName = Path.GetFileNameWithoutExtension(hashFilePath)
            + "." + DateTime.Now.ToString("yyyyMMdd-HHmm")
            + ".hash.bak";
        var backupPath = Path.Combine(Path.GetDirectoryName(hashFilePath)!, backupName);
        File.Copy(hashFilePath, backupPath, overwrite: false);

        // Expand the provided drive root with the primary volume's scan sub-path so
        // re-create scans the same subfolder as the original baseline.
        var primaryVol   = existing.Volumes.FirstOrDefault();
        var scanMediaRoot = primaryVol != null ? primaryVol.GetFullScanPath(mediaRoot) : mediaRoot;

        var options = new CreateOptions(
            scanMediaRoot,
            existing.SerialNumber,
            existing.VolumeLabel,
            existing.MediaTotalBytes,
            existing.Paths.Where(p => IsTopLevelScope(p, existing.Paths)).ToList(),
            existing.Description,
            existing.Algorithm,
            existing.ReminderDays,
            existing.FilterMode,
            existing.Filters,
            Path.GetDirectoryName(hashFilePath)!,
            existing.Autoscan);

        var newData = await CreateAsync(options, progress, ct);

        // CreateAsync wrote a timestamped temp file and registered it — clean both up
        // before writing to the original path so no duplicate appears in the dashboard.
        var tempPath = newData.FilePath;
        _settings.RemoveKnownHashFile(tempPath);
        try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }

        // Restore all registered volumes from the original group — re-create only scans the
        // primary volume but the other copies in the group are still valid members.
        newData.Volumes = existing.Volumes.ToList();
        newData.FilePath = hashFilePath;
        await HashFileWriter.WriteAsync(newData, hashFilePath);
        return newData;
    }

    public async Task UpdateVolumeScanPathAsync(string hashFilePath, string serial, string scanSubPath)
    {
        var hashFile = await HashFileReader.ReadAsync(hashFilePath, verifyIntegrity: false);
        var idx = hashFile.Volumes.FindIndex(v =>
            string.Equals(v.SerialNumber, serial, StringComparison.OrdinalIgnoreCase));
        if (idx < 0) return;
        hashFile.Volumes[idx] = hashFile.Volumes[idx] with { ScanSubPath = scanSubPath };
        await HashFileWriter.WriteAsync(hashFile, hashFilePath);
    }

    public async Task AddVolumeAsync(string hashFilePath, string serial, string label, long totalBytes,
        string scanSubPath = @"\")
    {
        var hashFile = await HashFileReader.ReadAsync(hashFilePath, verifyIntegrity: false);
        if (hashFile.Volumes.Any(v => v.SerialNumber.Equals(serial, StringComparison.OrdinalIgnoreCase)))
            return;
        hashFile.Volumes.Add(new VolumeEntry(serial, label, totalBytes, DateTime.UtcNow, scanSubPath));
        await HashFileWriter.WriteAsync(hashFile, hashFilePath);
    }

    public Task<IReadOnlyList<HashFileData>> LoadAllKnownAsync() =>
        LoadAllKnownWithDiagnosticsAsync().ContinueWith(t => t.Result.Data);

    public async Task<(IReadOnlyList<HashFileData> Data, string Diagnostics)> LoadAllKnownWithDiagnosticsAsync()
    {
        var result = new List<HashFileData>();
        var errors = new List<string>();
        int trackedLoaded = 0, trackedMissing = 0;

        foreach (var path in _settings.Current.KnownHashFiles.ToList())
        {
            if (!File.Exists(path))
            {
                _settings.RemoveKnownHashFile(path);
                trackedMissing++;
                continue;
            }
            try
            {
                var data = await HashFileReader.ReadAsync(path, verifyIntegrity: false);
                result.Add(data);
                trackedLoaded++;
            }
            catch (Exception ex)
            {
                errors.Add($"Parse error: {Path.GetFileName(path)}: {ex.Message}");
            }
        }

        // Also scan the default storage path (to auto-discover files created by the app)
        var scanDirs = new HashSet<string>(_settings.Current.KnownHashLocations, StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(_settings.Current.DefaultHashStoragePath))
            scanDirs.Add(_settings.Current.DefaultHashStoragePath);

        int dirFilesFound = 0, dirFilesLoaded = 0;
        foreach (var dir in scanDirs)
        {
            if (!Directory.Exists(dir))
            {
                errors.Add($"Folder not found: {dir}");
                continue;
            }
            try
            {
                foreach (var file in Directory.EnumerateFiles(dir, "*.hash"))
                {
                    dirFilesFound++;
                    if (result.Any(r => string.Equals(r.FilePath, file, StringComparison.OrdinalIgnoreCase)))
                        continue;
                    try
                    {
                        var data = await HashFileReader.ReadAsync(file, verifyIntegrity: false);
                        result.Add(data);
                        dirFilesLoaded++;
                        _settings.AddKnownHashFile(file);
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"Parse error: {Path.GetFileName(file)}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                errors.Add($"Scan error in {dir}: {ex.Message}");
            }
        }

        var diag = new System.Text.StringBuilder();
        diag.AppendLine($"Tracked files: {trackedLoaded} loaded, {trackedMissing} missing");
        if (scanDirs.Count > 0)
            diag.AppendLine($"Scanned {scanDirs.Count} folder(s), found {dirFilesFound} .hash file(s), loaded {dirFilesLoaded} new");
        else
            diag.AppendLine("No folders configured to scan. Use 'Add Folder…' or Settings.");
        foreach (var e in errors)
            diag.AppendLine(e);

        return (result, diag.ToString().TrimEnd());
    }

    public void RemoveFromTracking(string filePath)
    {
        _settings.RemoveKnownHashFile(filePath);
    }

    public void RemoveAndDeleteHashFile(string filePath)
    {
        _settings.RemoveKnownHashFile(filePath);
        if (File.Exists(filePath))
            File.Delete(filePath);
    }

    private static bool IsTopLevelScope(string path, IEnumerable<string> allPaths)
    {
        // A path is top-level if no other path is a proper prefix of it
        return !allPaths.Any(other =>
            !string.Equals(other, path, StringComparison.OrdinalIgnoreCase)
            && path.StartsWith(other.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase));
    }

    private static string SanitizeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name;
    }

    // Derives the scan sub-path (relative to volume root) from a full media root path.
    // e.g. "D:\myphotos\2026" → "\myphotos\2026";  "D:\" → "\"
    private static string ComputeScanSubPath(string mediaRoot)
    {
        var volumeRoot = Path.GetPathRoot(mediaRoot);
        if (string.IsNullOrEmpty(volumeRoot)) return @"\";
        var root = volumeRoot.TrimEnd('\\');
        var sub  = mediaRoot.StartsWith(root, StringComparison.OrdinalIgnoreCase)
            ? mediaRoot.Substring(root.Length).TrimEnd('\\')
            : "";
        return string.IsNullOrEmpty(sub) ? @"\" : sub;
    }
}
