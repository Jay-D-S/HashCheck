using System.Diagnostics.CodeAnalysis;
using HashCheck.Core.Repair;
using HashCheck.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace HashCheck.Views;

/// <summary>Code-behind for the cross-drive repair page. Receives a hash file path as navigation parameter, validates all online volumes, then repairs corrupted files from intact copies.</summary>
public sealed partial class RepairPage : Page
{
    public RepairViewModel ViewModel { get; }

    private string _hashFilePath = "";

    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(ValidationRow))]
    public RepairPage()
    {
        ViewModel = new RepairViewModel(AppServices.HashSets, AppServices.Settings.Current);
        InitializeComponent();
        ViewModel.ValidationRows.CollectionChanged += (_, _) =>
        {
            ValidationRowsPanel.Items.Clear();
            foreach (var row in ViewModel.ValidationRows)
                ValidationRowsPanel.Items.Add(row);
        };
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ViewModel.Phase))
                UpdatePhaseLabel();
            if (e.PropertyName == nameof(ViewModel.RepairReport) && ViewModel.RepairReport != null)
                BindRepairReport(ViewModel.RepairReport);
            if (e.PropertyName == nameof(ViewModel.RepairFilesProcessed) ||
                e.PropertyName == nameof(ViewModel.RepairFilesTotal))
                UpdateRepairCountText();
        };
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is not string hashFilePath) return;
        _hashFilePath = hashFilePath;
        UpdatePhaseLabel();
        _ = ViewModel.RunAsync(hashFilePath);
    }

    private void UpdatePhaseLabel()
    {
        PhaseLabel.Text = ViewModel.Phase switch
        {
            RepairPhase.Validating => "Step 1 of 2 — Validating drives…",
            RepairPhase.Repairing  => "Step 2 of 2 — Repairing files…",
            RepairPhase.Complete   => "Complete",
            RepairPhase.Cancelled  => "Cancelled",
            RepairPhase.Failed     => "Failed",
            _                      => ""
        };

        if (ViewModel.Phase == RepairPhase.Cancelled)
        {
            StatusInfoBar.Severity = InfoBarSeverity.Warning;
            StatusInfoBar.Title = "Repair cancelled.";
            StatusInfoBar.Message = "Some files may not have been repaired.";
            StatusInfoBar.IsOpen = true;
            SummaryGrid.Visibility = Visibility.Collapsed;
            NothingToRepairText.Visibility = Visibility.Collapsed;
        }
        else if (ViewModel.Phase == RepairPhase.Failed)
        {
            StatusInfoBar.Severity = InfoBarSeverity.Error;
            StatusInfoBar.Title = "Repair failed.";
            StatusInfoBar.Message = ViewModel.ErrorMessage;
            StatusInfoBar.IsOpen = true;
            SummaryGrid.Visibility = Visibility.Collapsed;
            NothingToRepairText.Visibility = Visibility.Collapsed;
        }
    }

    private void UpdateRepairCountText()
    {
        RepairCountText.Text = ViewModel.RepairFilesTotal > 0
            ? $"{ViewModel.RepairFilesProcessed} / {ViewModel.RepairFilesTotal} files"
            : "";
    }

    private void BindRepairReport(RepairReport report)
    {
        RepairedCountText.Text = report.Repaired.ToString();
        UnrecoverableCountText.Text = report.Unrecoverable.ToString();
        SkippedCountText.Text = report.ReadOnlySkipped.ToString();
        FailedCountText.Text = report.Failed.ToString();

        if (!report.AnyCorrupted)
        {
            SummaryGrid.Visibility = Visibility.Collapsed;
            NothingToRepairText.Visibility = Visibility.Visible;
            return;
        }

        SummaryGrid.Visibility = Visibility.Visible;
        NothingToRepairText.Visibility = Visibility.Collapsed;

        var labels = ViewModel.VolumeLabels;

        string LabelFor(string? serial) =>
            serial != null && labels.TryGetValue(serial, out var lbl) ? lbl : serial ?? "Unknown";

        var repaired = report.Results
            .Where(r => r.Status == RepairStatus.Repaired)
            .Select(r => $"{r.RelativePath}  ←  {LabelFor(r.SourceSerial)} → {LabelFor(r.TargetSerial)}")
            .ToList();

        var unrecoverable = report.Results
            .Where(r => r.Status == RepairStatus.Unrecoverable)
            .Select(r => r.RelativePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var skipped = report.Results
            .Where(r => r.Status == RepairStatus.ReadOnlySkipped)
            .Select(r => $"{r.RelativePath}  ({LabelFor(r.TargetSerial)} is read-only)")
            .ToList();

        var failed = report.Results
            .Where(r => r.Status is RepairStatus.VerificationFailed or RepairStatus.Error)
            .Select(r => $"{r.RelativePath}  —  {r.ErrorMessage}")
            .ToList();

        if (repaired.Count > 0)
        {
            RepairedList.ItemsSource = repaired;
            RepairedPanel.Visibility = Visibility.Visible;
        }

        if (unrecoverable.Count > 0)
        {
            UnrecoverableList.ItemsSource = unrecoverable;
            UnrecoverablePanel.Visibility = Visibility.Visible;
        }

        if (skipped.Count > 0)
        {
            SkippedList.ItemsSource = skipped;
            SkippedPanel.Visibility = Visibility.Visible;
        }

        if (failed.Count > 0)
        {
            FailedList.ItemsSource = failed;
            FailedPanel.Visibility = Visibility.Visible;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => ViewModel.Cancel();

    private void ValidateAgain_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_hashFilePath))
            Frame.Navigate(typeof(ValidatePage), new ValidateRequest(_hashFilePath));
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (Frame.CanGoBack) Frame.GoBack();
        else Frame.Navigate(typeof(DashboardPage));
    }
}
