using FluentTaskScheduler.Localization;
using FluentTaskScheduler.Scheduling;
using FluentTaskScheduler.Settings;
using FluentTaskScheduler.ViewModels;
using FluentTaskScheduler.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace FluentTaskScheduler.Shell;

/// <summary>The frame of a window: the navigation pane and the page shown next to it.</summary>
public sealed partial class ShellPage : Page, IShellNavigator
{
    /// <param name="Page">The page type; resolved from the window's service scope.</param>
    /// <param name="TitleKey">Resource name of the heading shown above the page.</param>
    /// <param name="CreatedByFrame">True for pages that still take their dependencies from the old service locator and rely on Frame navigation events.</param>
    private sealed record Destination(Type Page, string TitleKey, bool CreatedByFrame = false);

    public const string StartPage = "Tasks";

    private static readonly Dictionary<string, Destination> Destinations = new()
    {
        ["Tasks"] = new(typeof(TasksPage), "Main.Header.ScheduledTasks"),
        ["Dashboard"] = new(typeof(DashboardPage), "Main.Header.Dashboard", CreatedByFrame: true),
        ["QuickActions"] = new(typeof(QuickActionsPage), "Main.Header.QuickActions", CreatedByFrame: true),
        ["ScriptLibrary"] = new(typeof(ScriptLibraryPage), "Main.Header.Library", CreatedByFrame: true),
        ["ScriptEditor"] = new(typeof(ScriptEditorPage), "Main.Header.ScriptEditor.Text", CreatedByFrame: true),
        ["Settings"] = new(typeof(SettingsPage), "Main.Header.Settings", CreatedByFrame: true)
    };

    // What the pane's entries take up, so the folder tree above them can use the rest.
    private const double HeightOfPaneEntries = 400;

    private readonly IServiceProvider _windowServices;
    private readonly TasksViewModel _tasks;
    private readonly FoldersViewModel _folders;

    public ShellPage(IServiceProvider windowServices, ISettingsStore settings, TasksViewModel tasks, FoldersViewModel folders)
    {
        _windowServices = windowServices;
        _tasks = tasks;
        _folders = folders;
        InitializeComponent();
        if (settings.Current.IsOledMode) UseBlackContentAreaInDarkTheme();

        Folders.ViewModel = folders;
        SizeChanged += (_, e) => Folders.MaxHeight = Math.Max(120, e.NewSize.Height - HeightOfPaneEntries);
        Loaded += async (_, _) => await folders.ReloadAsync();
    }

    /// <summary>The element that acts as the window's title bar.</summary>
    public UIElement TitleBarElement => TitleBar;

    /// <summary>Tag of the page that is showing.</summary>
    public string CurrentPage { get; private set; } = "";

    /// <summary>Shows the page with the given tag and selects its entry in the pane.</summary>
    public void Navigate(string tag)
    {
        if (!Destinations.ContainsKey(tag)) tag = StartPage;
        NavView.SelectedItem = NavView.FooterMenuItems.OfType<NavigationViewItem>().First(item => (string?)item.Tag == tag);
    }

    /// <param name="backdrop">One of the states in BackdropStates: Solid, Mica or Oled.</param>
    public void ShowBackdrop(string backdrop) => VisualStateManager.GoToState(this, backdrop, useTransitions: false);

    public void ShowTask(string taskPath)
    {
        Navigate("Tasks");
        _ = _windowServices.GetRequiredService<TasksPage>().ShowAsync(taskPath);
    }

    public Task<string?> CreateTaskAsync(TaskDraft template)
    {
        Navigate("Tasks");
        return _windowServices.GetRequiredService<TasksPage>().CreateTaskAsync(template);
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem { Tag: string tag }) return;

        if (tag == "Tasks")
        {
            // "All tasks" means all folders.
            _folders.Selected = null;
            _tasks.FolderPath = null;
        }
        Show(tag);
    }

    private void Folders_FolderChosen(object? sender, EventArgs e)
    {
        // The task list of one folder is no entry of the pane; none of them is selected then.
        NavView.SelectedItem = null;
        Show("Tasks");
    }

    private void Show(string tag)
    {
        if (tag == CurrentPage) return;

        Destination destination = Destinations[tag];
        CurrentPage = tag;
        NavView.Header = Loc.Get(destination.TitleKey);
        if (destination.CreatedByFrame) PageHost.Navigate(destination.Page);
        else PageHost.Content = _windowServices.GetRequiredService(destination.Page);
    }

    private async void NavView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (!ReferenceEquals(args.InvokedItemContainer, NavAdd)) return;
        Navigate("Tasks");
        await _windowServices.GetRequiredService<TasksPage>().CreateTaskAsync();
    }

    // The area next to the pane normally sits one layer above the window background. OLED mode
    // wants it black as well; overriding the brush for the dark theme only keeps the light theme
    // as it is. The control reads the brush when its template loads, so this has to happen first.
    private void UseBlackContentAreaInDarkTheme()
    {
        var dark = new ResourceDictionary { ["NavigationViewContentBackground"] = new SolidColorBrush(Colors.Black) };
        NavView.Resources.ThemeDictionaries["Dark"] = dark;
    }
}
