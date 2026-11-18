using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentTaskScheduler.Localization;
using FluentTaskScheduler.Scheduling;
using FluentTaskScheduler.Settings;
using FluentTaskScheduler.Snoozing;
using TaskInstancesPolicy = Microsoft.Win32.TaskScheduler.TaskInstancesPolicy;

namespace FluentTaskScheduler.ViewModels;

/// <summary>Creates task editors for one window.</summary>
public sealed class TaskEditorFactory(ITaskStore store, ISettingsStore settings, IAppLifetime app, IFilePicker files, INetworkProfiles networks,
    TimeProvider time, ISnoozeService snooze)
{
    /// <param name="source">The task as it was loaded, or what a new task starts out as.</param>
    /// <param name="folderPath">The folder the task is in, or a new task is created in.</param>
    /// <param name="originalPath">Path of the task being edited; null for a new task.</param>
    /// <param name="otherTasks">Paths of the other tasks, which this one can start when it finishes.</param>
    public TaskEditorViewModel Create(TaskDraft source, string folderPath, string? originalPath, IEnumerable<string> otherTasks) =>
        new(store, settings, app, files, networks, time, snooze, source, folderPath, originalPath, otherTasks);
}

/// <summary>
/// What the task editor shows and changes. The controls bind to its properties; saving builds a
/// <see cref="TaskDraft"/> from them on top of the task as it was loaded, so everything the user
/// left alone is written back unchanged.
/// </summary>
public sealed partial class TaskEditorViewModel : ObservableObject
{
    /// <summary>The priorities offered, in the order of the list.</summary>
    public static ValueList<ProcessPriorityClass> Priorities { get; } =
    [
        ProcessPriorityClass.RealTime, ProcessPriorityClass.High, ProcessPriorityClass.AboveNormal,
        ProcessPriorityClass.Normal, ProcessPriorityClass.BelowNormal, ProcessPriorityClass.Idle
    ];

    /// <summary>What can happen when the task is started while it is already running, in the order of the list.</summary>
    public static ValueList<TaskInstancesPolicy> InstancePolicies { get; } =
        [TaskInstancesPolicy.IgnoreNew, TaskInstancesPolicy.Parallel, TaskInstancesPolicy.Queue, TaskInstancesPolicy.StopExisting];

    // Positions in AccountOptions.
    private const int CurrentUser = 0, OtherUser = 1, System = 2, AccountAsItIs = 3;

    private readonly ITaskStore _store;
    private readonly ISettingsStore _settings;
    private readonly IAppLifetime _app;
    private readonly IFilePicker _files;
    private readonly TimeProvider _time;
    private readonly ISnoozeService _snooze;
    private readonly TaskDraft _source;
    private readonly string _folderPath;
    private readonly string? _originalPath;
    private readonly string _shownTags, _shownIdleDuration, _shownRestartInterval, _shownTimeLimit;
    private readonly List<NetworkProfile> _networks;

