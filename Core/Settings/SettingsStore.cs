using System.Text.Json;

namespace HashCheck.Core.Settings;

/// <summary>Loads, saves, and manages the live <see cref="AppSettings"/> instance backed by <c>%APPDATA%\HashCheck\settings.json</c>.</summary>
public sealed class SettingsStore
{
    private static readonly string SettingsPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "HashCheck", "settings.json");

    /// <summary>The currently active settings. Always non-null; falls back to defaults if the file is missing or corrupt.</summary>
    public AppSettings Current { get; private set; } = new();

    /// <summary>Reads settings from disk; silently uses defaults if the file is absent or cannot be parsed.</summary>
    public void Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                Current = JsonSerializer.Deserialize(json, AppSettingsContext.Default.AppSettings) ?? new();
            }
        }
        catch
        {
            Current = new AppSettings();
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(Current, AppSettingsContext.Default.AppSettings));
    }

    /// <summary>Adds <paramref name="filePath"/> to <see cref="AppSettings.KnownHashFiles"/> if not already present, then saves.</summary>
    public void AddKnownHashFile(string filePath)
    {
        if (!Current.KnownHashFiles.Contains(filePath, StringComparer.OrdinalIgnoreCase))
        {
            Current.KnownHashFiles.Add(filePath);
            Save();
        }
    }

    /// <summary>Removes <paramref name="filePath"/> from <see cref="AppSettings.KnownHashFiles"/> (case-insensitive) and saves.</summary>
    public void RemoveKnownHashFile(string filePath)
    {
        Current.KnownHashFiles.RemoveAll(p =>
            string.Equals(p, filePath, StringComparison.OrdinalIgnoreCase));
        Save();
    }
}
