namespace HashCheck.Core.Volumes;

/// <summary>Immutable snapshot of a mounted volume's identity. <see cref="SerialNumber"/> is always used for identification — <see cref="RootPath"/> changes between sessions as drive letters are re-assigned.</summary>
public record VolumeIdentity(
    string SerialNumber,
    string Label,
    long TotalBytes,
    string RootPath);
