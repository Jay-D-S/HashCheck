using System.Diagnostics.CodeAnalysis;
using HashCheck.Core.Volumes;
using HashCheck.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace HashCheck.Views;

/// <summary>Code-behind for the dashboard page.</summary>
public sealed partial class DashboardPage : Page
{
    public DashboardViewModel ViewModel { get; }

    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(DashboardItem))]
    public DashboardPage()
    {
        ViewModel = new DashboardViewModel(AppServices.HashSets);
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ActiveValidationBar.IsOpen = AppServices.ActiveValidation != null;
        await ViewModel.LoadAsync();
    }

    private void ReturnToValidation_Click(object sender, RoutedEventArgs e) =>
        Frame.Navigate(typeof(ValidatePage));

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        _ = ViewModel.LoadAsync();
    }

    private void SortHeader_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string col)
        {
            ViewModel.SortBy(col);
            UpdateSortHeaders();
        }
    }

    private void UpdateSortHeaders()
    {
        var headers = new (Button Btn, string Label)[]
        {
            (SortMedia,        "Media"),
            (SortDescription,  "Description"),
            (SortFiles,        "Files"),
            (SortSize,         "Size"),
            (SortCreated,      "Created"),
            (SortLastVerified, "Last Verified"),
            (SortStatus,       "Status"),
            (SortAvailable,    "Available"),
            (SortNextDue,      "Next Due"),
        };
        foreach (var (btn, label) in headers)
        {
            var col = btn.Tag as string;
            var indicator = ViewModel.SortColumn == col
                ? (ViewModel.SortAscending ? " ▲" : " ▼")
                : "";
            btn.Content = label + indicator;
        }
    }

    private void ItemsList_DoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
    {
        var item = ViewModel.SelectedItem;
        if (item == null) return;
        Frame.Navigate(typeof(MediaGroupPage), item.FilePath);
    }

    private void ValidateSelected_Click(object sender, RoutedEventArgs e)
    {
        var item = ViewModel.SelectedItem;
        if (item == null) return;
        Frame.Navigate(typeof(ValidatePage), item.FilePath);
    }

    private async void ReCreateSelected_Click(object sender, RoutedEventArgs e)
    {
        var item = ViewModel.SelectedItem;
        if (item == null) return;

        var dlg = new ContentDialog
        {
            Title = "Re-create Hash Set",
            Content = $"Re-create will re-baseline '{item.MediaName}' from scratch.\nThe current .hash file will be backed up.\n\nContinue?",
            PrimaryButtonText = "Re-create",
            CloseButtonText = "Cancel",
            XamlRoot = XamlRoot,
            DefaultButton = ContentDialogButton.Close
        };

        if (await dlg.ShowAsync() == ContentDialogResult.Primary)
            Frame.Navigate(typeof(ReCreatePage), item.FilePath);
    }

    private async void RegisterMirror_Click(object sender, RoutedEventArgs e)
    {
        var item = ViewModel.SelectedItem;
        if (item == null) return;

        var registeredSerials = item.HashFile.Volumes
            .Select(v => v.SerialNumber)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // FolderPicker must be shown BEFORE ContentDialog — WinUI does not support showing a
        // picker from within an open dialog (results in an E_FAIL COM exception on some builds).
        // Step 1: folder picker — opened before any ContentDialog to avoid WinUI picker conflict
        var folderPicker = new Windows.Storage.Pickers.FolderPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(
            folderPicker, WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow));
        folderPicker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.ComputerFolder;
        folderPicker.FileTypeFilter.Add("*");

        Windows.Storage.StorageFolder? picked;
        try { picked = await folderPicker.PickSingleFolderAsync(); }
        catch (Exception ex)
        {
            await ShowSimpleDialogAsync("Browse Error", ex.Message);
            return;
        }
        if (picked == null) return;

        var fullPath = picked.Path;
        var driveRoot = Path.GetPathRoot(fullPath);
        if (string.IsNullOrEmpty(driveRoot))
        {
            await ShowSimpleDialogAsync("Invalid Path", "Could not determine drive root for the selected folder.");
            return;
        }

        var volId = VolumeLocator.GetVolumeIdentity(driveRoot);
        if (volId == null)
        {
            await ShowSimpleDialogAsync("Unknown Drive", $"Could not read volume information for {driveRoot}.");
            return;
        }

        if (registeredSerials.Contains(volId.SerialNumber))
        {
            await ShowSimpleDialogAsync("Already Registered",
                $"The drive '{volId.Label}' is already registered in this group.");
            return;
        }

        // Compute scan sub-path relative to the drive root
        var scanSubPath = ComputeSubPath(fullPath, driveRoot);

        // Step 2: confirm with an editable path box
        var pathBox = new TextBox
        {
            Text = fullPath,
            MinWidth = 380,
            PlaceholderText = @"e.g. D:\Photos\2026"
        };

        var dlg = new ContentDialog
        {
            Title = $"Register Mirror — {item.MediaName}",
            Content = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    new TextBlock
                    {
                        Text = $"Adding: {volId.Label}  ({volId.SerialNumber})\n\n" +
                               "Enter the folder on this drive where the mirrored data lives — " +
                               "the exact folder that corresponds to the primary scan root.\n\n" +
                               "Example: if the primary data is at Z:\\_PHOTOS\\2026, " +
                               "enter D:\\PHOTOS\\2026 (the folder that contains the same files).\n\n" +
                               "The app stores this as the scan root for this volume and validates " +
                               "files relative to it, so different drives can use different folder names.",
                        TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
                        MaxWidth = 440
                    },
                    pathBox
                }
            },
            PrimaryButtonText = "Register",
            CloseButtonText = "Cancel",
            XamlRoot = XamlRoot,
            DefaultButton = ContentDialogButton.Primary
        };

        if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;

        // Re-derive the sub-path from the (possibly edited) text box value
        var confirmedPath = pathBox.Text.Trim();
        scanSubPath = ComputeSubPath(confirmedPath, driveRoot);

        try
        {
            await AppServices.HashSets.AddVolumeAsync(
                item.FilePath, volId.SerialNumber, volId.Label, volId.TotalBytes, scanSubPath);
            // Navigate to the verification page so the new mirror is immediately validated
            // and any corruption is detected before the user relies on it.
            Frame.Navigate(typeof(MirrorVerificationPage),
                new MirrorVerificationRequest(item.FilePath, volId.SerialNumber));
        }
        catch (Exception ex)
        {
            await ShowSimpleDialogAsync("Error Registering Mirror", ex.Message);
        }
    }

    private async Task ShowSimpleDialogAsync(string title, string message)
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

    private void OpenLocation_Click(object sender, RoutedEventArgs e)
    {
        var item = ViewModel.SelectedItem;
        if (item == null) return;

        // Open the first online volume's scan root; fall back to the hash file folder.
        var onlineMap = HashCheck.Core.Volumes.VolumeLocator.GetAllVolumes()
            .ToDictionary(v => v.SerialNumber, StringComparer.OrdinalIgnoreCase);

        foreach (var vol in item.HashFile.Volumes)
        {
            if (onlineMap.TryGetValue(vol.SerialNumber, out var identity))
            {
                var scanRoot = vol.GetFullScanPath(identity.RootPath);
                if (Directory.Exists(scanRoot))
                {
                    System.Diagnostics.Process.Start("explorer.exe", $"\"{scanRoot}\"");
                    return;
                }
            }
        }

        var dir = Path.GetDirectoryName(item.FilePath);
        if (dir != null && Directory.Exists(dir))
            System.Diagnostics.Process.Start("explorer.exe", dir);
    }

    private async void RemoveSelected_Click(object sender, RoutedEventArgs e)
    {
        var item = ViewModel.SelectedItem;
        if (item == null) return;

        var dlg = new ContentDialog
        {
            Title = "Remove Hash Set",
            Content = $"This will permanently delete the hash file:\n{item.FilePath}\n\nThis cannot be undone. Continue?",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            XamlRoot = XamlRoot,
            DefaultButton = ContentDialogButton.Close
        };

        if (await dlg.ShowAsync() == ContentDialogResult.Primary)
            ViewModel.RemoveSelected();
    }
}
