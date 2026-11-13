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

    public FolderTree() => InitializeComponent();

    public FoldersViewModel? ViewModel
    {
        get => (FoldersViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    /// <summary>Raised when the user picks a folder, also when it is the one already selected.</summary>
    public event EventHandler? FolderChosen;

    private void Tree_ItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
    {
        if (args.InvokedItem is not FolderNode folder || ViewModel is not { } viewModel) return;
        viewModel.Selected = folder;
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
