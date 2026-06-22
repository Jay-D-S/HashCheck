using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HashCheck.Core.Hashing;
using HashCheck.Core.Settings;

namespace HashCheck.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly SettingsStore _store;

    [ObservableProperty] private string _defaultHashStoragePath;
    [ObservableProperty] private int _defaultReminderDays;
    [ObservableProperty] private int _defaultAlgorithmIndex;
    [ObservableProperty] private bool _defaultAutoscan;
    [ObservableProperty] private bool _autoscanPromptOnAttach;
    [ObservableProperty] private bool _runAtLogin;
    [ObservableProperty] private bool _runValidationsConcurrently;
    [ObservableProperty] private string _saveStatus = "";

    public ObservableCollection<string> KnownHashLocations { get; } = new();
    public string[] AlgorithmNames => HasherFactory.AlgorithmDisplayNames;

    public SettingsViewModel(SettingsStore store)
    {
        _store = store;
        var s = store.Current;
        _defaultHashStoragePath = s.DefaultHashStoragePath;
        _defaultReminderDays = s.DefaultReminderDays;
        _defaultAlgorithmIndex = HasherFactory.ToDisplayIndex(s.DefaultAlgorithm);
        _defaultAutoscan = s.DefaultAutoscan;
        _autoscanPromptOnAttach = s.AutoscanPromptOnAttach;
        _runAtLogin = s.RunAtLogin;
        _runValidationsConcurrently = s.RunValidationsConcurrently;

        foreach (var loc in s.KnownHashLocations)
            KnownHashLocations.Add(loc);
    }

    [RelayCommand]
    public void Save()
    {
        var s = _store.Current;
        s.DefaultHashStoragePath = DefaultHashStoragePath;
        s.DefaultReminderDays = DefaultReminderDays;
        s.DefaultAlgorithm = HasherFactory.FromDisplayIndex(DefaultAlgorithmIndex);
        s.DefaultAutoscan = DefaultAutoscan;
        s.AutoscanPromptOnAttach = AutoscanPromptOnAttach;
        s.RunAtLogin = RunAtLogin;
        s.RunValidationsConcurrently = RunValidationsConcurrently;
        s.KnownHashLocations = KnownHashLocations.ToList();

        _store.Save();
        ApplyRunAtLogin(RunAtLogin);
        SaveStatus = "Settings saved.";
    }

    [RelayCommand]
    public void AddKnownLocation(string path)
    {
        if (!string.IsNullOrWhiteSpace(path) && !KnownHashLocations.Contains(path))
        {
            KnownHashLocations.Add(path);
            // Update backing store immediately so Refresh can find hash files in new locations
            _store.Current.KnownHashLocations = KnownHashLocations.ToList();
            _store.Save();
        }
    }

    [RelayCommand]
    public void RemoveKnownLocation(string path)
    {
        KnownHashLocations.Remove(path);
    }

    private static void ApplyRunAtLogin(bool enable)
    {
        try
        {
            const string keyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
            const string valueName = "HashCheck";
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(keyPath, writable: true);
            if (key == null) return;
            if (enable)
            {
                var exePath = Environment.ProcessPath ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                if (exePath != null)
                    key.SetValue(valueName, $"\"{exePath}\"");
            }
            else
            {
                key.DeleteValue(valueName, throwOnMissingValue: false);
            }
        }
        catch { }
    }
}
