using HashCheck.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace HashCheck.Views;

/// <summary>Code-behind for the settings page.</summary>
public sealed partial class SettingsPage : Page
{
    public SettingsViewModel ViewModel { get; }

    public SettingsPage()
    {
        ViewModel = new SettingsViewModel(AppServices.Settings);
        InitializeComponent();
    }

    private async void BrowseStorage_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker();
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindow));
        var folder = await picker.PickSingleFolderAsync();
        if (folder != null)
            ViewModel.DefaultHashStoragePath = folder.Path;
    }

    private async void AddLocation_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker();
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindow));
        var folder = await picker.PickSingleFolderAsync();
        if (folder != null)
            ViewModel.AddKnownLocationCommand.Execute(folder.Path);
    }

    private void RemoveLocation_Click(object sender, RoutedEventArgs e)
    {
        if (LocationsList.SelectedItem is string path)
            ViewModel.RemoveKnownLocationCommand.Execute(path);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.SaveCommand.Execute(null);
    }
}
