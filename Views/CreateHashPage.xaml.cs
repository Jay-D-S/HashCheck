using System.Diagnostics.CodeAnalysis;
using HashCheck.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace HashCheck.Views;

/// <summary>Code-behind for the Create Hash wizard page.</summary>
public sealed partial class CreateHashPage : Page
{
    public CreateHashViewModel ViewModel { get; }

    // The TreeView DataTemplate uses reflection-based {Binding Content.Name} and
    // {Binding Content.IsChecked} to reach FolderNode properties through TreeViewNode.Content.
    // This attribute tells the trimmer to preserve all public properties on FolderNode
    // so those bindings resolve correctly in trimmed/self-contained publish outputs.
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties, typeof(FolderNode))]
    public CreateHashPage()
    {
        ViewModel = new CreateHashViewModel(AppServices.HashSets, AppServices.Settings.Current);
        InitializeComponent();
        foreach (var name in ViewModel.AlgorithmNames)
            AlgorithmBox.Items.Add(name);
        AlgorithmBox.SelectedIndex = ViewModel.AlgorithmIndex;
        StoragePathBox.Text = ViewModel.StoragePath;
        LoadDrivesIntoTree();
    }

    private void AlgorithmBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AlgorithmBox.SelectedIndex >= 0)
            ViewModel.AlgorithmIndex = AlgorithmBox.SelectedIndex;
    }

    private void ReminderDaysBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (!double.IsNaN(args.NewValue))
            ViewModel.ReminderDays = (int)args.NewValue;
    }

    private void StoragePathBox_TextChanged(object sender, TextChangedEventArgs e)
        => ViewModel.StoragePath = StoragePathBox.Text;

    private void LoadDrivesIntoTree()
    {
        ScopeTree.RootNodes.Clear();
        ViewModel.LoadDrives();

        foreach (var drive in ViewModel.DriveNodes)
        {
            var node = new TreeViewNode
            {
                Content = drive,
                HasUnrealizedChildren = drive.HasChildren
            };
            ScopeTree.RootNodes.Add(node);
        }
    }

    // Guards against re-entrant checkbox change handling when PropagateToChildren fires binding updates on children
    private bool _suppressCheckBoxEvents;

    private void NodeCheckBox_StateChanged(object sender, RoutedEventArgs e)
    {
        if (_suppressCheckBoxEvents) return;
        if (sender is not CheckBox cb) return;
        if (cb.DataContext is not Microsoft.UI.Xaml.Controls.TreeViewNode treeNode) return;
        if (treeNode.Content is not FolderNode folderNode) return;

        _suppressCheckBoxEvents = true;
        try { folderNode.IsChecked = cb.IsChecked; }
        finally { _suppressCheckBoxEvents = false; }
    }

    private void ScopeTree_Expanding(TreeView sender, TreeViewExpandingEventArgs args)
    {
        if (args.Node.HasUnrealizedChildren && args.Node.Content is FolderNode folderNode)
        {
            ViewModel.LoadChildren(folderNode);
            args.Node.Children.Clear();
            foreach (var child in folderNode.Children)
            {
                var childNode = new TreeViewNode
                {
                    Content = child,
                    HasUnrealizedChildren = child.HasChildren
                };
                args.Node.Children.Add(childNode);
            }
            args.Node.HasUnrealizedChildren = false;
        }
    }

    private void SelectScopeNext_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.SelectScopeCompleteCommand.Execute(null);
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
                ViewModel.StoragePath = folder.Path;
                StoragePathBox.Text = folder.Path;
            }
        }
        finally { btn.IsEnabled = true; }
    }

    private async void StartHashing_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.StartHashingAsync();
    }

    private void ConfigureBack_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.CurrentStep = CreateStep.SelectScope;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.CancelCommand.Execute(null);
    }

    private void GoToDashboard_Click(object sender, RoutedEventArgs e)
    {
        Frame.Navigate(typeof(DashboardPage));
    }

    private void CreateAnother_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ResetCommand.Execute(null);
        LoadDrivesIntoTree();
    }
}
