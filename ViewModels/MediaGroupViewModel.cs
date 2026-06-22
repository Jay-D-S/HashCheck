using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using HashCheck.Core.HashFile;
using HashCheck.Core.Volumes;
using HashCheck.Services;

namespace HashCheck.ViewModels;

public partial class VolumeRow : ObservableObject
{
    public string SerialNumber { get; }
    public string Label { get; }
    public string ScanSubPath { get; }
    public bool IsOnline { get; }
    public string MountPoint { get; }
    public string FullScanPath { get; }
    public string ScanScope { get; }

    [ObservableProperty] private string _lastVerified = "Never";
    [ObservableProperty] private string _status = "Never verified";

    public VolumeRow(VolumeEntry entry, VolumeIdentity? onlineVol, ValidationEntry? lastVal,
        IReadOnlyList<string> topLevelPaths)
    {
        SerialNumber = entry.SerialNumber;
        Label = entry.Label;
        ScanSubPath = entry.ScanSubPath;
        IsOnline = onlineVol != null;

        if (onlineVol != null)
        {
            MountPoint = onlineVol.RootPath.TrimEnd('\\');
            FullScanPath = entry.GetFullScanPath(onlineVol.RootPath);
        }
        else
        {
            MountPoint = "—";
            FullScanPath = "—";
        }

        if (entry.ScanSubPath != @"\")
        {
            ScanScope = IsOnline ? FullScanPath : entry.ScanSubPath;
        }
        else if (topLevelPaths.Count > 0)
        {
            var prefix = IsOnline ? MountPoint : "";
            ScanScope = string.Join(", ", topLevelPaths.Take(3).Select(p => prefix + p));
            if (topLevelPaths.Count > 3) ScanScope += "…";
        }
        else
        {
            ScanScope = IsOnline ? FullScanPath : "—";
        }

        if (lastVal != null)
        {
            LastVerified = lastVal.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
            Status = lastVal.Status;
        }
    }
}

public partial class MediaGroupViewModel : ViewModelBase
{
    private readonly HashSetService _service;

    public string HashFilePath { get; private set; } = "";

    [ObservableProperty] private string _mediaName = "";
    [ObservableProperty] private string _description = "";
    [ObservableProperty] private VolumeRow? _selectedRow;

    partial void OnSelectedRowChanged(VolumeRow? oldValue, VolumeRow? newValue) =>
        OnPropertyChanged(nameof(HasSelectedRow));

    public bool HasSelectedRow => SelectedRow != null;
    public bool AnyOnline => Volumes.Any(v => v.IsOnline);

    public ObservableCollection<VolumeRow> Volumes { get; } = new();

    public MediaGroupViewModel(HashSetService service) => _service = service;

    public async Task LoadAsync(string hashFilePath)
    {
        HashFilePath = hashFilePath;
        var hashFile = await HashFileReader.ReadAsync(hashFilePath, verifyIntegrity: false);
        MediaName = hashFile.MediaName;
        Description = hashFile.Description;

        var onlineMap = VolumeLocator.GetAllVolumes()
            .ToDictionary(v => v.SerialNumber, StringComparer.OrdinalIgnoreCase);

        var topLevelPaths = hashFile.Paths
            .Where(p => !hashFile.Paths.Any(other =>
                !string.Equals(other, p, StringComparison.OrdinalIgnoreCase) &&
                p.StartsWith(other.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)))
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();

        Volumes.Clear();
        SelectedRow = null;

        foreach (var vol in hashFile.Volumes)
        {
            onlineMap.TryGetValue(vol.SerialNumber, out var onlineVol);
            var lastVal = hashFile.Validations.LastOrDefault(v =>
                string.Equals(v.VolumeSerial, vol.SerialNumber, StringComparison.OrdinalIgnoreCase));
            Volumes.Add(new VolumeRow(vol, onlineVol, lastVal, topLevelPaths));
        }
    }
}
