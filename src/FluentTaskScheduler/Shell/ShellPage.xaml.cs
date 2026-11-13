using System.ComponentModel;
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

    // Selections of a pane entry that the shell makes itself and that are still under way.
    private int _selectionsFromCode;

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
        tasks.PropertyChanged += Tasks_PropertyChanged;
        Loaded += async (_, _) => await folders.ReloadAsync();
        Loaded += async (_, _) => await windowServices.GetRequiredService<StartupFlow>().RunAsync();
    }

    /// <summary>The element that acts as the window's title bar.</summary>
    public UIElement TitleBarElement => TitleBar;

    /// <summary>Tag of the page that is showing.</summary>
    public string CurrentPage { get; private set; } = "";

    /// <summary>
    /// Shows the page with the given tag in a window that has just been built. The task list shows
    /// what the user chose last, in this window or another: the tasks of one folder, or of all.
    /// </summary>
    public void Open(string tag)
    {
        _folders.ShowLastChoice();
        if (tag == "Tasks") ShowTaskList();
        else Navigate(tag);
    }

    /// <summary>Shows the page with the given tag and selects its entry in the pane.</summary>
    public void Navigate(string tag)
    {
        if (!Destinations.ContainsKey(tag)) tag = StartPage;
        SelectInPane(NavView.FooterMenuItems.OfType<NavigationViewItem>().First(item => (string?)item.Tag == tag));
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
            Content = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    new TextBlock { Text = Loc.Get("Dialog.Crash.Message"), TextWrapping = TextWrapping.Wrap },
                    new ScrollViewer
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

    private void Folders_FolderChosen(object? sender, EventArgs e) => ShowTaskList();

    // The list also falls back to all tasks by itself: when the folder it shows is gone, or to show
    // a task of another folder. The pane's entry for all tasks is then selected as well.
    private void Tasks_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TasksViewModel.FolderPath) && _tasks.FolderPath is null && CurrentPage == "Tasks") SelectInPane(NavAllTasks);
    }

    /// <summary>Shows the task list as it is, with the tasks of one folder or of all, and the pane in step with it.</summary>
    private void ShowTaskList()
    {
        if (_tasks.FolderPath is null)
        {
            Navigate("Tasks");
            return;
        }

        // The task list of one folder is no entry of the pane; none of them is selected then.
        SelectInPane(null);
        Show("Tasks");
    }

    /// <summary>Selects an entry of the pane, or none, as the shell's doing rather than the user's.</summary>
    private void SelectInPane(NavigationViewItem? item)
    {
        _selectionsFromCode++;
        try
        {
            NavView.SelectedItem = item;
        }
        finally
        {
            _selectionsFromCode--;
        }
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
        if (ReferenceEquals(args.InvokedItemContainer, NavAllTasks))
        {
            // Windows App SDK 1.5 also reports the shell's own selection of the entry as invoked
            // after a click in the pane that selected nothing: New task, or the entry selected already.
            if (_selectionsFromCode == 0) _folders.ChooseAllTasks();
        }
        else if (ReferenceEquals(args.InvokedItemContainer, NavAdd))
        {
            // The new task goes into the folder the list shows.
            ShowTaskList();
            await _windowServices.GetRequiredService<TasksPage>().CreateTaskAsync();
        }
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
