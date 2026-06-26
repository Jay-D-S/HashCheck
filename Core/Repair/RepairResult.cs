namespace HashCheck.Core.Repair;

public enum RepairStatus
{
    Repaired,
    Unrecoverable,
    ReadOnlySkipped,
    VerificationFailed,
    Error
}

public record RepairResult(
    string RelativePath,
    RepairStatus Status,
    string? SourceSerial,
    string? TargetSerial,
    string? ErrorMessage
);
