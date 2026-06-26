namespace HashCheck.Core.Repair;

public class RepairReport
{
    public DateTime Timestamp { get; init; }
    public string MediaName { get; init; } = "";
    public string HashFilePath { get; init; } = "";
    public List<RepairResult> Results { get; init; } = new();

    public int Repaired => Results.Count(r => r.Status == RepairStatus.Repaired);
    public int Unrecoverable => Results.Count(r => r.Status == RepairStatus.Unrecoverable);
    public int ReadOnlySkipped => Results.Count(r => r.Status == RepairStatus.ReadOnlySkipped);
    public int Failed => Results.Count(r => r.Status is RepairStatus.VerificationFailed or RepairStatus.Error);
    public bool AnyCorrupted => Results.Count > 0;
    public bool HasIssues => Unrecoverable > 0 || Failed > 0;
}
