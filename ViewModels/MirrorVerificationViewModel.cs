using CommunityToolkit.Mvvm.ComponentModel;
using HashCheck.Core.HashFile;
using HashCheck.Core.Scanning;
using HashCheck.Core.Validation;
using HashCheck.Core.Volumes;
using HashCheck.Services;

namespace HashCheck.ViewModels;

public enum MirrorVerificationPhase
{
    Validating,
    Analysing,
    AllGood,
    Complete,
    Cancelled,
    Failed,
}

/// <summary>View model for MirrorVerificationPage. Validates a newly registered mirror and, when Corrupted files are found, cross-checks against other online volumes using majority-vote logic.</summary>
public partial class MirrorVerificationViewModel : ViewModelBase
{
    private readonly HashSetService _service;
    private CancellationTokenSource? _cts;

    // ── Phase ────────────────────────────────────────────────────────────────
    [ObservableProperty]
    private MirrorVerificationPhase _phase = MirrorVerificationPhase.Validating;

    partial void OnPhaseChanged(MirrorVerificationPhase value)
    {
        OnPropertyChanged(nameof(IsValidating));
        OnPropertyChanged(nameof(IsAnalysing));
        OnPropertyChanged(nameof(IsInProgress));
        OnPropertyChanged(nameof(IsAllGood));
        OnPropertyChanged(nameof(IsComplete));
        OnPropertyChanged(nameof(IsCancelled));
        OnPropertyChanged(nameof(IsFailed));
        OnPropertyChanged(nameof(PhaseHeading));
        OnPropertyChanged(nameof(CanCancel));
        OnPropertyChanged(nameof(Has50_50Case));
        OnPropertyChanged(nameof(HasMajorityVote));
    }

    public bool IsValidating => Phase == MirrorVerificationPhase.Validating;
    public bool IsAnalysing  => Phase == MirrorVerificationPhase.Analysing;
    public bool IsInProgress => Phase is MirrorVerificationPhase.Validating or MirrorVerificationPhase.Analysing;
    public bool IsAllGood    => Phase == MirrorVerificationPhase.AllGood;
    public bool IsComplete   => Phase == MirrorVerificationPhase.Complete;
    public bool IsCancelled  => Phase == MirrorVerificationPhase.Cancelled;
    public bool IsFailed     => Phase == MirrorVerificationPhase.Failed;
    public bool IsDone       => !IsInProgress;
    public bool CanCancel    => IsInProgress;

    public string PhaseHeading => Phase switch
    {
        MirrorVerificationPhase.Validating => "Validating new mirror…",
        MirrorVerificationPhase.Analysing  => "Comparing with other drives…",
        _                                  => "",
    };

    // ── Validation progress ──────────────────────────────────────────────────
    [ObservableProperty] private int  _filesProcessed;
    [ObservableProperty] private int  _filesTotal;
    [ObservableProperty] private long _bytesProcessed;
    [ObservableProperty] private long _bytesTotal;
    [ObservableProperty] private string _currentFile = "";
    [ObservableProperty] private string _etaText     = "";

    partial void OnFilesProcessedChanged(int  value) => OnPropertyChanged(nameof(ProgressFilesText));
    partial void OnFilesTotalChanged(int  value)      => OnPropertyChanged(nameof(ProgressFilesText));
    partial void OnBytesProcessedChanged(long value)  { OnPropertyChanged(nameof(ProgressBytesText)); OnPropertyChanged(nameof(BytesProcessedD)); }
    partial void OnBytesTotalChanged(long value)      { OnPropertyChanged(nameof(ProgressBytesText)); OnPropertyChanged(nameof(BytesTotalD)); }

    public double BytesProcessedD => BytesProcessed;
    public double BytesTotalD     => BytesTotal > 0 ? BytesTotal : 1;
    public string ProgressFilesText => $"Files: {FilesProcessed} / {FilesTotal}";
    public string ProgressBytesText => $"Bytes: {FormatBytes(BytesProcessed)} / {FormatBytes(BytesTotal)}";

    // ── Analysis progress ────────────────────────────────────────────────────
    [ObservableProperty] private int    _analysisProcessed;
    [ObservableProperty] private int    _analysisTotal;
    [ObservableProperty] private string _analysisCurrentFile = "";

    partial void OnAnalysisProcessedChanged(int value) { OnPropertyChanged(nameof(AnalysisProgressText)); OnPropertyChanged(nameof(AnalysisProgressD)); }
    partial void OnAnalysisTotalChanged(int value)     { OnPropertyChanged(nameof(AnalysisProgressText)); OnPropertyChanged(nameof(AnalysisTotalD)); }

    public string AnalysisProgressText => $"File {AnalysisProcessed} of {AnalysisTotal}";
    public double AnalysisProgressD    => AnalysisProcessed;
    public double AnalysisTotalD       => AnalysisTotal > 0 ? AnalysisTotal : 1;

    // ── Results ──────────────────────────────────────────────────────────────
    [ObservableProperty] private string _mediaName      = "";
    [ObservableProperty] private string _newMirrorLabel = "";
    [ObservableProperty] private int    _otherOnlineVolumeCount;
    [ObservableProperty] private string _errorMessage   = "";

