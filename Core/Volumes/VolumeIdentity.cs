namespace HashCheck.Core.Volumes;

public record VolumeIdentity(
    string SerialNumber,
    string Label,
    long TotalBytes,
    string RootPath);
