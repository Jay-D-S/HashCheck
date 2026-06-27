using HashCheck.Core.Validation;
using HashCheck.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace HashCheck.Views;

/// <summary>Navigation parameter for <see cref="MirrorVerificationPage"/>.</summary>
public record MirrorVerificationRequest(string HashFilePath, string NewVolumeSerial);

/// <summary>Code-behind for the mirror verification page — validates a newly registered mirror and runs cross-drive majority-vote analysis when mismatches are found.</summary>
public sealed partial class MirrorVerificationPage : Page
{
    public MirrorVerificationViewModel ViewModel { get; }

    private string _hashFilePath = "";

    public MirrorVerificationPage()
    {
        ViewModel = new MirrorVerificationViewModel(AppServices.HashSets);
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is not MirrorVerificationRequest req) return;
        _hashFilePath = req.HashFilePath;

        // Subscribe to phase changes to update code-behind UI elements
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;

        await ViewModel.RunAsync(req.HashFilePath, req.NewVolumeSerial);
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MirrorVerificationViewModel.Phase)
            or nameof(MirrorVerificationViewModel.AnalysisReport)
            or nameof(MirrorVerificationViewModel.ValidationReport)
            or nameof(MirrorVerificationViewModel.MediaName)
            or nameof(MirrorVerificationViewModel.NewMirrorLabel))
        {
            UpdateCodeBehindUI();
        }
    }

    private void UpdateCodeBehindUI()
    {
        // Subtitle
        var mediaName = ViewModel.MediaName;
        var label     = ViewModel.NewMirrorLabel;
        SubtitleLabel.Text = string.IsNullOrEmpty(label)
            ? mediaName
            : $"{mediaName}  —  new mirror: {label}";

        // All-good detail
        if (ViewModel.IsAllGood && ViewModel.ValidationReport != null)
        {
            var r = ViewModel.ValidationReport;
            AllGoodDetail.Text = $"{r.TotalMatching:N0} file{(r.TotalMatching == 1 ? "" : "s")} verified. Your new mirror matches the stored hashes.";
        }

        // 50/50 case
        if (ViewModel.Has50_50Case && ViewModel.ValidationReport != null)
        {
            var count = ViewModel.ValidationReport.TotalCorrupted;
            Fifty50Heading.Text = $"⚠  {count} file{(count == 1 ? "" : "s")} don't match — unable to determine which copy is correct";

            var paths = ViewModel.ValidationReport.NotMatchingFiles
                .Where(f => f.Reason == NotMatchingReason.Corrupted)
                .Select(f => f.RelativePath)
                .ToList();
            Fifty50FileList.Text = paths.Count <= 20
                ? string.Join("\n", paths)
                : string.Join("\n", paths.Take(20)) + $"\n… and {paths.Count - 20} more";
        }

        // Majority-vote result sections
        BindAnalysisResults();
    }

    private void BindAnalysisResults()
    {
        var report = ViewModel.AnalysisReport;
        if (report == null)
        {
            CorruptedSection.Visibility    = Visibility.Collapsed;
            SuspectSection.Visibility      = Visibility.Collapsed;
            IndeterminateSection.Visibility = Visibility.Collapsed;
            return;
        }

        // Corrupted on new mirror
        var corrupted = report.Results
            .Where(r => r.Status == CrossDriveFileStatus.NewMirrorCorrupted)
            .Select(r => r.RelativePath)
            .ToList();
        CorruptedSection.Visibility = corrupted.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (corrupted.Count > 0)
        {
            CorruptedHeading.Text = $"Corrupted on new mirror ({corrupted.Count} file{(corrupted.Count == 1 ? "" : "s")})";
            CorruptedList.ItemsSource = corrupted;
        }

        // Stored hash suspect
        var suspect = report.Results
            .Where(r => r.Status == CrossDriveFileStatus.StoredHashSuspect)
            .Select(r => r.RelativePath)
            .ToList();
        SuspectSection.Visibility = suspect.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (suspect.Count > 0)
        {
            SuspectHeading.Text = $"Stored hashes may be wrong ({suspect.Count} file{(suspect.Count == 1 ? "" : "s")})";
            SuspectList.ItemsSource = suspect;
        }

        // Indeterminate
        var indeterminate = report.Results
            .Where(r => r.Status == CrossDriveFileStatus.Indeterminate)
            .Select(r => r.RelativePath)
            .ToList();
        IndeterminateSection.Visibility = indeterminate.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (indeterminate.Count > 0)
        {
            IndeterminateHeading.Text = $"Cannot determine ({indeterminate.Count} file{(indeterminate.Count == 1 ? "" : "s")})";
            IndeterminateList.ItemsSource = indeterminate;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.Cancel();
    }

    private void Repair_Click(object sender, RoutedEventArgs e)
    {
        Frame.Navigate(typeof(RepairPage), _hashFilePath);
    }

    private void Rebaseline_Click(object sender, RoutedEventArgs e)
    {
        Frame.Navigate(typeof(ReCreatePage), _hashFilePath);
    }

    private void MediaGroup_Click(object sender, RoutedEventArgs e)
    {
        Frame.Navigate(typeof(MediaGroupPage), _hashFilePath);
    }

    private void Dashboard_Click(object sender, RoutedEventArgs e)
    {
        Frame.Navigate(typeof(DashboardPage));
    }
}
