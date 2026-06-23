namespace HashCheck.Core.Validation;

/// <summary>Why a file's hash did not match the stored value.</summary>
public enum NotMatchingReason
{
    /// <summary>Hash differs but size and mtime are unchanged — likely silent bit-rot.</summary>
    Corrupted,
    /// <summary>Hash differs and size or mtime also changed — likely a legitimate edit.</summary>
    Modified
}

/// <summary>A file whose computed hash did not match the stored hash.</summary>
public record NotMatchingFile(string RelativePath, NotMatchingReason Reason);
/// <summary>A file that could not be read during validation.</summary>
public record ErrorFile(string RelativePath, string ErrorMessage);

/// <summary>Complete result of one validation run, including per-category file lists.</summary>
public class ValidationReport
{
    public DateTime Timestamp { get; set; }
    public string MediaName { get; set; } = "";
    public string HashFilePath { get; set; } = "";
    /// <summary>Serial number of the volume that was validated (e.g. <c>1CBA-E2C8</c>).</summary>
    public string VolumeSerial { get; set; } = "";
    /// <summary>Absolute path of the folder that was scanned (the volume scan root).</summary>
    public string ScanRoot { get; set; } = "";

    public int TotalFilesFound { get; set; }
    public int TotalDirectoriesFound { get; set; }
    public long TotalBytesFound { get; set; }
    public int TotalFilesInHashSet { get; set; }

    public int TotalMatching { get; set; }
    public int TotalNotMatching => TotalCorrupted + TotalModified;
    public int TotalCorrupted { get; set; }
    public int TotalModified { get; set; }
    public int TotalMissing { get; set; }
    public int TotalErrors { get; set; }
    public int TotalNew { get; set; }

    public List<string> MissingFiles { get; set; } = new();
    public List<NotMatchingFile> NotMatchingFiles { get; set; } = new();
    public List<ErrorFile> ErrorFiles { get; set; } = new();
    public List<string> NewFiles { get; set; } = new();

    /// <summary><c>true</c> only when there are no corrupted, modified, missing, or error files.</summary>
    public bool Passed => TotalNotMatching == 0 && TotalMissing == 0 && TotalErrors == 0;
    public string Status => Passed ? "PASS" : "FAIL";

    /// <summary>Populated when autoscan ran immediately after this validation (see <see cref="HashSetService.ValidateAsync"/>).</summary>
    public Scanning.AutoscanResult? AutoscanResult { get; set; }
}
