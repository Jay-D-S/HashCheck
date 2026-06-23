using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace HashCheck.ViewModels;

/// <summary>Tree node representing a drive or folder in the scope-selection tree. Supports tri-state checked state (true/false/null for partial) with bi-directional propagation to children and parent.</summary>
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
            // Propagate definite states down to children; null (partial) is only set by UpdateFromChildren.
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
            // Set the backing field directly to avoid re-triggering propagation loops
            child._isChecked = value;
            child.OnPropertyChanged(nameof(IsChecked));
            child.PropagateToChildren(value);
        }
    }

    /// <summary>Recomputes this node's checked state from its children's states and propagates upward.</summary>
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

    /// <summary>Yields the absolute paths of all selected (or partially-selected) nodes in this subtree. A fully-checked node returns its own path; a partially-checked node recurses into children.</summary>
    public IEnumerable<string> GetSelectedPaths()
    {
        if (IsChecked == true)
        {
            yield return FullPath;
            yield break;
        }

        if (IsChecked == null) // partial — recurse to find the individually-checked children
        {
            foreach (var child in Children)
                foreach (var p in child.GetSelectedPaths())
                    yield return p;
        }
        // IsChecked == false: nothing selected under this node
    }
}
