namespace HashCheck.Core.HashFile;

public record VolumeEntry(string SerialNumber, string Label, long TotalBytes, DateTime DateAdded, string ScanSubPath = @"\")
{
    // Returns the full scan path by combining the volume's mount root with its scan sub-path.
    public string GetFullScanPath(string volumeRootPath)
    {
        var root = volumeRootPath.TrimEnd('\\');
        var sub  = ScanSubPath.TrimStart('\\');
        return string.IsNullOrEmpty(sub) ? volumeRootPath : root + '\\' + sub;
    }
}

public class HashFileData
{
    public string FilePath { get; set; } = "";

    // [META]
    public string Description { get; set; } = "";
    public string MediaName { get; set; } = "";   // user-defined group name
    public HashAlgorithmType Algorithm { get; set; } = HashAlgorithmType.XxHash3;
    public int ReminderDays { get; set; } = 180;
    public FilterMode FilterMode { get; set; } = FilterMode.Exclude;
    public bool Autoscan { get; set; } = false;
    public DateTime DateCreated { get; set; }
    public DateTime DateModified { get; set; }

    // [VOLUMES] — one entry per registered media copy in this group
    public List<VolumeEntry> Volumes { get; set; } = new();

    // Convenience: primary (first) volume — used by code that predates groups
    public string SerialNumber  => Volumes.Count > 0 ? Volumes[0].SerialNumber : "";
    public string VolumeLabel   => Volumes.Count > 0 ? Volumes[0].Label        : "";
    public long MediaTotalBytes => Volumes.Count > 0 ? Volumes[0].TotalBytes   : 0;

    // [FILTERS]
    public List<string> Filters { get; set; } = new();

    // [VALIDATIONS]
    public List<ValidationEntry> Validations { get; set; } = new();

    // [PATHS]
    public List<string> Paths { get; set; } = new();

    // [FILES]
    public List<FileEntry> Files { get; set; } = new();

    public DateTime? LastValidated => Validations.Count > 0 ? Validations[^1].Timestamp : null;
    public DateTime DueDate => (LastValidated ?? DateCreated).AddDays(ReminderDays);
    public bool IsOverdue => DateTime.UtcNow >= DueDate;
    public string StatusText => Validations.Count == 0 ? "Never verified"
        : IsOverdue ? "Overdue"
        : "OK";
}

public record FileEntry(string RelativePath, string Hash, long SizeBytes, DateTime ModifiedUtc);

public record ValidationEntry(
    DateTime Timestamp,
    string Status,
    string VolumeSerial,   // which copy of the group was validated
    int Files,
    long Bytes,
    int Matching,
    int NotMatching,
    int Missing,
    int Errors);
