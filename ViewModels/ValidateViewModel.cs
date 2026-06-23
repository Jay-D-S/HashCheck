using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using HashCheck.Core.Scanning;
using HashCheck.Core.Settings;
using HashCheck.Core.Validation;
using HashCheck.Core.Volumes;
using HashCheck.Services;

namespace HashCheck.ViewModels;

/// <summary>Lifecycle state of one validation row on the ValidatePage.</summary>
public enum ValidationRowStatus { Queued, Running, Paused, Done, Failed, Cancelled }

/// <summary>View model for a single volume row on ValidatePage. Owns its own <see cref="PauseToken"/> and <see cref="CancellationTokenSource"/> so each volume can be paused/cancelled independently.</summary>
public sealed partial class ValidationRow : ObservableObject
{
    public string SerialNumber { get; }
    public string Label { get; }
    public string ScanRoot { get; }

    [ObservableProperty] private ValidationRowStatus _status = ValidationRowStatus.Queued;
    [ObservableProperty] private int _filesProcessed;
    [ObservableProperty] private int _filesTotal;
    [ObservableProperty] private long _bytesProcessed;
    [ObservableProperty] private long _bytesTotal;
    [ObservableProperty] private string _currentFile = "";
    [ObservableProperty] private string _etaText = "";
    [ObservableProperty] private string _errorMessage = "";
    [ObservableProperty] private Core.Validation.ValidationReport? _report;

    partial void OnStatusChanged(ValidationRowStatus value)
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(CanPause));
        OnPropertyChanged(nameof(CanResume));
        OnPropertyChanged(nameof(IsActive));
    }

    partial void OnReportChanged(Core.Validation.ValidationReport? value) =>
        OnPropertyChanged(nameof(HasReport));

    partial void OnErrorMessageChanged(string value) =>
        OnPropertyChanged(nameof(HasErrorMessage));

    partial void OnFilesProcessedChanged(int value) =>
        OnPropertyChanged(nameof(ProgressText));
    partial void OnFilesTotalChanged(int value) =>
        OnPropertyChanged(nameof(ProgressText));
    partial void OnEtaTextChanged(string value) =>
        OnPropertyChanged(nameof(ProgressText));

    partial void OnBytesProcessedChanged(long value) =>
        OnPropertyChanged(nameof(ProgressPercent));
    partial void OnBytesTotalChanged(long value) =>
        OnPropertyChanged(nameof(ProgressPercent));

    public bool HasReport => Report != null;
    public bool HasErrorMessage => !string.IsNullOrEmpty(ErrorMessage);
    public string ProgressText =>
        FilesTotal > 0 ? $"{FilesProcessed} / {FilesTotal} files  {EtaText}" : EtaText;
    public bool CanPause => Status == ValidationRowStatus.Running;
    public bool CanResume => Status == ValidationRowStatus.Paused;
    public bool IsActive => Status is ValidationRowStatus.Queued
                                           or ValidationRowStatus.Running
                                           or ValidationRowStatus.Paused;
    public double ProgressPercent => BytesTotal > 0
        ? (double)BytesProcessed / BytesTotal * 100.0 : 0;

    public string StatusText => Status switch
    {
        ValidationRowStatus.Queued => "Queued",
        ValidationRowStatus.Running => "Running",
        ValidationRowStatus.Paused => "Paused",
        ValidationRowStatus.Done => "Done",
        ValidationRowStatus.Failed => "Failed",
        ValidationRowStatus.Cancelled => "Cancelled",
        _ => ""
    };

    internal PauseToken PauseToken { get; } = new();
    internal CancellationTokenSource Cts { get; } = new();

    public void Pause()
    {
        PauseToken.Pause();
        Status = ValidationRowStatus.Paused;
    }

    public void Resume()
    {
        PauseToken.Resume();
        Status = ValidationRowStatus.Running;
    }

    public void Cancel() => Cts.Cancel();

    public ValidationRow(string serialNumber, string label, string scanRoot)
    {
        SerialNumber = serialNumber;
        Label = label;
        ScanRoot = scanRoot;
    }
}

/// <summary>Navigation parameter for <see cref="HashCheck.Views.ValidatePage"/>. Carries the hash file path and an optional serial number to restrict validation to a single volume (used when navigating from MediaGroupPage).</summary>
public record ValidateRequest(string HashFilePath, string? RestrictToSerial = null);

/// <summary>Wizard step for the validate flow.</summary>
public enum ValidateStep { PickFile, InsertMedia, Validating }

/// <summary>View model for the ValidatePage. Discovers online volumes for a hash set, builds per-volume rows, and drives concurrent or sequential validation runs.</summary>
public partial class ValidateViewModel : ViewModelBase
{
    private readonly HashSetService _service;
    private readonly AppSettings _settings;
    private Core.HashFile.HashFileData? _hashFile;
    private CancellationTokenSource? _pollCts;
    private List<string> _pollSerials = new();

    private ValidateStep _currentStep = ValidateStep.PickFile;
    public ValidateStep CurrentStep
    {
        get => _currentStep;
        set
        {
            if (SetProperty(ref _currentStep, value))
            {
                OnPropertyChanged(nameof(IsPickFileStep));
                OnPropertyChanged(nameof(IsInsertMediaStep));
                OnPropertyChanged(nameof(IsValidatingStep));
            }
        }
    }

    public bool IsPickFileStep => CurrentStep == ValidateStep.PickFile;
    public bool IsInsertMediaStep => CurrentStep == ValidateStep.InsertMedia;
    public bool IsValidatingStep => CurrentStep == ValidateStep.Validating;
    public bool HasHashFilePath => !string.IsNullOrEmpty(HashFilePath);

