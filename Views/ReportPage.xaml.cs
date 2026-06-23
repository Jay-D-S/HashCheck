using HashCheck.Core.Validation;
using HashCheck.ViewModels;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace HashCheck.Views;

/// <summary>Code-behind for the validation report page.</summary>
public sealed partial class ReportPage : Page
{
    private ReportViewModel? _vm;

    public ReportPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is not ValidationReport report) return;

        _vm = new ReportViewModel(report);
        BindReport(report);
    }

    private void BindReport(ValidationReport r)
    {
        MediaNameText.Text = r.MediaName;
        TimestampText.Text = r.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
        FilesFoundText.Text = $"{r.TotalFilesFound} / {r.TotalFilesInHashSet}";
        MatchingText.Text = $"{r.TotalMatching} / {r.TotalNotMatching}";
        IssuesText.Text = $"{r.TotalMissing} / {r.TotalErrors} / {r.TotalNew}";

        StatusText.Text = r.Status;
        var passed = r.Passed;
        StatusBadge.Background = new SolidColorBrush(
            passed
                ? Windows.UI.Color.FromArgb(255, 45, 184, 77)
                : Windows.UI.Color.FromArgb(255, 232, 17, 35));
        StatusText.Foreground = new SolidColorBrush(Microsoft.UI.Colors.White);

        // Populate detail lists
        if (r.NotMatchingFiles.Count > 0)
        {
            NotMatchingList.ItemsSource = r.NotMatchingFiles
                .Select(f => $"[{f.Reason}] {f.RelativePath}");
        }
        else
        {
            NotMatchingPanel.Visibility = Visibility.Collapsed;
        }

        if (r.MissingFiles.Count > 0)
            MissingList.ItemsSource = r.MissingFiles;
        else
            MissingPanel.Visibility = Visibility.Collapsed;

        if (r.ErrorFiles.Count > 0)
            ErrorsList.ItemsSource = r.ErrorFiles.Select(f => $"{f.RelativePath}: {f.ErrorMessage}");
        else
            ErrorsPanel.Visibility = Visibility.Collapsed;

        if (r.NewFiles.Count > 0)
            NewFilesList.ItemsSource = r.NewFiles;
        else
            NewFilesPanel.Visibility = Visibility.Collapsed;
    }

    private async void ExportHtml_Click(object sender, RoutedEventArgs e)
    {
        if (_vm == null) return;
        var picker = new FileSavePicker();
        picker.SuggestedFileName = $"HashCheck-Report-{DateTime.Now:yyyyMMdd-HHmm}.html";
        picker.FileTypeChoices.Add("HTML", new List<string> { ".html" });
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindow));
        var file = await picker.PickSaveFileAsync();
        if (file != null)
            await _vm.ExportHtmlAsync(file.Path);
    }

    private async void ExportCsv_Click(object sender, RoutedEventArgs e)
    {
        if (_vm == null) return;
        var picker = new FileSavePicker();
        picker.SuggestedFileName = $"HashCheck-Report-{DateTime.Now:yyyyMMdd-HHmm}.csv";
        picker.FileTypeChoices.Add("CSV", new List<string> { ".csv" });
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindow));
        var file = await picker.PickSaveFileAsync();
        if (file != null)
            await _vm.ExportCsvAsync(file.Path);
    }

    private void ReCreate_Click(object sender, RoutedEventArgs e)
    {
        if (_vm?.Report.HashFilePath != null)
            Frame.Navigate(typeof(ReCreatePage), _vm.Report.HashFilePath);
    }

    private void BackToDashboard_Click(object sender, RoutedEventArgs e)
    {
        if (Frame.CanGoBack)
            Frame.GoBack();
        else
            Frame.Navigate(typeof(DashboardPage));
    }
}
