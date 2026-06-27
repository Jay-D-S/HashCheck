using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HashCheck.Core;
using HashCheck.Core.HashFile;
using HashCheck.Core.Hashing;
using HashCheck.Core.Scanning;
using HashCheck.Core.Settings;
using HashCheck.Core.Volumes;
using HashCheck.Services;

namespace HashCheck.ViewModels;

/// <summary>Wizard step for the Create Hash flow.</summary>
public enum CreateStep { SelectScope, Configure, Progress, Done }

/// <summary>View model for the create-hash wizard. Drives a multi-step UI from scope selection through hashing to completion.</summary>
public partial class CreateHashViewModel : ViewModelBase
{
    private readonly HashSetService _service;
    private readonly AppSettings _settings;
    private CancellationTokenSource? _cts;

    public ObservableCollection<FolderNode> DriveNodes { get; } = new();

    private CreateStep _currentStep = CreateStep.SelectScope;
    public CreateStep CurrentStep
    {
        get => _currentStep;
        set
        {
            if (SetProperty(ref _currentStep, value))
            {
                OnPropertyChanged(nameof(IsScopeStep));
                OnPropertyChanged(nameof(IsConfigureStep));
                OnPropertyChanged(nameof(IsProgressStep));
                OnPropertyChanged(nameof(IsDoneStep));
                OnPropertyChanged(nameof(IsScanning));
                OnPropertyChanged(nameof(IsHashingFiles));
                OnPropertyChanged(nameof(ProgressPhaseTitle));
            }
        }
    }
    [ObservableProperty] private string _description = "";
    [ObservableProperty] private int _algorithmIndex = 0;
    [ObservableProperty] private int _reminderDays = 180;
    [ObservableProperty] private bool _filterModeExclude = true;
    [ObservableProperty] private bool _autoscan = false;

    // FilterModeInclude is the logical inverse — notify it whenever FilterModeExclude changes
    partial void OnFilterModeExcludeChanged(bool value)
    {
        OnPropertyChanged(nameof(FilterModeInclude));
    }
    [ObservableProperty] private string _filterPatterns = string.Join("\n", FilterEngine.DefaultExcludePatterns);
    [ObservableProperty] private string _storagePath = "";
    [ObservableProperty] private string _selectedMediaRoot = "";
    [ObservableProperty] private string _selectedMediaSerial = "";
    [ObservableProperty] private string _selectedMediaLabel = "";
    [ObservableProperty] private long _selectedMediaTotalBytes;

    // Progress
    [ObservableProperty] private int _filesProcessed;
    [ObservableProperty] private int _filesTotal;
    [ObservableProperty] private long _bytesProcessed;
    [ObservableProperty] private long _bytesTotal;
    [ObservableProperty] private string _currentFile = "";

    partial void OnFilesProcessedChanged(int value) => OnPropertyChanged(nameof(ProgressFilesText));
    partial void OnFilesTotalChanged(int value)
    {
        OnPropertyChanged(nameof(ProgressFilesText));
        OnPropertyChanged(nameof(IsScanning));
        OnPropertyChanged(nameof(IsHashingFiles));
        OnPropertyChanged(nameof(ProgressPhaseTitle));
    }
    partial void OnBytesProcessedChanged(long value)
    {
        OnPropertyChanged(nameof(ProgressBytesText));
        OnPropertyChanged(nameof(BytesProcessedD));
    }
    partial void OnBytesTotalChanged(long value)
    {
        OnPropertyChanged(nameof(ProgressBytesText));
        OnPropertyChanged(nameof(BytesTotalD));
    }
    [ObservableProperty] private string _progressMessage = "";

    partial void OnProgressMessageChanged(string value)
    {
        OnPropertyChanged(nameof(HasProgressMessage));
        OnPropertyChanged(nameof(ProgressMessageVisibility));
    }
    [ObservableProperty] private string _etaText = "";
    [ObservableProperty] private bool _canCancel = true;

    // Result
    [ObservableProperty] private string _resultMessage = "";
    [ObservableProperty] private string _resultFilePath = "";
    [ObservableProperty] private bool _resultSuccess;

    partial void OnResultSuccessChanged(bool value)
    {
        OnPropertyChanged(nameof(ResultIcon));
        OnPropertyChanged(nameof(ResultIconBrush));
    }

    public string[] AlgorithmNames => HasherFactory.AlgorithmDisplayNames;

    // Step visibility helpers
    public bool IsScopeStep => CurrentStep == CreateStep.SelectScope;
    public bool IsConfigureStep => CurrentStep == CreateStep.Configure;
    public bool IsProgressStep => CurrentStep == CreateStep.Progress;
    public bool IsDoneStep => CurrentStep == CreateStep.Done;

