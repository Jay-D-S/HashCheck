using HashCheck.Core.Volumes;
using HashCheck.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace HashCheck.Views;

/// <summary>Code-behind for the validation page.</summary>
public sealed partial class ValidatePage : Page
{
    public ValidateViewModel ViewModel { get; }

    public ValidatePage()
    {
        ViewModel = new ValidateViewModel(AppServices.HashSets, AppServices.Settings.Current);
        InitializeComponent();
        LoadDriveList();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is string hashFilePath)
            await ViewModel.StartWithFileAsync(hashFilePath);
    }

    private void LoadDriveList()
    {
        foreach (var vol in VolumeLocator.GetAllVolumes())
            ManualDrivePicker.Items.Add($"{vol.RootPath.TrimEnd('\\')} ({vol.Label})");
    }

    private async void BrowseHash_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".hash");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindow));
        var file = await picker.PickSingleFileAsync();
        if (file != null)
        {
            HashFilePathBox.Text = file.Path;
            ViewModel.HashFilePath = file.Path;
        }
    }

    private async void StartValidate_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(ViewModel.HashFilePath))
            await ViewModel.StartWithFileAsync(ViewModel.HashFilePath);
    }

    private void UseManualDrive_Click(object sender, RoutedEventArgs e)
    {
        var selected = ManualDrivePicker.SelectedItem?.ToString();
        if (selected == null) return;
        var driveLetter = selected.Split(' ')[0] + "\\";
        ViewModel.SelectMediaManually(driveLetter);
    }

    private void CancelInsertMedia_Click(object sender, RoutedEventArgs e) =>
        ViewModel.CancelInsertMedia();

    // Per-row Pause/Resume/Cancel/ViewReport buttons store their ValidationRow in Button.Tag
    // (set in XAML via {Binding}) so the handler can target the correct row without knowing which one.
    private void Pause_Click(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is ValidationRow row) row.Pause();
    }

    private void Resume_Click(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is ValidationRow row) row.Resume();
    }

    private void CancelRow_Click(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is ValidationRow row) row.Cancel();
    }

    private void ViewReport_Click(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is ValidationRow row && row.Report != null)
            Frame.Navigate(typeof(ReportPage), row.Report);
    }

    private void BackToDashboard_Click(object sender, RoutedEventArgs e) =>
        Frame.Navigate(typeof(DashboardPage));
}
