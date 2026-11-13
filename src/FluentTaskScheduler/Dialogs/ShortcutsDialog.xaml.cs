using FluentTaskScheduler.Localization;
using Microsoft.UI.Xaml.Controls;

namespace FluentTaskScheduler.Dialogs;

/// <summary>A key combination and what it does.</summary>
public sealed record Shortcut(string Keys, string Action);

/// <summary>The keyboard shortcuts of the task list.</summary>
public sealed partial class ShortcutsDialog : ContentDialog
{
    public ShortcutsDialog() => InitializeComponent();

    // Kept next to the accelerators in TasksPage.xaml, which are what really carries them out.
    public IReadOnlyList<Shortcut> Shortcuts { get; } =
    [
        new("Ctrl+N", Loc.Get("Shortcuts.NewTask")),
        new("Ctrl+E", Loc.Get("Shortcuts.Edit")),
        new("Ctrl+R", Loc.Get("Shortcuts.Run")),
        new("Del", Loc.Get("Shortcuts.Delete")),
        new("F5", Loc.Get("Shortcuts.Refresh")),
        new("F1", Loc.Get("Shortcuts.Help"))
    ];
}
