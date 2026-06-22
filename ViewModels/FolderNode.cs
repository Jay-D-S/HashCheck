using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace HashCheck.ViewModels;

public partial class FolderNode : ObservableObject
{
    public string Name { get; }
    public string FullPath { get; }
    public bool HasChildren { get; set; }

    private bool? _isChecked = false;
    public bool? IsChecked
    {
        get => _isChecked;
        set
        {
            if (_isChecked == value) return;
            _isChecked = value;
            OnPropertyChanged();
            if (value.HasValue)
                PropagateToChildren(value.Value);
            Parent?.UpdateFromChildren();
        }
    }

    public FolderNode? Parent { get; set; }
    public ObservableCollection<FolderNode> Children { get; } = new();
    public bool IsLoaded { get; set; }

    [ObservableProperty]
    private bool _isExpanded;

    public FolderNode(string name, string fullPath, bool hasChildren = true)
    {
        Name = name;
        FullPath = fullPath;
        HasChildren = hasChildren;
    }

    private void PropagateToChildren(bool value)
    {
        foreach (var child in Children)
        {
            child._isChecked = value;
            child.OnPropertyChanged(nameof(IsChecked));
            child.PropagateToChildren(value);
        }
    }

    public void UpdateFromChildren()
    {
        if (Children.Count == 0) return;
        var allTrue = Children.All(c => c.IsChecked == true);
        var allFalse = Children.All(c => c.IsChecked == false);
        var newState = allTrue ? true : allFalse ? false : (bool?)null;

        if (_isChecked != newState)
        {
            _isChecked = newState;
            OnPropertyChanged(nameof(IsChecked));
            Parent?.UpdateFromChildren();
        }
    }

    public IEnumerable<string> GetSelectedPaths()
    {
        if (IsChecked == true)
        {
            yield return FullPath;
            yield break;
        }

        if (IsChecked == null) // partial
        {
            foreach (var child in Children)
                foreach (var p in child.GetSelectedPaths())
                    yield return p;
        }
        // IsChecked == false: nothing selected
    }
}