    internal TaskEditorViewModel(ITaskStore store, ISettingsStore settings, IAppLifetime app, IFilePicker files, INetworkProfiles networks,
        TimeProvider time, ISnoozeService snooze, TaskDraft source, string folderPath, string? originalPath, IEnumerable<string> otherTasks)
    {
        _store = store;
        _settings = settings;
        _app = app;
        _files = files;
        _time = time;
        _snooze = snooze;
        _source = source;
        _folderPath = folderPath;
        _originalPath = originalPath;

        _name = source.Name;
        _description = source.Description;
        _author = source.Author;
        _category = source.Category;
        _tags = _shownTags = string.Join(", ", source.Tags);
        _enabled = source.Enabled;

        DateTime now = time.GetLocalNow().DateTime;
        Triggers = new ObservableCollection<TriggerViewModel>(source.Triggers.Select(trigger => new TriggerViewModel(trigger, now)));
        Actions = new ObservableCollection<ActionViewModel>(source.Actions.Select(action => new ActionViewModel(action)));
        _selectedTrigger = Triggers.FirstOrDefault();
        _selectedAction = Actions.FirstOrDefault();
        CompletionActions = new CompletionActionsViewModel(source.CompletionActions,
            otherTasks.Where(path => !string.Equals(path, originalPath, StringComparison.OrdinalIgnoreCase)));

        _runOnlyIfIdle = source.RunOnlyIfIdle;
        _idleDuration = _shownIdleDuration = DurationText.Format(source.IdleDuration);
        _stopOnIdleEnd = source.StopOnIdleEnd;
        _runOnlyOnAcPower = source.RunOnlyOnAcPower;
        _stopOnBattery = source.StopOnBattery;
        _runOnlyIfNetwork = source.RunOnlyIfNetwork;
        _wakeToRun = source.WakeToRun;

        // The task's own network stays selectable even when the list cannot be read.
        _networks = networks.GetAll().ToList();
        if (source.NetworkId != Guid.Empty && _networks.All(network => network.Id != source.NetworkId))
            _networks.Add(new NetworkProfile(source.NetworkId, source.NetworkName.Length > 0 ? source.NetworkName : source.NetworkId.ToString()));
        NetworkOptions = [Loc.Get("Main.Network.Any"), .. _networks.Select(network => network.Name)];
        _networkIndex = _networks.FindIndex(network => network.Id == source.NetworkId) + 1;

        _highestPrivileges = source.HighestPrivileges;
        _hidden = source.Hidden;
        _runIfMissed = source.RunIfMissed;
        _instancePolicyIndex = Math.Max(InstancePolicies.ToList().IndexOf(source.MultipleInstances), 0);
        _priorityIndex = Math.Max(Priorities.ToList().IndexOf(source.Priority), 0);
        _deleteWhenExpired = source.DeleteWhenExpired;
        _allowHardTerminate = source.AllowHardTerminate;
        _restartOnFailure = source.RestartCount > 0;
        _restartCount = source.RestartCount > 0 ? source.RestartCount : 3;
        _restartInterval = _shownRestartInterval = DurationText.Format(source.RestartInterval);
        _limitRunTime = source.ExecutionTimeLimit > TimeSpan.Zero;
        _timeLimit = _shownTimeLimit = DurationText.Format(source.ExecutionTimeLimit);

        var options = new List<string> { Loc.Get("Dialog.RunAsCurrent"), Loc.Get("Dialog.RunAsSpecific"), Loc.Get("Dialog.RunAsSystem") };
        (_accountIndex, _userName) = source.Account.Kind switch
        {
            TaskAccountKind.CurrentUser => (CurrentUser, ""),
            TaskAccountKind.User => (OtherUser, source.Account.Name),
            TaskAccountKind.System => (System, ""),
            _ => (AccountAsItIs, "")
        };
        if (_accountIndex == AccountAsItIs) options.Add(Loc.Format("Editor.Account.Keep", AccountName(source.Account)));
        AccountOptions = options;
    }

    public bool IsNew => _originalPath is null;
    public string Title => IsNew ? Loc.Get("Editor.Title.New") : Loc.Format("Editor.Title.Edit", _source.Name);

    /// <summary>Path of the task after a successful <see cref="SaveAsync"/>.</summary>
    public string? SavedPath { get; private set; }

    /// <summary>
    /// Set by a <see cref="SaveAsync"/> that gave the task a new name, and with it a new path,
    /// which the completion actions of other tasks that start it have to follow.
    /// </summary>
    public TaskMove? Renamed { get; private set; }

    [ObservableProperty] private string _name;
    [ObservableProperty] private string _description;
    [ObservableProperty] private string _author;
    [ObservableProperty] private string _category;

    /// <summary>The tags as the user types them, separated by commas.</summary>
    [ObservableProperty] private string _tags;

    [ObservableProperty] private bool _enabled;

