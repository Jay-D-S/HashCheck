namespace HashCheck.Core.Validation;

/// <summary>Conclusion for one file after comparing it across all available drives.</summary>
public enum CrossDriveFileStatus
{
    /// <summary>At least one other drive matches the stored hash — the new mirror's copy is corrupted.</summary>
    NewMirrorCorrupted,
    /// <summary>All other readable drives agree on a different hash — the original hash set may have been created from a corrupted source.</summary>
    StoredHashSuspect,
    /// <summary>No clear majority — every available copy has different data or there are not enough drives to vote.</summary>
    Indeterminate,
}

/// <summary>Cross-drive hash comparison result for a single file.</summary>
public record CrossDriveFileResult(
    string RelativePath,
    string StoredHash,
    IReadOnlyDictionary<string, string?> OtherDriveHashes,   // serial → computed hash (null = unreadable)
    CrossDriveFileStatus Status
);

/// <summary>Summary of the cross-drive majority-vote analysis run after a new mirror is registered.</summary>
public class CrossDriveAnalysisReport
{
    public List<CrossDriveFileResult> Results { get; init; } = new();
    public int TotalRegisteredVolumes { get; init; }
    public int OtherOnlineVolumeCount { get; init; }

    public int NewMirrorCorrupted => Results.Count(r => r.Status == CrossDriveFileStatus.NewMirrorCorrupted);
    public int StoredHashSuspect   => Results.Count(r => r.Status == CrossDriveFileStatus.StoredHashSuspect);
    public int Indeterminate       => Results.Count(r => r.Status == CrossDriveFileStatus.Indeterminate);
}
