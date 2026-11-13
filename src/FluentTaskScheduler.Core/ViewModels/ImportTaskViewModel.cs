using CommunityToolkit.Mvvm.ComponentModel;
using FluentTaskScheduler.Localization;
using FluentTaskScheduler.Scheduling;

namespace FluentTaskScheduler.ViewModels;

/// <summary>
/// What the user decides when importing a task from a file: where it goes, what it is called and,
/// for a task that comes from another account or machine, who it runs as here.
/// </summary>
public sealed partial class ImportTaskViewModel : ObservableObject
{
    // Positions in AccountOptions.
    private const int AsExported = 0, AsCurrentUser = 1, AsSystem = 2;

    private readonly ITaskStore _store;
    private readonly IAppLifetime _app;
    private readonly string _xml;
    private readonly TaskImportInfo _info;

    public ImportTaskViewModel(ITaskStore store, IAppLifetime app, string xml, TaskImportInfo info, string suggestedName,
        IReadOnlyList<string> folders, string? currentFolder)
    {
        _store = store;
        _app = app;
        _xml = xml;
        _info = info;
        _name = suggestedName;
        Folders = folders.Count > 0 ? folders : [TaskPaths.Root];
        _folderIndex = Math.Max(Folders.ToList().FindIndex(folder => string.Equals(folder, currentFolder, StringComparison.OrdinalIgnoreCase)), 0);

        AccountOptions =
        [
            Loc.Format("Import.Account.Keep", AccountName),
            Loc.Format("Import.Account.Me", Environment.UserName),
            Loc.Get("Dialog.RunAsSystem")
        ];
        // An account that does not exist here cannot run anything; the importing user is the sensible default.
        _accountIndex = info.AccountExistsHere ? AsExported : AsCurrentUser;
        _highestPrivileges = info.HighestPrivileges;
    }

    [ObservableProperty] private string _name;

    public IReadOnlyList<string> Folders { get; }
    [ObservableProperty] private int _folderIndex;

    /// <summary>The account the task was exported with, as its name or, if that cannot be resolved here, its identifier.</summary>
    public string AccountName => _info.Account.Kind switch
    {
        TaskAccountKind.CurrentUser => Environment.UserName,
        TaskAccountKind.System => "SYSTEM",
        TaskAccountKind.LocalService => "LOCAL SERVICE",
        TaskAccountKind.NetworkService => "NETWORK SERVICE",
        _ => _info.Account.Name
    };

    /// <summary>True when the task's account is unknown on this computer, as for a task exported elsewhere.</summary>
    public bool AccountIsMissing => !_info.AccountExistsHere;

    /// <summary>Keep the exported account, run as the current user, or run as SYSTEM.</summary>
    public IReadOnlyList<string> AccountOptions { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NeedsPassword), nameof(NeedsElevation))]
    private int _accountIndex;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NeedsElevation))]
    private bool _highestPrivileges;

    /// <summary>True when the task keeps an account whose password Windows has to be given.</summary>
    public bool NeedsPassword => AccountIndex == AsExported && _info.StoresPassword;

    [ObservableProperty] private string _password = "";

    /// <summary>True when what is chosen can only be registered with administrator rights, which the app does not have right now.</summary>
    public bool NeedsElevation => !_app.IsElevated && (AccountIndex == AsSystem || HighestPrivileges);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProblem))]
    private string _problem = "";

    public bool HasProblem => Problem.Length > 0;

    /// <summary>Path of the task after a successful <see cref="ImportAsync"/>.</summary>
    public string? ImportedPath { get; private set; }

    partial void OnAccountIndexChanged(int oldValue, int newValue)
    {
        if (newValue < 0 || newValue >= AccountOptions.Count) AccountIndex = oldValue;
    }

    partial void OnFolderIndexChanged(int oldValue, int newValue)
    {
        if (newValue < 0 || newValue >= Folders.Count) FolderIndex = oldValue;
    }

    /// <summary>Registers the task.</summary>
    /// <returns>True when it worked; otherwise <see cref="Problem"/> says why not.</returns>
    public async Task<bool> ImportAsync()
    {
        string name = Name.Trim();
        if (TaskDraftValidator.Validate(new TaskDraft { Name = name, Actions = [new ActionDraft { Path = "." }] }) is [var invalid, ..])
        {
            Problem = invalid.Message;
            return false;
        }
        if (NeedsPassword && Password.Length == 0)
        {
            Problem = Loc.Get("Editor.Error.PasswordNeeded");
            return false;
        }

        var options = new TaskImportOptions(
            Folders[FolderIndex],
            name,
            AccountIndex switch { AsCurrentUser => TaskAccount.CurrentUser, AsSystem => TaskAccount.System, _ => null },
            HighestPrivileges,
            NeedsPassword ? Password : null);

        try
        {
            ImportedPath = await Task.Run(() => _store.Import(_xml, options));
            Problem = "";
            return true;
        }
        catch (TaskAccessDeniedException)
        {
            Problem = Loc.Get(_app.IsElevated ? "Editor.Error.AccessDenied" : "Editor.Error.AccessDeniedNotElevated");
        }
        catch (TaskAccountException ex)
        {
            Problem = Loc.Format("Editor.Error.Account", ex.Message);
        }
        catch (Exception ex)
        {
            // Task Scheduler rejects a definition with whatever COM error fits; the text is the best there is to show.
            Serilog.Log.Error(ex, "Task {Name} could not be imported", name);
            Problem = ex.Message;
        }
        return false;
    }
}
