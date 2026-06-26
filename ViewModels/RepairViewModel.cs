using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using HashCheck.Core.HashFile;
using HashCheck.Core.Repair;
using HashCheck.Core.Scanning;
using HashCheck.Core.Settings;
using HashCheck.Core.Volumes;
using HashCheck.Services;

namespace HashCheck.ViewModels;

public enum RepairPhase { Validating, Repairing, Complete, Cancelled, Failed }

/// <summary>View model for <see cref="HashCheck.Views.RepairPage"/>. Drives a two-phase operation: validate all online volumes, then cross-copy corrupted files from intact copies.</summary>
public partial class RepairViewModel : ViewModelBase
{
    private readonly HashSetService _service;
    private readonly AppSettings _settings;
    private CancellationTokenSource? _cts;

    [ObservableProperty] private RepairPhase _phase = RepairPhase.Validating;
    [ObservableProperty] private string _mediaName = "";
    [ObservableProperty] private string _currentFile = "";
    [ObservableProperty] private int _repairFilesProcessed;
    [ObservableProperty] private int _repairFilesTotal;
    [ObservableProperty] private Core.Repair.RepairReport? _repairReport;
    [ObservableProperty] private string _errorMessage = "";

    /// <summary>Volume label map populated during <see cref="RunAsync"/>. Maps serial → label for use when formatting result strings.</summary>
    public IReadOnlyDictionary<string, string> VolumeLabels { get; private set; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public ObservableCollection<ValidationRow> ValidationRows { get; } = new();

    partial void OnPhaseChanged(RepairPhase value)
    {
        OnPropertyChanged(nameof(IsValidating));
        OnPropertyChanged(nameof(IsRepairing));
        OnPropertyChanged(nameof(IsComplete));
        OnPropertyChanged(nameof(IsDone));
        OnPropertyChanged(nameof(CanCancel));
    }

    partial void OnRepairFilesProcessedChanged(int value) =>
        OnPropertyChanged(nameof(RepairProgressPercent));

    partial void OnRepairFilesTotalChanged(int value) =>
        OnPropertyChanged(nameof(RepairProgressPercent));

    public bool IsValidating => Phase == RepairPhase.Validating;
    public bool IsRepairing => Phase == RepairPhase.Repairing;
    public bool IsComplete => Phase == RepairPhase.Complete;
    public bool IsDone => Phase is RepairPhase.Complete or RepairPhase.Cancelled or RepairPhase.Failed;
    public bool CanCancel => Phase is RepairPhase.Validating or RepairPhase.Repairing;
    public double RepairProgressPercent => RepairFilesTotal > 0
        ? (double)RepairFilesProcessed / RepairFilesTotal * 100.0 : 0;

    public RepairViewModel(HashSetService service, AppSettings settings)
    {
        _service = service;
        _settings = settings;
    }

    public async Task RunAsync(string hashFilePath)
    {
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        try
        {
            var hashFile = await HashFileReader.ReadAsync(hashFilePath, verifyIntegrity: false);
            MediaName = hashFile.MediaName;

            // Build volume label map for result display
            VolumeLabels = hashFile.Volumes.ToDictionary(
                v => v.SerialNumber,
                v => v.Label,
                StringComparer.OrdinalIgnoreCase);

            var onlineMap = VolumeLocator.GetAllVolumes()
                .ToDictionary(v => v.SerialNumber, StringComparer.OrdinalIgnoreCase);

            ValidationRows.Clear();
            var onlineVolumes = new List<VolumeIdentity>();

            foreach (var vol in hashFile.Volumes)
            {
                if (onlineMap.TryGetValue(vol.SerialNumber, out var volId))
                {
                    onlineVolumes.Add(volId);
                    ValidationRows.Add(new ValidationRow(
                        vol.SerialNumber, vol.Label,
                        vol.GetFullScanPath(volId.RootPath)));
                }
            }

            if (ValidationRows.Count == 0)
            {
                ErrorMessage = "No online volumes found. Connect at least one drive and try again.";
                Phase = RepairPhase.Failed;
                return;
            }

            // Phase 1: validate all online volumes (writes [VALIDATIONS] entries)
            Phase = RepairPhase.Validating;

            if (_settings.RunValidationsConcurrently && ValidationRows.Count > 1)
                await Task.WhenAll(ValidationRows.Select(r => ValidateRowAsync(r, hashFilePath, ct)));
            else
                foreach (var row in ValidationRows)
                    await ValidateRowAsync(row, hashFilePath, ct);

            ct.ThrowIfCancellationRequested();

            var reports = ValidationRows
                .Where(r => r.Report != null)
                .Select(r => r.Report!)
                .ToList();

            if (reports.Count == 0)
            {
                ErrorMessage = "Validation did not complete for any volume.";
                Phase = RepairPhase.Failed;
                return;
            }

            // Count unique corrupted paths across all reports
            var corruptedCount = reports
                .SelectMany(r => r.NotMatchingFiles)
                .Where(f => f.Reason == Core.Validation.NotMatchingReason.Corrupted)
                .Select(f => f.RelativePath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();

            if (corruptedCount == 0)
            {
                // Nothing to repair — all validations passed or only Modified files found
                RepairReport = new RepairReport
                {
                    Timestamp = DateTime.UtcNow,
                    MediaName = hashFile.MediaName,
                    HashFilePath = hashFilePath,
                    Results = new List<Core.Repair.RepairResult>()
                };
                Phase = RepairPhase.Complete;
                return;
            }

            RepairFilesTotal = corruptedCount;

            // Phase 2: cross-drive repair
            Phase = RepairPhase.Repairing;

            var engine = new RepairEngine(hashFile, reports, onlineVolumes);
            var repairProgress = new Progress<RepairProgress>(p =>
            {
                CurrentFile = p.CurrentFile;
                RepairFilesProcessed = p.FilesProcessed;
                RepairFilesTotal = p.FilesTotal;
            });

            RepairReport = await engine.RunAsync(repairProgress, ct);
            Phase = RepairPhase.Complete;
        }
        catch (OperationCanceledException)
        {
            Phase = RepairPhase.Cancelled;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            Phase = RepairPhase.Failed;
        }
    }

    private async Task ValidateRowAsync(ValidationRow row, string hashFilePath, CancellationToken ct)
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
                hashFilePath, row.ScanRoot, row.SerialNumber, progress, ct);
            row.Report = report;
            row.Status = ValidationRowStatus.Done;
        }
        catch (OperationCanceledException)
        {
            row.Status = ValidationRowStatus.Cancelled;
            throw;
        }
        catch (Exception ex)
        {
            row.ErrorMessage = ex.Message;
            row.Status = ValidationRowStatus.Failed;
        }
    }

    public void Cancel() => _cts?.Cancel();
}
