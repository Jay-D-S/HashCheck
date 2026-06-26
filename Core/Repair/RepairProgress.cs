namespace HashCheck.Core.Repair;

public record RepairProgress(
    string Phase,
    string CurrentFile,
    int FilesProcessed,
    int FilesTotal
);
