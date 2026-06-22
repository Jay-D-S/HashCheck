using System.Text.Json;

namespace HashCheck.Core.Settings;

public sealed class SettingsStore
{
    private static readonly string SettingsPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "HashCheck", "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public AppSettings Current { get; private set; } = new();

    public void Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                Current = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new();
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
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(Current, JsonOptions));
    }

    public void AddKnownHashFile(string filePath)
    {
        if (!Current.KnownHashFiles.Contains(filePath, StringComparer.OrdinalIgnoreCase))
        {
            Current.KnownHashFiles.Add(filePath);
            Save();
        }
    }

    public void RemoveKnownHashFile(string filePath)
    {
        Current.KnownHashFiles.RemoveAll(p =>
            string.Equals(p, filePath, StringComparison.OrdinalIgnoreCase));
        Save();
    }
}