    public ObservableCollection<TriggerViewModel> Triggers { get; }
    public ObservableCollection<ActionViewModel> Actions { get; }
    public CompletionActionsViewModel CompletionActions { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedTrigger))]
    [NotifyCanExecuteChangedFor(nameof(RemoveTriggerCommand), nameof(MoveTriggerUpCommand), nameof(MoveTriggerDownCommand))]
    private TriggerViewModel? _selectedTrigger;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedAction))]
    [NotifyCanExecuteChangedFor(nameof(RemoveActionCommand), nameof(MoveActionUpCommand), nameof(MoveActionDownCommand), nameof(BrowseForProgramCommand))]
    private ActionViewModel? _selectedAction;

    public bool HasSelectedTrigger => SelectedTrigger is not null;
    public bool HasSelectedAction => SelectedAction is not null;

    [ObservableProperty] private bool _runOnlyIfIdle;
    [ObservableProperty] private string _idleDuration;
    [ObservableProperty] private bool _stopOnIdleEnd;
    [ObservableProperty] private bool _runOnlyOnAcPower;
    [ObservableProperty] private bool _stopOnBattery;
    [ObservableProperty] private bool _runOnlyIfNetwork;
    [ObservableProperty] private bool _wakeToRun;

    /// <summary>"Any network" followed by the known networks.</summary>
    public IReadOnlyList<string> NetworkOptions { get; }
    [ObservableProperty] private int _networkIndex;

    /// <summary>True when a particular network cannot be chosen because the list of networks needs administrator rights.</summary>
    public bool NetworkListNeedsElevation => _networks.Count == 0 && !_app.IsElevated;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NeedsElevation))]
    private bool _highestPrivileges;

    [ObservableProperty] private bool _hidden;
    [ObservableProperty] private bool _runIfMissed;
    [ObservableProperty] private int _instancePolicyIndex;
    [ObservableProperty] private int _priorityIndex;
    [ObservableProperty] private bool _deleteWhenExpired;
    [ObservableProperty] private bool _allowHardTerminate;
    [ObservableProperty] private bool _restartOnFailure;
    [ObservableProperty] private double _restartCount;
    [ObservableProperty] private string _restartInterval;

    /// <summary>Whether the task is stopped after running for <see cref="TimeLimit"/>.</summary>
    [ObservableProperty] private bool _limitRunTime;
    [ObservableProperty] private string _timeLimit;

    /// <summary>Who the task can run as: the current user, a named user, SYSTEM, and for a task that runs as something else, that.</summary>
    public IReadOnlyList<string> AccountOptions { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RunsAsOtherUser), nameof(NeedsElevation), nameof(NeedsPassword))]
    private int _accountIndex;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NeedsPassword))]
    private string _userName;

    [ObservableProperty] private string _password = "";

    public bool RunsAsOtherUser => AccountIndex == OtherUser;

    /// <summary>True when what is chosen can only be saved with administrator rights, which the app does not have right now.</summary>
    public bool NeedsElevation => !_app.IsElevated && (AccountIndex == System || HighestPrivileges);

    /// <summary>
    /// True when Windows keeps a password for the task's account. It asks for that password again
    /// whenever the task is changed, unless the account is changed too.
    /// </summary>
    public bool NeedsPassword => _source.StoresPassword && ChosenAccount() == _source.Account;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProblems))]
    private string _problems = "";

    public bool HasProblems => Problems.Length > 0;

    // A list control reports -1 while it has nothing selected. That is not a choice.
    partial void OnPriorityIndexChanged(int oldValue, int newValue)
    {
        if (newValue < 0 || newValue >= Priorities.Count) PriorityIndex = oldValue;
    }

    partial void OnInstancePolicyIndexChanged(int oldValue, int newValue)
    {
        if (newValue < 0 || newValue >= InstancePolicies.Count) InstancePolicyIndex = oldValue;
    }

    partial void OnAccountIndexChanged(int oldValue, int newValue)
    {
        if (newValue < 0 || newValue >= AccountOptions.Count) AccountIndex = oldValue;
    }

    partial void OnNetworkIndexChanged(int oldValue, int newValue)
    {
        if (newValue < 0 || newValue >= NetworkOptions.Count) NetworkIndex = oldValue;
    }

    partial void OnLimitRunTimeChanged(bool value)
    {
        // Three days is what Task Scheduler itself suggests.
        if (value && string.IsNullOrWhiteSpace(TimeLimit)) TimeLimit = DurationText.Format(TimeSpan.FromDays(3));
    }

    [RelayCommand]
    private void AddTrigger()
    {
        DateTime now = _time.GetLocalNow().DateTime;
        var trigger = new TriggerViewModel(NewTask.Trigger(now), now);
        Triggers.Add(trigger);
        SelectedTrigger = trigger;
    }

    [RelayCommand(CanExecute = nameof(HasSelectedTrigger))]
    private void RemoveTrigger() => SelectedTrigger = RemoveAndSelectNeighbour(Triggers, SelectedTrigger!);

    [RelayCommand(CanExecute = nameof(HasSelectedTrigger))]
    private void MoveTriggerUp() => Move(Triggers, SelectedTrigger!, -1, moved => SelectedTrigger = moved);

    [RelayCommand(CanExecute = nameof(HasSelectedTrigger))]
    private void MoveTriggerDown() => Move(Triggers, SelectedTrigger!, +1, moved => SelectedTrigger = moved);

    [RelayCommand]
    private void AddAction() => Add(new ActionDraft());

    [RelayCommand]
    private void AddEmailAction() => Add(NewTask.SendEmail);

    [RelayCommand]
    private void AddMessageAction() => Add(NewTask.ShowMessage);

    [RelayCommand(CanExecute = nameof(HasSelectedAction))]
    private void RemoveAction() => SelectedAction = RemoveAndSelectNeighbour(Actions, SelectedAction!);

    [RelayCommand(CanExecute = nameof(HasSelectedAction))]
    private void MoveActionUp() => Move(Actions, SelectedAction!, -1, moved => SelectedAction = moved);

    [RelayCommand(CanExecute = nameof(HasSelectedAction))]
    private void MoveActionDown() => Move(Actions, SelectedAction!, +1, moved => SelectedAction = moved);

    [RelayCommand(CanExecute = nameof(HasSelectedAction))]
    private async Task BrowseForProgramAsync()
    {
        ActionViewModel action = SelectedAction!;
        if (await _files.PickOpenFileAsync(Loc.Get("Dialog.ProgramScript"), Loc.Get("Editor.AllFiles"), "*") is { } path) action.Path = path;
    }

    /// <summary>The saved categories that contain what the category box holds, apart from that category itself.</summary>
    public IReadOnlyList<string> SuggestCategories()
    {
        string typed = Category.Trim();
        return _settings.Current.SavedCategories
            .Where(category => category.Contains(typed, StringComparison.CurrentCultureIgnoreCase) && !category.Equals(typed, StringComparison.CurrentCultureIgnoreCase))
            .ToList();
    }

    // A Japanese or Chinese input method types a comma of its own for the comma key.
    private static readonly char[] TagSeparators = [',', '、', '，'];

    /// <summary>
    /// The saved tags that begin with the tag being typed, which is the text after the last comma,
    /// apart from the tags the box holds already. Before a tag is begun, that is every saved tag the
    /// box does not hold.
    /// </summary>
    public IReadOnlyList<string> SuggestTags()
    {
        string[] held = Tags.Split(TagSeparators, StringSplitOptions.TrimEntries);
        string typed = held[^1];
        return _settings.Current.SavedTags
            .Where(tag => tag.StartsWith(typed, StringComparison.CurrentCultureIgnoreCase) && !held.Contains(tag, StringComparer.CurrentCultureIgnoreCase))
            .ToList();
    }

    /// <summary>Puts <paramref name="tag"/> in place of the tag being typed, with a comma after it for the next one.</summary>
    public void ChooseTag(string tag)
    {
        int comma = Tags.LastIndexOfAny(TagSeparators);
        string before = comma < 0 ? "" : Tags[..(comma + 1)] + (Tags[comma] == ',' ? " " : "");
        Tags = before + tag + ", ";
    }

    /// <summary>Checks the input and saves the task.</summary>
    /// <returns>True when the task was saved; otherwise <see cref="Problems"/> says what is in the way.</returns>
    public async Task<bool> SaveAsync()
    {
        if (!TryBuild(out TaskDraft draft, out List<DraftProblem> problems))
        {
            Problems = string.Join(Environment.NewLine, problems.Select(problem => problem.Message));
            return false;
        }

        string savedPath;
        try
        {
            string? password = NeedsPassword ? Password : null;
            savedPath = await Task.Run(() => _store.Save(draft, _folderPath, _originalPath, password));
        }
        catch (TaskAccessDeniedException)
        {
            Problems = Loc.Get(_app.IsElevated ? "Editor.Error.AccessDenied" : "Editor.Error.AccessDeniedNotElevated");
            return false;
        }
        catch (TaskAccountException ex)
        {
            Problems = Loc.Format("Editor.Error.Account", ex.Message);
            return false;
        }
        catch (TaskExistsException)
        {
            Problems = Loc.Format("Editor.Error.NameTaken", draft.Name);
            return false;
        }
        catch (TaskNotFoundException)
        {
            // Deleted elsewhere while the editor was open; what is gone is the task as it was loaded.
            Problems = Loc.Format("Tasks.Error.Gone", _source.Name);
            return false;
        }
        catch (TaskDuplicatedException ex)
        {
            Problems = Loc.Format("Tasks.Error.Duplicated", ex.Path, ex.CopyPath);
            return false;
        }
        catch (Exception ex)
        {
            // Task Scheduler rejects a definition with whatever COM error fits; the text is the best there is to show.
            Serilog.Log.Error(ex, "Task {Name} could not be saved", draft.Name);
            Problems = Loc.Get("Dialog.Error.SaveTaskFailedPrefix") + ex.Message;
            return false;
        }

        SavedPath = savedPath;
        // A snooze knows its task only by the path, and a new name is a new path.
        if (_originalPath is not null && !string.Equals(_originalPath, savedPath, StringComparison.OrdinalIgnoreCase))
        {
            Renamed = new TaskMove(_originalPath, savedPath);
            await _snooze.TaskMovedAsync(_originalPath, savedPath);
        }

        Problems = "";
        RememberCategoryAndTags(draft);
        return true;
    }

    /// <summary>Builds the task from what was entered, or lists what cannot be understood or saved.</summary>
    public bool TryBuild(out TaskDraft draft, out List<DraftProblem> problems)
    {
        draft = _source;
        problems = [];

        var triggers = new List<TriggerDraft>(Triggers.Count);
        for (int i = 0; i < Triggers.Count; i++)
        {
            if (Triggers[i].TryBuild(out TriggerDraft trigger, out string? problem)) triggers.Add(trigger);
            else problems.Add(new DraftProblem(problem!, (i + 1).ToString()));
        }

        if (!DurationInput.TryRead(IdleDuration, _shownIdleDuration, _source.IdleDuration, out TimeSpan idle))
            problems.Add(new DraftProblem("Dialog.Error.InvalidIdleDuration", IdleDuration));
        if (!DurationInput.TryRead(RestartInterval, _shownRestartInterval, _source.RestartInterval, out TimeSpan restartInterval))
            problems.Add(new DraftProblem("Dialog.Error.InvalidRestartInterval", RestartInterval));
        if (!DurationInput.TryRead(TimeLimit, _shownTimeLimit, _source.ExecutionTimeLimit, out TimeSpan timeLimit)
            || (LimitRunTime && timeLimit <= TimeSpan.Zero))
            problems.Add(new DraftProblem("Dialog.Error.InvalidStopAfter", TimeLimit));
        if (NeedsPassword && Password.Length == 0) problems.Add(new DraftProblem("Editor.Error.PasswordNeeded"));
        if (problems.Count > 0) return false;

        // The network belongs to the network condition. While that is off, the network is not
        // shown and stays as it was, like everything else the user cannot see.
        NetworkProfile? network = NetworkIndex > 0 ? _networks[NetworkIndex - 1] : null;
        bool networkKept = !RunOnlyIfNetwork || (network?.Id ?? Guid.Empty) == _source.NetworkId;
        draft = _source with
        {
            Name = Name.Trim(),
            Description = Description,
            Author = Author,
            Category = Category.Trim(),
            Tags = Tags == _shownTags ? _source.Tags : ParseTags(Tags),
            Enabled = Enabled,
            CompletionActions = CompletionActions.Build(),
            Triggers = triggers,
            Actions = Actions.Select(action => action.Build()).ToValueList(),
            RunOnlyIfIdle = RunOnlyIfIdle,
            IdleDuration = idle,
            StopOnIdleEnd = StopOnIdleEnd,
            RunOnlyOnAcPower = RunOnlyOnAcPower,
            StopOnBattery = StopOnBattery,
            RunOnlyIfNetwork = RunOnlyIfNetwork,
            NetworkId = networkKept ? _source.NetworkId : network?.Id ?? Guid.Empty,
            NetworkName = networkKept ? _source.NetworkName : network?.Name ?? "",
            WakeToRun = WakeToRun,
            HighestPrivileges = HighestPrivileges,
            Hidden = Hidden,
            RunIfMissed = RunIfMissed,
            MultipleInstances = InstancePolicies[InstancePolicyIndex],
            Priority = Priorities[PriorityIndex],
            DeleteWhenExpired = DeleteWhenExpired,
            AllowHardTerminate = AllowHardTerminate,
            RestartCount = RestartOnFailure ? (int)Math.Clamp(double.IsNaN(RestartCount) ? 1 : RestartCount, 1, 999) : 0,
            RestartInterval = restartInterval,
            ExecutionTimeLimit = LimitRunTime ? timeLimit : TimeSpan.Zero,
            Account = ChosenAccount()
        };

        problems.AddRange(TaskDraftValidator.Validate(draft));
        return problems.Count == 0;
    }

    private TaskAccount ChosenAccount() => AccountIndex switch
    {
        CurrentUser => TaskAccount.CurrentUser,
        OtherUser => new TaskAccount(TaskAccountKind.User, UserName.Trim()),
        System => TaskAccount.System,
        _ => _source.Account
    };

    private static string AccountName(TaskAccount account) => account.Kind switch
    {
        TaskAccountKind.LocalService => "LOCAL SERVICE",
        TaskAccountKind.NetworkService => "NETWORK SERVICE",
        _ => account.Name
    };

    private static ValueList<string> ParseTags(string text) =>
        text.Split(TagSeparators, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToValueList();

    private void RememberCategoryAndTags(TaskDraft saved) => _settings.Update(settings => settings with
    {
        SavedCategories = saved.Category.Length > 0 ? [.. settings.SavedCategories.Union([saved.Category], StringComparer.OrdinalIgnoreCase)] : settings.SavedCategories,
        SavedTags = [.. settings.SavedTags.Union(saved.Tags, StringComparer.OrdinalIgnoreCase)]
    });

    private void Add(ActionDraft action)
    {
        var added = new ActionViewModel(action);
        Actions.Add(added);
        SelectedAction = added;
    }

    private static T? RemoveAndSelectNeighbour<T>(ObservableCollection<T> items, T item) where T : class
    {
        int index = items.IndexOf(item);
        items.RemoveAt(index);
        return items.Count == 0 ? null : items[Math.Min(index, items.Count - 1)];
    }

    private static void Move<T>(ObservableCollection<T> items, T item, int by, Action<T> reselect)
    {
        int from = items.IndexOf(item), to = from + by;
        if (to < 0 || to >= items.Count) return;
        items.Move(from, to);
        reselect(item);
    }
}
