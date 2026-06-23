namespace HashCheck.Core.HashFile;

/// <summary>One registered copy of the media in a hash group. <c>ScanSubPath</c> is relative to the drive root (e.g. <c>\_PHOTOS\2026</c> or <c>\</c>).</summary>
public record VolumeEntry(string SerialNumber, string Label, long TotalBytes, DateTime DateAdded, string ScanSubPath = @"\")
{
    /// <summary>Combines <paramref name="volumeRootPath"/> with <see cref="ScanSubPath"/> to produce the absolute scan root for this volume.</summary>
    public string GetFullScanPath(string volumeRootPath)
    {
        var root = volumeRootPath.TrimEnd('\\');
        var sub = ScanSubPath.TrimStart('\\');
        return string.IsNullOrEmpty(sub) ? volumeRootPath : root + '\\' + sub;
    }
}

/// <summary>In-memory representation of a <c>.hash</c> file. Mirrors the on-disk section order exactly; do not add sections without updating <see cref="HashFileReader"/> and <see cref="HashFileWriter"/>.</summary>
public class HashFileData
{
    /// <summary>Absolute path to the <c>.hash</c> file on the PC (never on media).</summary>
    public string FilePath { get; set; } = "";

    // [META]
    public string Description { get; set; } = "";
    /// <summary>User-defined group name, usually the primary volume label.</summary>
    public string MediaName { get; set; } = "";
    public HashAlgorithmType Algorithm { get; set; } = HashAlgorithmType.XxHash3;
    public int ReminderDays { get; set; } = 180;
    public FilterMode FilterMode { get; set; } = FilterMode.Exclude;
    public bool Autoscan { get; set; } = false;
    public DateTime DateCreated { get; set; }
    public DateTime DateModified { get; set; }

    // [VOLUMES] — one entry per registered media copy in this group
    public List<VolumeEntry> Volumes { get; set; } = new();

    // Convenience: primary (first) volume — used by code that predates groups
    public string SerialNumber => Volumes.Count > 0 ? Volumes[0].SerialNumber : "";
    public string VolumeLabel => Volumes.Count > 0 ? Volumes[0].Label : "";
    public long MediaTotalBytes => Volumes.Count > 0 ? Volumes[0].TotalBytes : 0;

    // [FILTERS]
    public List<string> Filters { get; set; } = new();

    // [VALIDATIONS]
    public List<ValidationEntry> Validations { get; set; } = new();

    // [PATHS] — fast scope index; always starts with "\" (the scan root)
    public List<string> Paths { get; set; } = new();

    // [FILES]
    public List<FileEntry> Files { get; set; } = new();

    /// <summary>Timestamp of the most recent validation entry, or null if never validated.</summary>
    public DateTime? LastValidated => Validations.Count > 0 ? Validations[^1].Timestamp : null;
    /// <summary>Date after which a new validation is due, based on <see cref="LastValidated"/> or <see cref="DateCreated"/>.</summary>
    public DateTime DueDate => (LastValidated ?? DateCreated).AddDays(ReminderDays);
    public bool IsOverdue => DateTime.UtcNow >= DueDate;
    public string StatusText => Validations.Count == 0 ? "Never verified"
        : IsOverdue ? "Overdue"
        : "OK";
}

/// <summary>One file entry from the <c>[FILES]</c> section. <see cref="RelativePath"/> is relative to the scan root, with a leading backslash.</summary>
public record FileEntry(string RelativePath, string Hash, long SizeBytes, DateTime ModifiedUtc);

/// <summary>One row from the <c>[VALIDATIONS]</c> section, recording the outcome of a single validation run.</summary>
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