    private string _hashFilePath = "";
    public string HashFilePath
    {
        get => _hashFilePath;
        set { if (SetProperty(ref _hashFilePath, value)) OnPropertyChanged(nameof(HasHashFilePath)); }
    }

    [ObservableProperty] private string _mediaName = "";
    [ObservableProperty] private string _insertMediaMessage = "";
    [ObservableProperty] private bool _isPolling;
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private bool _allDone;

    public ObservableCollection<ValidationRow> Rows { get; } = new();

    public ValidateViewModel(HashSetService service, AppSettings settings)
    {
        _service = service;
        _settings = settings;
    }

    public async Task StartWithFileAsync(string hashFilePath, string? restrictToSerial = null)
    {
        HashFilePath = hashFilePath;
        _hashFile = await Core.HashFile.HashFileReader.ReadAsync(hashFilePath, verifyIntegrity: false);
        MediaName = _hashFile.MediaName;

        Rows.Clear();
        AllDone = false;
        IsRunning = false;

        // Register as the active validation so the user can navigate away and return.
        AppServices.ActiveValidation = this;

        var onlineMap = VolumeLocator.GetAllVolumes()
            .ToDictionary(v => v.SerialNumber, StringComparer.OrdinalIgnoreCase);

        foreach (var ve in _hashFile.Volumes)
        {
            if (restrictToSerial != null &&
                !string.Equals(ve.SerialNumber, restrictToSerial, StringComparison.OrdinalIgnoreCase))
                continue;
            if (onlineMap.TryGetValue(ve.SerialNumber, out var vol))
                Rows.Add(new ValidationRow(ve.SerialNumber, ve.Label,
                    ve.GetFullScanPath(vol.RootPath)));
        }

        if (Rows.Count == 0)
        {
            InsertMediaMessage = _hashFile.Volumes.Count == 1
                ? $"Please insert media: {_hashFile.Volumes[0].Label} ({_hashFile.Volumes[0].SerialNumber})"
                : $"Please insert any registered volume for: {_hashFile.MediaName}";
            CurrentStep = ValidateStep.InsertMedia;
            StartPolling(_hashFile.Volumes.Select(v => v.SerialNumber).ToList());
            return;
        }

        CurrentStep = ValidateStep.Validating;
        await RunValidationsAsync();
    }

    private void StartPolling(List<string> serials)
    {
        _pollSerials = serials;
        IsPolling = true;
        _pollCts = new CancellationTokenSource();
        _ = PollForMediaAsync(_pollCts.Token);
    }

    private async Task PollForMediaAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(2000, ct).ConfigureAwait(false);
            foreach (var serial in _pollSerials)
            {
                if (VolumeLocator.FindBySerial(serial) != null)
                {
                    IsPolling = false;
                    await StartWithFileAsync(HashFilePath);
                    return;
                }
            }
        }
    }

    public void CancelInsertMedia()
    {
        _pollCts?.Cancel();
        IsPolling = false;
        CurrentStep = ValidateStep.PickFile;
    }

    public void SelectMediaManually(string driveLetter)
    {
        _pollCts?.Cancel();
        IsPolling = false;
        if (_hashFile == null) { CurrentStep = ValidateStep.PickFile; return; }

        var volId = VolumeLocator.GetVolumeIdentity(driveLetter);
        if (volId == null) { CurrentStep = ValidateStep.PickFile; return; }

        Rows.Clear();
        var entry = _hashFile.Volumes.FirstOrDefault(v =>
            string.Equals(v.SerialNumber, volId.SerialNumber, StringComparison.OrdinalIgnoreCase));
        var scanRoot = entry != null ? entry.GetFullScanPath(driveLetter) : driveLetter;
        var serial = entry?.SerialNumber ?? volId.SerialNumber;
        Rows.Add(new ValidationRow(serial, volId.Label, scanRoot));

        CurrentStep = ValidateStep.Validating;
        _ = RunValidationsAsync();
    }

    private async Task RunValidationsAsync()
    {
        IsRunning = true;
        AllDone = false;

        // Concurrent mode: all volume rows hash in parallel (good for separate physical drives).
        // Sequential mode: one row at a time (avoids contention when mirrors share the same spindle).
        if (_settings.RunValidationsConcurrently && Rows.Count > 1)
            await Task.WhenAll(Rows.Select(ValidateRowAsync));
        else
            foreach (var row in Rows)
                await ValidateRowAsync(row);

        IsRunning = false;
        AllDone = true;
        AppServices.ActiveValidation = null;
    }

    private async Task ValidateRowAsync(ValidationRow row)
    {
        row.Status = ValidationRowStatus.Running;

        var progress = new Progress<ScanProgress>(p =>
        {
            row.FilesProcessed = p.FilesProcessed;
            row.FilesTotal = p.FilesTotal;
            row.BytesProcessed = p.BytesProcessed;
            row.BytesTotal = p.BytesTotal;
            row.CurrentFile = p.CurrentFile;
            row.EtaText = p.Eta.HasValue ? $"ETA: {p.Eta.Value:mm\\:ss}" : "Calculating...";
        });

        try
        {
            var report = await _service.ValidateAsync(
                HashFilePath, row.ScanRoot, row.SerialNumber,
                progress, row.Cts.Token, row.PauseToken);
            row.Report = report;
            row.Status = ValidationRowStatus.Done;
        }
        catch (OperationCanceledException)
        {
            row.Status = ValidationRowStatus.Cancelled;
        }
        catch (Exception ex)
        {
            row.ErrorMessage = ex.Message;
            row.Status = ValidationRowStatus.Failed;
        }
    }
}