    // Scanning = in progress step but scan hasn't completed yet (FilesTotal still 0)
    public bool IsScanning => CurrentStep == CreateStep.Progress && FilesTotal == 0;
    public bool IsHashingFiles => CurrentStep == CreateStep.Progress && FilesTotal > 0;
    public string ProgressPhaseTitle => IsScanning ? "Scanning files…" : "Hashing files…";

    // FilterMode inverse for XAML
    public bool FilterModeInclude
    {
        get => !FilterModeExclude;
        set => FilterModeExclude = !value;
    }

    public bool HasProgressMessage => !string.IsNullOrEmpty(ProgressMessage);
    public Microsoft.UI.Xaml.Visibility ProgressMessageVisibility =>
        HasProgressMessage ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    // ProgressBar.Value/Maximum require double; these properties expose the long values as double
    public double BytesProcessedD => BytesProcessed;
    public double BytesTotalD => BytesTotal > 0 ? BytesTotal : 1;

    public string ProgressFilesText =>
        $"Files: {FilesProcessed} / {FilesTotal}";

    public string ProgressBytesText =>
        $"Bytes: {FormatBytes(BytesProcessed)} / {FormatBytes(BytesTotal)}";

    public string ResultIcon => ResultSuccess ? "" : ""; // Completed / Error
    public Microsoft.UI.Xaml.Media.SolidColorBrush ResultIconBrush => ResultSuccess
        ? new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 45, 184, 64))
        : new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 232, 17, 35));

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1L << 40) return $"{bytes / (double)(1L << 40):F1} TB";
        if (bytes >= 1L << 30) return $"{bytes / (double)(1L << 30):F1} GB";
        if (bytes >= 1L << 20) return $"{bytes / (double)(1L << 20):F1} MB";
        if (bytes >= 1L << 10) return $"{bytes / (double)(1L << 10):F1} KB";
        return $"{bytes} B";
    }

    public CreateHashViewModel(HashSetService service, AppSettings settings)
    {
        _service = service;
        _settings = settings;
        _reminderDays = settings.DefaultReminderDays;
        _algorithmIndex = HasherFactory.ToDisplayIndex(settings.DefaultAlgorithm);
        _storagePath = settings.DefaultHashStoragePath;
        _autoscan = settings.DefaultAutoscan;
    }

    public void LoadDrives()
    {
        DriveNodes.Clear();
        foreach (var vol in VolumeLocator.GetAllVolumes())
        {
            var node = new FolderNode(
                $"{vol.RootPath.TrimEnd('\\')} ({vol.Label})",
                vol.RootPath, hasChildren: true)
            {
                IsChecked = false
            };
            // Add placeholder for lazy loading
            node.Children.Add(new FolderNode("Loading...", "", false));
            DriveNodes.Add(node);
        }
    }

    public void LoadChildren(FolderNode node)
    {
        if (node.IsLoaded) return;
        node.IsLoaded = true;
        node.Children.Clear();

        try
        {
            var dirs = Directory.GetDirectories(node.FullPath);
            foreach (var dir in dirs.OrderBy(d => d))
            {
                var child = new FolderNode(Path.GetFileName(dir), dir, true)
                {
                    Parent = node,
                    IsChecked = node.IsChecked == true
                };
                child.Children.Add(new FolderNode("Loading...", "", false));
                node.Children.Add(child);
            }
        }
        catch { }

        if (node.Children.Count == 0)
            node.HasChildren = false;
    }

    [RelayCommand]
    public void SelectScopeComplete()
    {
        var selected = DriveNodes
            .SelectMany(d => d.IsChecked == true
                ? new[] { d.FullPath }
                : d.GetSelectedPaths())
            .ToList();

        if (selected.Count == 0)
        {
            ProgressMessage = "Please select at least one drive or folder.";
            return;
        }

        // Detect which drive/volume is selected
        var firstPath = selected[0];
        var root = Path.GetPathRoot(firstPath) ?? firstPath;
        var vol = VolumeLocator.GetVolumeIdentity(root);
        if (vol != null)
        {
            SelectedMediaRoot = vol.RootPath;
            SelectedMediaSerial = vol.SerialNumber;
            SelectedMediaLabel = vol.Label;
            SelectedMediaTotalBytes = vol.TotalBytes;
        }

        if (string.IsNullOrEmpty(StoragePath))
            StoragePath = _settings.DefaultHashStoragePath.Length > 0
                ? _settings.DefaultHashStoragePath
                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        CurrentStep = CreateStep.Configure;
    }

    [RelayCommand]
    public async Task StartHashingAsync()
    {
        if (string.IsNullOrWhiteSpace(Description))
        {
            ProgressMessage = "Description is required.";
            return;
        }

        // Collect absolute paths of everything the user checked in the tree.
        var selectedAbsPaths = DriveNodes
            .SelectMany(d => d.IsChecked == true
                ? new[] { d.FullPath }     // whole drive checked
                : d.GetSelectedPaths())    // individual subfolders
            .ToList();

        // The scan root is the deepest common ancestor of the selection.
        // For a single folder like Z:\_PHOTOS\2026 this equals that folder itself,
        // which means [PATHS] and [FILES] are stored relative to it — so a mirror
        // at D:\PHOTOS\2026 only needs ScanSubPath=\PHOTOS\2026 to validate correctly.
        var scanRoot = ComputeCommonAncestor(selectedAbsPaths, SelectedMediaRoot);

        var scopePaths = selectedAbsPaths
            .Select(p => FileScanner.ToRelative(scanRoot, p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // If root ("\") is included it covers everything; collapse to a single entry.
        if (scopePaths.Count == 0 || scopePaths.Contains("\\", StringComparer.OrdinalIgnoreCase))
            scopePaths = new List<string> { "\\" };

        CurrentStep = CreateStep.Progress;
        CanCancel = true;
        _cts = new CancellationTokenSource();

        var filters = FilterPatterns
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        var options = new CreateOptions(
            scanRoot,
            SelectedMediaSerial,
            SelectedMediaLabel,
            SelectedMediaTotalBytes,
            scopePaths,
            Description,
            HasherFactory.FromDisplayIndex(AlgorithmIndex),
            ReminderDays,
            FilterModeExclude ? FilterMode.Exclude : FilterMode.Include,
            filters,
            StoragePath,
            Autoscan);

        var progress = new Progress<ScanProgress>(p =>
        {
            FilesProcessed = p.FilesProcessed;
            FilesTotal = p.FilesTotal;
            BytesProcessed = p.BytesProcessed;
            BytesTotal = p.BytesTotal;
            CurrentFile = p.CurrentFile;
            EtaText = p.Eta.HasValue
                ? $"ETA: {p.Eta.Value:mm\\:ss}"
                : "Calculating...";
        });

        try
        {
            var hashData = await _service.CreateAsync(options, progress, _cts.Token);
            ResultSuccess = true;
            ResultMessage = "Hash set created successfully.";
            ResultFilePath = hashData.FilePath;
        }
        catch (OperationCanceledException)
        {
            ResultSuccess = false;
            ResultMessage = "Operation cancelled.";
            ResultFilePath = "";
        }
        catch (Exception ex)
        {
            ResultSuccess = false;
            ResultMessage = $"Error: {ex.Message}";
            ResultFilePath = "";
        }
        finally
        {
            CanCancel = false;
            CurrentStep = CreateStep.Done;
        }
    }

    [RelayCommand]
    public void Cancel()
    {
        _cts?.Cancel();
    }

    [RelayCommand]
    public void Reset()
    {
        CurrentStep = CreateStep.SelectScope;
        Description = "";
        ProgressMessage = "";
        ResultMessage = "";
        ResultFilePath = "";
        FilesProcessed = FilesTotal = 0;
        BytesProcessed = BytesTotal = 0;
        LoadDrives();
    }

    // Returns the deepest common ancestor of a set of absolute paths.
    // Example: ["Z:\_PHOTOS\2026\Jan", "Z:\_PHOTOS\2026\Feb"] → "Z:\_PHOTOS\2026"
    //          ["Z:\_PHOTOS\2026"]                             → "Z:\_PHOTOS\2026" (unchanged)
    //          ["Z:\"]                                         → "Z:\"
    private static string ComputeCommonAncestor(IList<string> absolutePaths, string fallback)
    {
        if (absolutePaths.Count == 0) return fallback;

        // Normalise: keep drive roots with trailing slash; remove trailing slash elsewhere.
        static string Normalise(string p)
        {
            var t = p.TrimEnd('\\');
            return t.Length == 2 && t[1] == ':' ? t + "\\" : t;
        }

        var normalised = absolutePaths.Select(Normalise).ToList();
        if (normalised.Count == 1) return normalised[0];

        var first = normalised[0];
        int commonLen = first.Length;

        foreach (var path in normalised.Skip(1))
        {
            int i = 0, max = Math.Min(commonLen, path.Length);
            while (i < max && char.ToUpperInvariant(first[i]) == char.ToUpperInvariant(path[i]))
                i++;
            commonLen = i;
        }

        var common = first[..commonLen];
        if (common.EndsWith('\\')) return common;

        var lastSlash = common.LastIndexOf('\\');
        if (lastSlash < 0) return fallback;

        var ancestor = common[..lastSlash];
        if (ancestor.Length == 2 && ancestor[1] == ':') ancestor += "\\";
        return string.IsNullOrEmpty(ancestor) ? fallback : ancestor;
    }
}