    [ObservableProperty]
    private ValidationReport? _validationReport;

    [ObservableProperty]
    private CrossDriveAnalysisReport? _analysisReport;

    partial void OnAnalysisReportChanged(CrossDriveAnalysisReport? value)
    {
        OnPropertyChanged(nameof(Has50_50Case));
        OnPropertyChanged(nameof(HasMajorityVote));
        OnPropertyChanged(nameof(CanRepair));
    }

    partial void OnValidationReportChanged(ValidationReport? value)
    {
        OnPropertyChanged(nameof(Has50_50Case));
    }

    // 50/50 when there were Corrupted files but no other drives were available to cross-check
    public bool Has50_50Case    => IsComplete && AnalysisReport == null && (ValidationReport?.TotalCorrupted > 0);
    // Majority-vote results when other drives were checked
    public bool HasMajorityVote => IsComplete && AnalysisReport != null;

    // "Repair new mirror" makes sense only when other drives confirmed these files are corrupted
    public bool CanRepair => (AnalysisReport?.NewMirrorCorrupted ?? 0) > 0;

    // ── Run logic ────────────────────────────────────────────────────────────
    public MirrorVerificationViewModel(HashSetService service)
    {
        _service = service;
    }

    public async Task RunAsync(string hashFilePath, string newVolumeSerial)
    {
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        try
        {
            var hashFile = await HashFileReader.ReadAsync(hashFilePath, verifyIntegrity: false);
            MediaName = hashFile.MediaName;

            var newVolumeEntry = hashFile.Volumes
                .FirstOrDefault(v => v.SerialNumber.Equals(newVolumeSerial, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException($"Volume {newVolumeSerial} is not registered in this hash file.");

            var volId = VolumeLocator.FindBySerial(newVolumeSerial)
                ?? throw new InvalidOperationException($"Volume {newVolumeSerial} is not currently online.");

            var scanRoot = newVolumeEntry.GetFullScanPath(volId.RootPath);
            NewMirrorLabel = volId.Label;

            // ── Phase 1: validate the new mirror ─────────────────────────────
            Phase = MirrorVerificationPhase.Validating;

            var validationProgress = new Progress<ScanProgress>(p =>
            {
                FilesProcessed = p.FilesProcessed;
                FilesTotal     = p.FilesTotal;
                BytesProcessed = p.BytesProcessed;
                BytesTotal     = p.BytesTotal;
                CurrentFile    = p.CurrentFile;
                EtaText        = p.Eta.HasValue ? $"ETA: {p.Eta.Value:mm\\:ss}" : "Calculating…";
            });

            // ValidateAsync also writes a [VALIDATIONS] entry to the hash file
            var report = await _service.ValidateAsync(
                hashFilePath, scanRoot, newVolumeSerial, validationProgress, ct);
            ValidationReport = report;

            if (report.Passed || report.TotalCorrupted == 0)
            {
                Phase = MirrorVerificationPhase.AllGood;
                return;
            }

            // ── Phase 2: cross-drive analysis ─────────────────────────────────
            var otherOnline = new List<(string serial, string scanRoot)>();
            foreach (var vol in hashFile.Volumes)
            {
                if (vol.SerialNumber.Equals(newVolumeSerial, StringComparison.OrdinalIgnoreCase))
                    continue;
                var otherId = VolumeLocator.FindBySerial(vol.SerialNumber);
                if (otherId == null) continue;
                otherOnline.Add((vol.SerialNumber, vol.GetFullScanPath(otherId.RootPath)));
            }

            OtherOnlineVolumeCount = otherOnline.Count;

            if (otherOnline.Count == 0)
            {
                // No other drives online — can't do majority vote; 50/50 case
                Phase = MirrorVerificationPhase.Complete;
                return;
            }

            Phase = MirrorVerificationPhase.Analysing;

            // Re-read so we have the freshest file list (autoscan inside ValidateAsync may have changed it)
            hashFile = await HashFileReader.ReadAsync(hashFilePath, verifyIntegrity: false);

            var analysisProgress = new Progress<(int processed, int total, string currentFile)>(p =>
            {
                AnalysisProcessed   = p.processed;
                AnalysisTotal       = p.total;
                AnalysisCurrentFile = p.currentFile;
            });

            AnalysisReport = await CrossDriveAnalyser.AnalyseAsync(
                hashFile, report, otherOnline, analysisProgress, ct);

            Phase = MirrorVerificationPhase.Complete;
        }
        catch (OperationCanceledException)
        {
            Phase = MirrorVerificationPhase.Cancelled;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            Phase = MirrorVerificationPhase.Failed;
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
        }
    }

    public void Cancel() => _cts?.Cancel();

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1L << 40) return $"{bytes / (double)(1L << 40):F1} TB";
        if (bytes >= 1L << 30) return $"{bytes / (double)(1L << 30):F1} GB";
        if (bytes >= 1L << 20) return $"{bytes / (double)(1L << 20):F1} MB";
        if (bytes >= 1L << 10) return $"{bytes / (double)(1L << 10):F1} KB";
        return $"{bytes} B";
    }
}
