namespace HashCheck.Core.Scanning;

public record ScanProgress(
    int FilesProcessed,
    int FilesTotal,
    long BytesProcessed,
    long BytesTotal,
    string CurrentFile,
    TimeSpan Elapsed,
    TimeSpan? Eta);
