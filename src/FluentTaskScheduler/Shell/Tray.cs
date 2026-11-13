using FluentTaskScheduler.Localization;
using FluentTaskScheduler.Scheduling;
using FluentTaskScheduler.Settings;
using FluentTaskScheduler.Snoozing;
using FluentTaskScheduler.ViewModels;

namespace FluentTaskScheduler.Shell;

/// <summary>
/// The app in the notification area, for as long as the setting for it is on: an icon that shows
/// how many tasks are running and whether all of them are snoozed, brings hidden windows back,
/// and offers what can be done without a window.
/// </summary>
internal sealed class Tray(WindowManager windows, ISettingsStore settings, ITaskStore store, ISnoozeService snooze, IUiThread ui, TimeProvider time)
    : IDisposable
{
    private static readonly TimeSpan CountInterval = TimeSpan.FromSeconds(10);
    private static readonly string IconFile = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");

    private TrayIcon? _icon;
    private ITimer? _counting;
    private int _runningTasks;

    /// <summary>Call on the UI thread.</summary>
    public void Start()
    {
        settings.Changed += OnSettingsChanged;
        snooze.Changed += OnSnoozeChanged;
        Apply();
    }

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs change)
    {
        if (change.Previous.EnableTrayIcon != change.Current.EnableTrayIcon || change.Previous.Language != change.Current.Language) ui.Post(Apply);
    }

    // The service reports on whatever thread made the change.
    private void OnSnoozeChanged(object? sender, EventArgs e) => ui.Post(() =>
    {
        if (_icon is null) return;
        Redraw();
        _icon.SetTooltip(Tooltip());
    });

    private void Apply()
    {
        if (!settings.Current.EnableTrayIcon)
        {
            Remove();
            // Without the icon there would be no way to get a hidden window back.
            windows.ShowAllHidden();
            return;
        }

        if (_icon is null)
        {
            var icon = new TrayIcon();
            icon.Clicked += windows.ShowLastHidden;
            icon.MenuRequested += () => icon.ShowMenu(BuildMenu());
            _icon = icon;
            Redraw();
            icon.Show();
            _counting = time.CreateTimer(_ => CountRunningTasks(), null, TimeSpan.Zero, CountInterval);
        }
        _icon.SetTooltip(Tooltip());
    }

    /// <summary>The app's name and, while all tasks are snoozed, until when, on a line of its own. Windows shows at most 127 characters of it.</summary>
    private string Tooltip() =>
        snooze.State.Global is { } global
            ? Loc.Get("App.WindowTitle") + "\n" + SnoozeText.AllTasks(global.Window, time)
            : Loc.Get("App.WindowTitle");

    private List<TrayMenuItem> BuildMenu()
    {
        var items = new List<TrayMenuItem>();
        foreach (WindowManager.HiddenWindow window in windows.HiddenWindows)
        {
            items.Add(new(Loc.Format("Tray.ShowWindowFormat", window.Title), window.Show));
            items.Add(new(Loc.Format("Tray.CloseWindowFormat", window.Title), window.Close));
        }
        if (items.Count == 0) items.Add(new(Loc.Get("Tray.Open"), windows.ShowMainWindow));

        items.Add(TrayMenuItem.Separator);
        items.AddRange(SnoozeItems());
        items.Add(TrayMenuItem.Separator);
        items.Add(new(Loc.Get("Tray.NewWindow"), windows.OpenWindow));
        items.Add(TrayMenuItem.Separator);
        items.Add(new(Loc.Get("Tray.ExitAll"), windows.CloseAll));
        return items;
    }

    // While all tasks are snoozed: until when, as a greyed caption, and the way out. Otherwise: for how long.
    private IEnumerable<TrayMenuItem> SnoozeItems()
    {
        if (snooze.State.Global is { } global)
            return [new(SnoozeText.AllTasks(global.Window, time)), new(Loc.Get("Snooze.Menu.Resume"), () => _ = snooze.ResumeAllAsync())];

        List<TrayMenuItem> lengths =
        [
            .. SnoozeChoice.All.Select(choice => new TrayMenuItem(choice.Label, () => SnoozeAll(choice.End))),
            TrayMenuItem.Separator,
            new(Loc.Get("Snooze.Duration.Custom"), ShowSnoozeDialog)
        ];
        return [new(Loc.Get("Snooze.Menu.SnoozeAll"), Children: lengths)];
    }

    private async void SnoozeAll(SnoozeEnd end)
    {
        try
        {
            await snooze.SnoozeAllAsync(end);
        }
        catch (Exception ex) when (SnoozeText.WhyNotSnoozed(ex) is { } why)
        {
            // Nothing was snoozed. The icon cannot say why; the window can.
            windows.ShowMainWindow();
            if (windows.MainShell is { } shell) await shell.ShowErrorAsync(why);
        }
    }

    private void ShowSnoozeDialog()
    {
        windows.ShowMainWindow();
        windows.MainShell?.ShowSnoozeDialog();
    }

    // Runs on a timer thread. Asking Task Scheduler directly keeps the number right whether or
    // not the history log is on, and whoever started the tasks.
    private void CountRunningTasks()
    {
        int count;
        try
        {
            count = store.CountRunningTasks();
        }
        catch (Exception ex)
        {
            // A timer callback that throws ends the process, and no count is worth that.
            Serilog.Log.Debug(ex, "The running tasks could not be counted");
            return;
        }

        if (Interlocked.Exchange(ref _runningTasks, count) != count) ui.Post(Redraw);
    }

    private void Redraw()
    {
        if (_icon is null) return;
        try
        {
            _icon.SetIcon(TrayIconImage.Create(IconFile, _runningTasks, snoozed: snooze.State.Global is not null));
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or System.Runtime.InteropServices.ExternalException)
        {
            Serilog.Log.Warning(ex, "The notification area icon could not be drawn");
        }
    }

    private void Remove()
    {
        _counting?.Dispose();
        _counting = null;
        _icon?.Dispose();
        _icon = null;
    }

    public void Dispose()
    {
        settings.Changed -= OnSettingsChanged;
        snooze.Changed -= OnSnoozeChanged;
        Remove();
    }
}
