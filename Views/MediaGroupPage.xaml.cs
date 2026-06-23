using HashCheck.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace HashCheck.Views;

/// <summary>Code-behind for the media group (volume management) page.</summary>
public sealed partial class MediaGroupPage : Page
{
    public MediaGroupViewModel ViewModel { get; }

    public MediaGroupPage()
    {
        ViewModel = new MediaGroupViewModel(AppServices.HashSets);
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is string hashFilePath)
            await ViewModel.LoadAsync(hashFilePath);
    }

    private void Validate_Click(object sender, RoutedEventArgs e)
    {
        var row = ViewModel.SelectedRow;
        if (row == null) return;
        Frame.Navigate(typeof(ValidatePage), new ValidateRequest(ViewModel.HashFilePath, row.SerialNumber));
    }

    // Opens a FolderPicker first (no picker-inside-dialog conflict) then confirms.
    private async void EditScanPath_Click(object sender, RoutedEventArgs e)
    {
        var row = ViewModel.SelectedRow;
        if (row == null) return;

        var folderPicker = new Windows.Storage.Pickers.FolderPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(
            folderPicker, WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow));
        folderPicker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.ComputerFolder;
        folderPicker.FileTypeFilter.Add("*");

        Windows.Storage.StorageFolder? picked;
        try { picked = await folderPicker.PickSingleFolderAsync(); }
        catch (Exception ex)
        {
            await ShowErrorAsync("Browse Error", ex.Message);
            return;
        }
        if (picked == null) return;

        var fullPath = picked.Path;
        var driveRoot = Path.GetPathRoot(fullPath) ?? @"\";
        var scanSubPath = ComputeSubPath(fullPath, driveRoot);

        var dlg = new ContentDialog
        {
            Title = $"Update Scan Root — {row.Label}",
            Content = $"New scan root:\n{fullPath}\n\n" +
                                "This should be the exact folder on this drive that contains " +
                                "the mirrored data — the same folder you would select if creating " +
                                "a fresh hash set on this drive.",
            PrimaryButtonText = "Update",
            CloseButtonText = "Cancel",
            XamlRoot = XamlRoot,
            DefaultButton = ContentDialogButton.Primary
        };
        if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;

        try
        {
            await AppServices.HashSets.UpdateVolumeScanPathAsync(
                ViewModel.HashFilePath, row.SerialNumber, scanSubPath);
            await ViewModel.LoadAsync(ViewModel.HashFilePath);
        }
        catch (Exception ex) { await ShowErrorAsync("Error", ex.Message); }
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (Frame.CanGoBack) Frame.GoBack();
        else Frame.Navigate(typeof(DashboardPage));
    }

    private async Task ShowErrorAsync(string title, string message)
    {
        var dlg = new ContentDialog
        {
            Title = title,
            Content = message,
            CloseButtonText = "OK",
            XamlRoot = XamlRoot
        };
        await dlg.ShowAsync();
    }

    private static string ComputeSubPath(string fullPath, string driveRoot)
    {
        var root = driveRoot.TrimEnd('\\');
        if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return @"\";
        var sub = fullPath.Substring(root.Length).TrimEnd('\\');
        return string.IsNullOrEmpty(sub) ? @"\" : sub;
    }
}
