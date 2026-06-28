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

        // Populate controls via Items.Add rather than ItemsSource — set_ItemsSource hits a
        // missing WinRT CCW vtable in the trimmed build regardless of whether it's called from
        // x:Bind or code-behind. Items.Add uses a different COM path that survives trimming.
        StoragePathBox.Text = ViewModel.DefaultHashStoragePath;
        foreach (var name in ViewModel.AlgorithmNames)
            AlgorithmBox.Items.Add(name);
        AlgorithmBox.SelectedIndex = ViewModel.DefaultAlgorithmIndex;
        foreach (var loc in ViewModel.KnownHashLocations)
            LocationsList.Items.Add(loc);
        ViewModel.KnownHashLocations.CollectionChanged += (_, _) =>
        {
            LocationsList.Items.Clear();
            foreach (var loc in ViewModel.KnownHashLocations)
                LocationsList.Items.Add(loc);
        };

        ViewModel.SaveStatus = "";
    }

    private void StoragePathBox_TextChanged(object sender, TextChangedEventArgs e)
        => ViewModel.DefaultHashStoragePath = StoragePathBox.Text;

    private void AlgorithmBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AlgorithmBox.SelectedIndex >= 0)
            ViewModel.DefaultAlgorithmIndex = AlgorithmBox.SelectedIndex;
    }

    private void ReminderDaysBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (!double.IsNaN(args.NewValue))
            ViewModel.DefaultReminderDays = (int)args.NewValue;
    }

    private async void BrowseStorage_Click(object sender, RoutedEventArgs e)
    {
        var btn = (Button)sender;
        btn.IsEnabled = false;
        try
        {
            var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.ComputerFolder };
            picker.FileTypeFilter.Add("*");
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindow));
            var folder = await picker.PickSingleFolderAsync();
            if (folder != null && !string.IsNullOrEmpty(folder.Path))
            {
                ViewModel.DefaultHashStoragePath = folder.Path;
                StoragePathBox.Text = folder.Path;
            }
        }
        finally { btn.IsEnabled = true; }
    }

    private async void AddLocation_Click(object sender, RoutedEventArgs e)
    {
        var btn = (Button)sender;
        btn.IsEnabled = false;
        try
        {
            var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.ComputerFolder };
            picker.FileTypeFilter.Add("*");
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindow));
            var folder = await picker.PickSingleFolderAsync();
            if (folder != null && !string.IsNullOrEmpty(folder.Path))
                ViewModel.AddKnownLocationCommand.Execute(folder.Path);
        }
        finally { btn.IsEnabled = true; }
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
