using FluentTaskScheduler.Localization;
using Microsoft.UI.Xaml.Controls;

namespace FluentTaskScheduler.Dialogs;

/// <summary>A key combination and what it does.</summary>
public sealed record Shortcut(string Keys, string Action);

/// <summary>The keyboard shortcuts of the task list.</summary>
public sealed partial class ShortcutsDialog : ContentDialog
{
    public ShortcutsDialog() => InitializeComponent();

    // Only a list: the keyboard accelerators in TasksPage.xaml carry the shortcuts out, and this
    // has to be kept in line with them.
    public IReadOnlyList<Shortcut> Shortcuts { get; } =
    [
        new(WithCtrl("N"), Loc.Get("Shortcuts.NewTask")),
        new(WithCtrl("E"), Loc.Get("Shortcuts.Edit")),
        new(WithCtrl("R"), Loc.Get("Shortcuts.Run")),
        new(Loc.Get("Shortcuts.Key.Del"), Loc.Get("Shortcuts.Delete")),
        new("F5", Loc.Get("Shortcuts.Refresh")),
        new("F1", Loc.Get("Shortcuts.Help"))
    ];

    // Keys are named as on the keyboard of the language: Ctrl is Strg on a German one.
    private static string WithCtrl(string key) => $"{Loc.Get("Shortcuts.Key.Ctrl")}+{key}";
}
