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
    private sealed record Destination(Type Page, string TitleKey);

    public const string StartPage = "Tasks";

    private static readonly Dictionary<string, Destination> Destinations = new()
    {
        ["Tasks"] = new(typeof(TasksPage), "Main.Header.ScheduledTasks"),
        ["Dashboard"] = new(typeof(DashboardPage), "Main.Header.Dashboard"),
        ["QuickActions"] = new(typeof(QuickActionsPage), "Main.Header.QuickActions"),
        ["ScriptLibrary"] = new(typeof(LibraryPage), "Main.Header.Library"),
        ["ScriptEditor"] = new(typeof(ScriptEditorPage), "Main.Header.ScriptEditor.Text"),
        ["Settings"] = new(typeof(SettingsPage), "Main.Header.Settings")
    };

    // What the pane's entries take up, so the folder tree above them can use the rest.
    private const double HeightOfPaneEntries = 400;

    private readonly IServiceProvider _windowServices;
    private readonly TasksViewModel _tasks;
    private readonly FoldersViewModel _folders;
    private readonly IDialogService _dialogs;
    private readonly IClipboard _clipboard;

    public ShellPage(IServiceProvider windowServices, ISettingsStore settings, TasksViewModel tasks, FoldersViewModel folders,
        IDialogService dialogs, IClipboard clipboard)
    {
        _windowServices = windowServices;
        _tasks = tasks;
        _folders = folders;
        _dialogs = dialogs;
        _clipboard = clipboard;
        InitializeComponent();
        if (settings.Current.IsOledMode) UseBlackContentAreaInDarkTheme();

        Folders.ViewModel = folders;
        SizeChanged += (_, e) => Folders.MaxHeight = Math.Max(120, e.NewSize.Height - HeightOfPaneEntries);
        Loaded += async (_, _) => await folders.ReloadAsync();
        Loaded += async (_, _) => await windowServices.GetRequiredService<StartupFlow>().RunAsync();
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

    public void ShowScriptEditor() => Navigate("ScriptEditor");

    /// <summary>Switches to the task list and opens the dialog that snoozes all tasks until a moment of the user's choosing.</summary>
    public void ShowSnoozeDialog()
    {
        Navigate("Tasks");
        _ = _windowServices.GetRequiredService<TasksPage>().ShowSnoozeDialogAsync();
    }

    /// <summary>Tells the user in this window what went wrong elsewhere, e.g. in the notification area.</summary>
    public Task ShowErrorAsync(string message) => _dialogs.ShowErrorAsync(message);

    /// <summary>Shows what an unhandled exception said, once the dialog the window may be showing has closed.</summary>
    public Task ShowCrashReportAsync(string details)
    {
        var dialog = new ContentDialog
        {
            Title = Loc.Get("Dialog.Crash.Title"),
            PrimaryButtonText = Loc.Get("Dialog.Crash.Copy"),
            CloseButtonText = Loc.Get("Dialog.Common.Close"),
            Content = new ScrollViewer
            {
                MaxHeight = 320,
                Content = new TextBlock
                {
                    Text = details,
                    FontFamily = new FontFamily("Consolas"),
                    IsTextSelectionEnabled = true,
                    TextWrapping = TextWrapping.Wrap
                }
            }
        };
        // Should another program hold the clipboard, the report stays open to be copied again.
        dialog.PrimaryButtonClick += (_, args) => args.Cancel = !_clipboard.SetText(details);
        return _dialogs.ShowAsync(dialog);
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
        PageHost.Content = BuildPage(destination.Page, tag);
        CurrentPage = tag;
        NavView.Header = Loc.Get(destination.TitleKey);
    }

    private object BuildPage(Type page, string tag)
    {
        try
        {
            return _windowServices.GetRequiredService(page);
        }
        catch (Exception ex)
        {
            // Asked for through UI Automation, by a screen reader for instance, the failure goes
            // back to the caller as a bare error code and never reaches the app's crash handler.
            Serilog.Log.Error(ex, "The {Page} page could not be built", tag);
            throw;
        }
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
