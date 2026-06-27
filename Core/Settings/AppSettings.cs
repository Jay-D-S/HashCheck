using System.Text.Json.Serialization;

namespace HashCheck.Core.Settings;

[JsonSerializable(typeof(AppSettings))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal sealed partial class AppSettingsContext : JsonSerializerContext { }

/// <summary>Application-wide settings persisted to <c>%APPDATA%\HashCheck\settings.json</c>. JSON-serialized by <see cref="SettingsStore"/>.</summary>
public class AppSettings
{
    /// <summary>Default output folder for new <c>.hash</c> files (centralised on the PC, never on media).</summary>
    public string DefaultHashStoragePath { get; set; } = "";
    public int DefaultReminderDays { get; set; } = 180;
    public HashAlgorithmType DefaultAlgorithm { get; set; } = HashAlgorithmType.XxHash3;
    public bool DefaultAutoscan { get; set; } = false;
    /// <summary>When <c>true</c>, shows the autoscan delay prompt each time a tracked volume is mounted.</summary>
    public bool AutoscanPromptOnAttach { get; set; } = true;
    public bool RunAtLogin { get; set; } = false;
    /// <summary>When <c>true</c>, ValidatePage runs all volume rows concurrently; set to <c>false</c> for single-drive systems to avoid disk contention.</summary>
    public bool RunValidationsConcurrently { get; set; } = true;
    /// <summary>Index into the donation nag messages array; advances each launch and wraps around.</summary>
    public int NagMessageIndex { get; set; } = 0;
    /// <summary>Set to <c>true</c> when the user clicks Donate; permanently suppresses the nag from the window title.</summary>
    public bool HideDonationNag { get; set; } = false;
    /// <summary>Extra folders scanned for <c>.hash</c> files on the dashboard (supplement to <see cref="KnownHashFiles"/>).</summary>
    public List<string> KnownHashLocations { get; set; } = new();
    /// <summary>Explicit list of known <c>.hash</c> file paths; maintained automatically as files are created or removed.</summary>
    public List<string> KnownHashFiles { get; set; } = new();

    // Not persisted — derived from FilterEngine; only used to initialise new hash sets in the UI.
    [JsonIgnore]
    public FilterMode DefaultFilterMode { get; set; } = FilterMode.Exclude;

    [JsonIgnore]
    public IReadOnlyList<string> DefaultExcludePatterns =>
        Core.Scanning.FilterEngine.DefaultExcludePatterns;
}
