using FluentTaskScheduler.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FluentTaskScheduler.Controls;

/// <summary>The tree of task folders in the navigation pane.</summary>
public sealed partial class FolderTree : UserControl
{
    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(
        nameof(ViewModel), typeof(FoldersViewModel), typeof(FolderTree), new PropertyMetadata(null));

    private FolderNode? _menuFolder;
    private bool _highlighting;

    public FolderTree()
    {
        InitializeComponent();
        Tree.RegisterPropertyChangedCallback(TreeView.SelectedItemProperty, (_, _) => HighlightSelected());
    }

    public FoldersViewModel? ViewModel
    {
        get => (FoldersViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    /// <summary>Raised when the user picks a folder, also when it is the one already selected.</summary>
    public event EventHandler? FolderChosen;

    /// <summary>
    /// Highlights the folder the view model has selected. The tree drops a folder it has no node
    /// for yet, and it makes the node of a subfolder only once it has laid out the item of the
    /// parent: the folder the list opens on is selected before that. So until the node is there,
    /// this looks again after each layout pass.
    /// </summary>
    private void HighlightSelected()
    {
        Tree.LayoutUpdated -= Tree_LayoutUpdated;
        if (_highlighting || ViewModel?.Selected is not { } folder || ReferenceEquals(Tree.SelectedNode?.Content, folder)) return;

        if (NodeOf(folder, Tree.RootNodes) is not { } node)
        {
            Tree.LayoutUpdated += Tree_LayoutUpdated;
            return;
        }
        _highlighting = true;
        try
        {
            Tree.SelectedNode = node;
        }
        finally
        {
            _highlighting = false;
        }
    }

    private void Tree_LayoutUpdated(object? sender, object e) => HighlightSelected();

    private static TreeViewNode? NodeOf(FolderNode folder, IList<TreeViewNode> nodes)
    {
        foreach (TreeViewNode node in nodes)
        {
            if (ReferenceEquals(node.Content, folder)) return node;
            if (NodeOf(folder, node.Children) is { } inside) return inside;
        }
        return null;
    }

    private void Tree_ItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
    {
        if (args.InvokedItem is not FolderNode folder || ViewModel is not { } viewModel) return;
        viewModel.Choose(folder);
        FolderChosen?.Invoke(this, EventArgs.Empty);
    }

    private void FolderMenu_Opening(object? sender, object e)
    {
        _menuFolder = (FolderMenu.Target as FrameworkElement)?.DataContext as FolderNode;
        bool canChange = _menuFolder is { IsRoot: false };
        RenameFolderItem.IsEnabled = canChange;
        DeleteFolderItem.IsEnabled = canChange;
    }

    private async void Reload_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel) await viewModel.ReloadAsync();
    }

    private async void NewTopFolder_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { Roots: [var root, ..] } viewModel) await viewModel.CreateAsync(viewModel.Selected ?? root);
    }

    private async void NewFolder_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel && _menuFolder is { } folder) await viewModel.CreateAsync(folder);
    }

    private async void RenameFolder_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel && _menuFolder is { } folder) await viewModel.RenameAsync(folder);
    }

    private async void DeleteFolder_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel && _menuFolder is { } folder) await viewModel.DeleteAsync(folder);
    }
}
