namespace HashCheck.Core.Validation;

public enum NotMatchingReason { Corrupted, Modified }

public record NotMatchingFile(string RelativePath, NotMatchingReason Reason);
public record ErrorFile(string RelativePath, string ErrorMessage);

public class ValidationReport
{
    public DateTime Timestamp { get; set; }
    public string MediaName { get; set; } = "";
    public string HashFilePath { get; set; } = "";

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

    public bool Passed => TotalNotMatching == 0 && TotalMissing == 0 && TotalErrors == 0;
    public string Status => Passed ? "PASS" : "FAIL";

    // Populated when autoscan ran after this validation
    public Scanning.AutoscanResult? AutoscanResult { get; set; }
}
