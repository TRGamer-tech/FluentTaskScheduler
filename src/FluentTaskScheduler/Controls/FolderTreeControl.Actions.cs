using System;
using FluentTaskScheduler.Helpers;
using FluentTaskScheduler.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;

namespace FluentTaskScheduler.Controls
{
    /// <summary>Folder context menu and the create / rename / delete dialogs.</summary>
    public sealed partial class FolderTreeControl
    {
        private void CreateRootFolder_Click(object sender, RoutedEventArgs e) => CreateFolder(RootPath);

        private void FolderTreeViewItem_RightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            var fe = e.OriginalSource as FrameworkElement;
            if (fe == null) return;

            var tvi = VisualTreeUtil.FindParent<TreeViewItem>(fe);
            if (tvi != null)
            {
                var node = FolderTreeView.NodeFromContainer(tvi);
                if (node != null && _nodeFolders.TryGetValue(node, out var folder))
                {
                    ShowFolderContextMenu(fe, e.GetPosition(fe), folder);
                }
            }
        }

        private void ShowFolderContextMenu(FrameworkElement targetElement, Windows.Foundation.Point position, TaskFolderModel folder)
        {
            var flyout = new MenuFlyout();

            var newFolderItem = new MenuFlyoutItem { Text = L("FolderMenu.NewSubfolder", "New Subfolder"), Icon = new SymbolIcon(Symbol.Add) };
            newFolderItem.Click += (s, args) => CreateFolder(folder.Path);
            flyout.Items.Add(newFolderItem);

            if (folder.Path != RootPath)
            {
                var renameItem = new MenuFlyoutItem { Text = L("FolderMenu.Rename", "Rename"), Icon = new SymbolIcon(Symbol.Rename) };
                renameItem.Click += (s, args) => RenameFolder(folder.Path, folder.Name);
                flyout.Items.Add(renameItem);

                var deleteItem = new MenuFlyoutItem { Text = L("FolderMenu.Delete", "Delete"), Icon = new SymbolIcon(Symbol.Delete) };
                deleteItem.Click += (s, args) => DeleteFolder(folder.Path);
                flyout.Items.Add(deleteItem);
            }

            flyout.ShowAt(targetElement, new FlyoutShowOptions { Position = position });
        }

        private async void CreateFolder(string parentPath)
        {
            var dialog = new ContentDialog
            {
                Title = L("Dialog.NewFolder.Title", "New Folder"),
                Content = new TextBox { PlaceholderText = L("Dialog.NewFolder.NamePlaceholder", "Name") },
                PrimaryButtonText = L("Dialog.Common.Create", "Create"),
                CloseButtonText = L("Dialog.Common.Cancel", "Cancel"),
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.XamlRoot,
                RequestedTheme = Settings.Theme
            };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary && dialog.Content is TextBox tb && !string.IsNullOrWhiteSpace(tb.Text))
            {
                try
                {
                    Folders.CreateFolder(parentPath == RootPath ? "\\" + tb.Text : parentPath + "\\" + tb.Text);
                    Reload();
                }
                catch (Exception ex)
                {
                    RaiseError(ex.Message);
                }
            }
        }

        private async void RenameFolder(string path, string oldName)
        {
            var tb = new TextBox { Text = oldName, PlaceholderText = L("Dialog.RenameFolder.NewNamePlaceholder", "New Name") };
            tb.SelectAll();

            var dialog = new ContentDialog
            {
                Title = L("Dialog.RenameFolder.Title", "Rename Folder"),
                Content = tb,
                PrimaryButtonText = L("Dialog.Common.Rename", "Rename"),
                CloseButtonText = L("Dialog.Common.Cancel", "Cancel"),
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.XamlRoot,
                RequestedTheme = Settings.Theme
            };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(tb.Text) && tb.Text != oldName)
            {
                try
                {
                    Folders.RenameFolder(path, tb.Text);
                    Reload();
                    FolderRenamed?.Invoke(this, path);
                }
                catch (Exception ex)
                {
                    RaiseError(ex.Message);
                }
            }
        }

        private async void DeleteFolder(string path)
        {
            var dialog = new ContentDialog
            {
                Title = L("Dialog.DeleteFolder.Title", "Delete Folder"),
                Content = string.Format(L("Dialog.DeleteFolder.ContentFormat", "Delete '{0}' and ALL tasks in it?"), path),
                PrimaryButtonText = L("Dialog.Common.Delete", "Delete"),
                CloseButtonText = L("Dialog.Common.Cancel", "Cancel"),
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.XamlRoot,
                RequestedTheme = Settings.Theme
            };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                try
                {
                    Folders.DeleteFolder(path);
                    Reload();
                    FolderDeleted?.Invoke(this, path);
                }
                catch (Exception ex)
                {
                    RaiseError(ex.Message);
                }
            }
        }
    }
}
