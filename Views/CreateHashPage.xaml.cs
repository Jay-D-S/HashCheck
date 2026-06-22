using HashCheck.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace HashCheck.Views;

public sealed partial class CreateHashPage : Page
{
    public CreateHashViewModel ViewModel { get; }

    public CreateHashPage()
    {
        ViewModel = new CreateHashViewModel(AppServices.HashSets, AppServices.Settings.Current);
        InitializeComponent();
        LoadDrivesIntoTree();
    }

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

    // Prevents re-entrant handling when PropagateToChildren triggers binding updates
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
        var picker = new FolderPicker();
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindow));
        var folder = await picker.PickSingleFolderAsync();
        if (folder != null)
            ViewModel.StoragePath = folder.Path;
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
