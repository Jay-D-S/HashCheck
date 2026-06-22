using System.Text.Json.Serialization;

namespace HashCheck.Core.Settings;

public class AppSettings
{
    public string DefaultHashStoragePath { get; set; } = "";
    public int DefaultReminderDays { get; set; } = 180;
    public HashAlgorithmType DefaultAlgorithm { get; set; } = HashAlgorithmType.XxHash3;
    public bool DefaultAutoscan { get; set; } = false;
    public bool AutoscanPromptOnAttach { get; set; } = true;
    public bool RunAtLogin { get; set; } = false;
    public bool RunValidationsConcurrently { get; set; } = true;
    public int NagMessageIndex { get; set; } = 0;
    public bool HideDonationNag { get; set; } = false;
    public List<string> KnownHashLocations { get; set; } = new();
    public List<string> KnownHashFiles { get; set; } = new();

    [JsonIgnore]
    public FilterMode DefaultFilterMode { get; set; } = FilterMode.Exclude;

    [JsonIgnore]
    public IReadOnlyList<string> DefaultExcludePatterns =>
        Core.Scanning.FilterEngine.DefaultExcludePatterns;
}
