using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HashCheck.Core.HashFile;
using HashCheck.Core.Volumes;
using HashCheck.Services;

namespace HashCheck.ViewModels;

/// <summary>View model for a single row in the dashboard list. Combines <see cref="HashFileData"/> with the current online/offline status of its volumes.</summary>
public partial class DashboardItem : ObservableObject
{
    public HashFileData HashFile { get; }
    /// <summary>Number of this hash set's volumes that are currently mounted.</summary>
    public int OnlineCount { get; }

    public string MediaName => HashFile.MediaName;
    public string Description => HashFile.Description;
    public int FileCount => HashFile.Files.Count;
    public string TotalBytes => FormatBytes(HashFile.Files.Sum(f => f.SizeBytes));
    public string DateCreated => HashFile.DateCreated.ToLocalTime().ToString("yyyy-MM-dd");
    public string LastValidated => HashFile.LastValidated?.ToLocalTime().ToString("yyyy-MM-dd") ?? "Never";
    public string Status => HashFile.StatusText;
    public string NextDue => HashFile.DueDate.ToLocalTime().ToString("yyyy-MM-dd");
    public string FilePath => HashFile.FilePath;

    public int VolumeCount => HashFile.Volumes.Count;
    public bool IsAnyVolumeOnline => OnlineCount > 0;
    public string AvailabilityText => VolumeCount == 0 ? "—"
        : VolumeCount == 1
            ? (IsAnyVolumeOnline ? "Online" : "Offline")
            : $"{OnlineCount}/{VolumeCount} online";

    public DashboardItem(HashFileData hashFile, IReadOnlySet<string> onlineSerials)
    {
        HashFile = hashFile;
        OnlineCount = hashFile.Volumes.Count(v => onlineSerials.Contains(v.SerialNumber));
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1L << 40) return $"{bytes / (double)(1L << 40):F1} TB";
        if (bytes >= 1L << 30) return $"{bytes / (double)(1L << 30):F1} GB";
        if (bytes >= 1L << 20) return $"{bytes / (double)(1L << 20):F1} MB";
        if (bytes >= 1L << 10) return $"{bytes / (double)(1L << 10):F1} KB";
        return $"{bytes} B";
    }
}

/// <summary>View model for the dashboard page. Loads all known hash sets and tracks the selected item.</summary>
public partial class DashboardViewModel : ViewModelBase
{
    private readonly HashSetService _service;

    public ObservableCollection<DashboardItem> Items { get; } = new();

    [ObservableProperty]
    private DashboardItem? _selectedItem;

    partial void OnSelectedItemChanged(DashboardItem? value)
    {
        OnPropertyChanged(nameof(HasSelectedItem));
    }

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusMessage = "";

    partial void OnStatusMessageChanged(string value)
    {
        OnPropertyChanged(nameof(StatusMessageVisibility));
    }

    public bool HasSelectedItem => SelectedItem != null;
    public Microsoft.UI.Xaml.Visibility StatusMessageVisibility =>
        string.IsNullOrEmpty(StatusMessage)
            ? Microsoft.UI.Xaml.Visibility.Collapsed
            : Microsoft.UI.Xaml.Visibility.Visible;

    public DashboardViewModel(HashSetService service)
    {
        _service = service;
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsLoading = true;
        StatusMessage = "";
        try
        {
            var (all, diagnostics) = await _service.LoadAllKnownWithDiagnosticsAsync();
            var onlineSerials = VolumeLocator.GetAllVolumes()
                .Select(v => v.SerialNumber)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            Items.Clear();
            foreach (var hf in all)
                Items.Add(new DashboardItem(hf, onlineSerials));

            if (Items.Count == 0)
                StatusMessage = "No hash sets found.\n" + diagnostics;
            else
                StatusMessage = "";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error loading: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    public void RemoveSelected()
    {
        if (SelectedItem == null) return;
        _service.RemoveAndDeleteHashFile(SelectedItem.FilePath);
        Items.Remove(SelectedItem);
        SelectedItem = null;
    }
}
