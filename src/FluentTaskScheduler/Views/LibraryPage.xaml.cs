using FluentTaskScheduler.Localization;
using FluentTaskScheduler.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FluentTaskScheduler.Views;

/// <summary>The library: task templates and scripts, one of the two at a time.</summary>
public sealed partial class LibraryPage : Page
{
    public LibraryPage(LibraryViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
    }

    public LibraryViewModel ViewModel { get; }

    /// <summary>The UI automation id of the main button of a card, e.g. "Template.Defrag" or "Script.FlushDns".</summary>
    public static string IdOf(string kind, string id) => $"{kind}.{id}";

    /// <summary>
    /// For x:Bind: what a screen reader calls a button of a card, with the name of what it acts on,
    /// e.g. "Delete script Backup". Every card has the same buttons, which this tells apart.
    /// </summary>
    public static string ButtonName(string format, string name) => Loc.Format(format, name);

    private void Sections_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        // The first section is selected while the page is still being put together; the views start out that way.
        if (ScriptsView is null) return;

        bool scripts = sender.Items.IndexOf(sender.SelectedItem) == 1;
        TemplatesView.Visibility = scripts ? Visibility.Collapsed : Visibility.Visible;
        ScriptsView.Visibility = scripts ? Visibility.Visible : Visibility.Collapsed;
    }
}
