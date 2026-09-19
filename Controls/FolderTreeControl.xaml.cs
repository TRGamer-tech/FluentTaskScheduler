using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentTaskScheduler.Models;
using FluentTaskScheduler.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FluentTaskScheduler.Controls
{
    /// <summary>
    /// The Task Scheduler folder tree shown in the navigation pane: builds and reloads the tree,
    /// remembers which nodes are expanded, and offers create / rename / delete and drag-and-drop
    /// moves. It talks to the page only through the events below.
    /// </summary>
    public sealed partial class FolderTreeControl : UserControl
    {
        private const string RootPath = "\\";

        private readonly Dictionary<TreeViewNode, TaskFolderModel> _nodeFolders = new();
        private readonly Dictionary<TreeViewNode, long> _expandedCallbackTokens = new();
        private readonly Dictionary<string, bool> _expandedState = new();

        private IFolderService Folders => App.Container.GetRequiredService<IFolderService>();
        private ISettingsService Settings => App.Container.GetRequiredService<ISettingsService>();
        private static string L(string key, string fallback) => LocalizationService.GetString(key, fallback);

        /// <summary>A folder was clicked; the argument is its path.</summary>
        public event EventHandler<string>? FolderInvoked;
        /// <summary>A folder was renamed; the argument is its old path.</summary>
        public event EventHandler<string>? FolderRenamed;
        /// <summary>A folder (and everything in it) was deleted; the argument is its path.</summary>
        public event EventHandler<string>? FolderDeleted;
        /// <summary>Tasks or folders were moved, so the task list must be reloaded.</summary>
        public event EventHandler? ContentMoved;
        /// <summary>An operation failed; the argument is a message for the user.</summary>
        public event EventHandler<string>? ErrorRaised;
        /// <summary>The user tried to start a drag while elevated, where system drag-and-drop is unavailable.</summary>
        public event EventHandler? ElevatedDragBlocked;

        public FolderTreeControl()
        {
            this.InitializeComponent();
            ApplyLocalizedText();
            Loaded += (s, e) => LocalizationService.LanguageChanged += OnLanguageChanged;
            Unloaded += (s, e) => LocalizationService.LanguageChanged -= OnLanguageChanged;
        }

        private void OnLanguageChanged(object? sender, EventArgs e) => ApplyLocalizedText();

        private void ApplyLocalizedText() => FoldersHeader.Text = L("Main.FoldersHeader", "Folders");

        // ── Public API ───────────────────────────────────────────────────────────────

        /// <summary>Paths of every folder currently in the tree.</summary>
        public IEnumerable<string> FolderPaths => _nodeFolders.Values.Select(f => f.Path);

        /// <summary>Upper height limit of the tree, so it scrolls instead of pushing the footer items out of view.</summary>
        public double TreeMaxHeight
        {
            get => FolderTreeView.MaxHeight;
            set => FolderTreeView.MaxHeight = value;
        }

        /// <summary>Whether items can be dropped on the tree using the system drag-and-drop.</summary>
        public bool AllowFolderDrop
        {
            get => FolderTreeView.AllowDrop;
            set => FolderTreeView.AllowDrop = value;
        }

        /// <summary>Removes the highlight from whichever folder is selected in the tree.</summary>
        public void ClearSelection() => FolderTreeView.SelectedItem = null;

        /// <summary>Rebuilds the tree from Task Scheduler, keeping nodes expanded as they were.</summary>
        public void Reload()
        {
            try
            {
                var rootFolder = Folders.GetFolderStructure();

                // Unregister the previous pass's property-changed callbacks before discarding those
                // nodes: RegisterPropertyChangedCallback tokens are otherwise never released, which
                // leaks a callback per folder on every reload.
                foreach (var kv in _expandedCallbackTokens)
                    kv.Key.UnregisterPropertyChangedCallback(TreeViewNode.IsExpandedProperty, kv.Value);
                _expandedCallbackTokens.Clear();

                _nodeFolders.Clear();
                FolderTreeView.RootNodes.Clear();
                AddFolderToTree(rootFolder, null);
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex.ToString()); }
        }

        /// <summary>Expands and selects the node for the given folder path, if it still exists.</summary>
        public void SelectFolder(string path)
        {
            var entry = _nodeFolders.FirstOrDefault(kv => string.Equals(kv.Value.Path, path, StringComparison.OrdinalIgnoreCase));
            if (entry.Key == null) return;

            for (var ancestor = entry.Key.Parent; ancestor != null; ancestor = ancestor.Parent)
                ancestor.IsExpanded = true;

            FolderTreeView.SelectedNode = entry.Key;
        }

        // ── Tree building ────────────────────────────────────────────────────────────

        private void AddFolderToTree(TaskFolderModel folder, TreeViewNode? parentNode)
        {
            var displayName = folder.Name == RootPath ? "Task Scheduler Library" : folder.Name;
            var treeNode = new TreeViewNode
            {
                Content = displayName,
                IsExpanded = _expandedState.TryGetValue(folder.Path, out bool expanded) ? expanded : folder.Path == RootPath
            };

            _nodeFolders[treeNode] = folder;

            // Track expansion state changes
            long token = treeNode.RegisterPropertyChangedCallback(TreeViewNode.IsExpandedProperty, (sender, dp) =>
            {
                if (sender is TreeViewNode node && _nodeFolders.TryGetValue(node, out var f))
                    _expandedState[f.Path] = node.IsExpanded;
            });
            _expandedCallbackTokens[treeNode] = token;

            if (parentNode != null)
                parentNode.Children.Add(treeNode);
            else
                FolderTreeView.RootNodes.Add(treeNode);

            foreach (var sub in folder.SubFolders)
                AddFolderToTree(sub, treeNode);
        }

        private void FolderTreeView_ItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
        {
            if (args.InvokedItem is TreeViewNode node && _nodeFolders.TryGetValue(node, out var folder))
            {
                // The page reacts first (it clears the navigation pane's own selection), then the node is selected.
                FolderInvoked?.Invoke(this, folder.Path);
                FolderTreeView.SelectedItem = node;
            }
        }

        private async void ReloadFolders_Click(object sender, RoutedEventArgs e)
        {
            FolderRefreshIcon.Visibility = Visibility.Collapsed;
            FolderRefreshRing.Visibility = Visibility.Visible;
            FolderRefreshRing.IsActive = true;

            await Task.Run(() =>
            {
                DispatcherQueue.TryEnqueue(() => Reload());
            });

            await Task.Delay(300); // Give a little visual feedback

            FolderRefreshRing.IsActive = false;
            FolderRefreshRing.Visibility = Visibility.Collapsed;
            FolderRefreshIcon.Visibility = Visibility.Visible;
        }

        private void RaiseError(string message) => ErrorRaised?.Invoke(this, message);
    }
}
