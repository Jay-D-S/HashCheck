namespace HashCheck.Core.Scanning;

/// <summary>Snapshot of scan or hash progress reported to the UI on each file.</summary>
public record ScanProgress(
    int FilesProcessed,
    int FilesTotal,
    long BytesProcessed,
    long BytesTotal,
    string CurrentFile,
    TimeSpan Elapsed,
    TimeSpan? Eta);
