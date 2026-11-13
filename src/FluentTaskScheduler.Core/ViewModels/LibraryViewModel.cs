using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentTaskScheduler.Library;
using FluentTaskScheduler.Localization;
using FluentTaskScheduler.Scheduling;
using DaysOfTheWeek = Microsoft.Win32.TaskScheduler.DaysOfTheWeek;

namespace FluentTaskScheduler.ViewModels;

/// <summary>A built-in task template as the library lists it.</summary>
public sealed class TemplateCard(BuiltInTemplate template, IAsyncRelayCommand createTask)
{
    private static readonly (DaysOfTheWeek Day, string Key)[] Weekdays =
    [
        (DaysOfTheWeek.Monday, "Mon"), (DaysOfTheWeek.Tuesday, "Tue"), (DaysOfTheWeek.Wednesday, "Wed"), (DaysOfTheWeek.Thursday, "Thu"),
        (DaysOfTheWeek.Friday, "Fri"), (DaysOfTheWeek.Saturday, "Sat"), (DaysOfTheWeek.Sunday, "Sun")
    ];

    public string Id => template.Id;
    public TemplateGroup Group => template.Group;
    public string Glyph => template.Glyph;
    public string Name => template.Template.Name;
    public string Description => template.Template.Description;

    /// <summary>The program that runs and when, e.g. "defrag.exe · Weekly Sunday 02:00".</summary>
    public string Summary { get; } = $"{template.Template.Command} · {Describe(template.Template)}";

    public string Note => template.Note;
    public bool HasNote => template.Note.Length > 0;
    public bool RunsElevated => template.Template.RunElevated;

    /// <summary>Opens the task editor with the task the template stands for.</summary>
    public IAsyncRelayCommand CreateTaskCommand => createTask;

    private static string Describe(TaskTemplate template)
    {
        TriggerDraft trigger = template.Trigger;
        string time = template.At?.ToString("t", CultureInfo.CurrentCulture) ?? "";
        return trigger.Type switch
        {
            TriggerType.Daily => $"{Loc.Get("Dialog.Trigger.Daily")} {time}",
            TriggerType.Weekly => $"{Loc.Get("Dialog.Trigger.Weekly")} {DayNames(trigger.DaysOfWeek)} {time}",
            TriggerType.AtLogon => Loc.Get("Trigger.AtLogon"),
            // The resource names leave out the "Session" some members of the enumeration start with.
            TriggerType.SessionStateChange => Loc.Format("Trigger.SessionStateChange", Loc.Get("Dialog.Sess." + trigger.SessionChange.ToString().Replace("Session", ""))),
            _ => Loc.Get("Dialog.Trigger." + trigger.Type)
        };
    }

    private static string DayNames(DaysOfTheWeek days) =>
        string.Join(", ", Weekdays.Where(weekday => days.HasFlag(weekday.Day)).Select(weekday => Loc.Get("Dialog.Weekday." + weekday.Key)));
}

/// <summary>The templates of one group, under the group's heading.</summary>
public sealed record TemplateSection(string Title, IReadOnlyList<TemplateCard> Templates);

/// <summary>A script as the library lists it.</summary>
public sealed class ScriptCard(LibraryScript script, IAsyncRelayCommand createTask, IAsyncRelayCommand edit, IAsyncRelayCommand delete)
{
    public string Id => script.Id;
    public string Name => script.Name;
    public string Description => script.Description;
    public bool RunsElevated => script.RunElevated;

    /// <summary>True for a script written in the editor, which can open it again.</summary>
    public bool CanEdit => script.HasText;
    public bool CanDelete => !script.IsBuiltIn;

    /// <summary>Opens the task editor with a task that runs the script.</summary>
    public IAsyncRelayCommand CreateTaskCommand => createTask;
    public IAsyncRelayCommand EditCommand => edit;
    public IAsyncRelayCommand DeleteCommand => delete;
}

/// <summary>The library of one window: the built-in task templates and the scripts, narrowed down by one search text.</summary>
public sealed partial class LibraryViewModel : ObservableObject, IDisposable
{
    private readonly ScriptStore _scripts;
    private readonly ScriptEditorViewModel _editor;
    private readonly IShellNavigator _navigator;
    private readonly IPrompts _prompts;
    private readonly TimeProvider _time;

    private readonly IReadOnlyList<TemplateCard> _allTemplates;
    private IReadOnlyList<ScriptCard> _allScripts = [];

    public LibraryViewModel(ScriptStore scripts, ScriptEditorViewModel editor, IShellNavigator navigator, IPrompts prompts, TimeProvider time)
    {
        _scripts = scripts;
        _editor = editor;
        _navigator = navigator;
        _prompts = prompts;
        _time = time;

        _allTemplates = BuiltInTemplates.Create()
            .Select(template => new TemplateCard(template, new AsyncRelayCommand(() => CreateTaskAsync(template.Template))))
            .ToList();

        _scripts.Changed += Scripts_Changed;
        LoadScripts();
        ApplySearch();
    }

    /// <summary>Templates and scripts are shown when their name or description contains this text.</summary>
    [ObservableProperty]
    private string _searchText = "";

    /// <summary>The groups that have a template to show, in their fixed order.</summary>
    public ObservableCollection<TemplateSection> TemplateSections { get; } = [];

    /// <summary>The user's scripts, then the built-in ones.</summary>
    public ObservableCollection<ScriptCard> Scripts { get; } = [];

    [ObservableProperty]
    private bool _hasNoTemplates;

    [ObservableProperty]
    private bool _hasNoScripts;

    /// <summary>Why the user's scripts are missing from the list, if they are; empty otherwise.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasScriptsProblem))]
    private string _scriptsProblem = "";

    public bool HasScriptsProblem => ScriptsProblem.Length > 0;

    public void Dispose() => _scripts.Changed -= Scripts_Changed;

    partial void OnSearchTextChanged(string value) => ApplySearch();

    [RelayCommand]
    private async Task NewScriptAsync()
    {
        await _editor.NewCommand.ExecuteAsync(null);
        _navigator.ShowScriptEditor();
    }

    private async Task CreateTaskAsync(TaskTemplate template)
    {
        string? created = await _navigator.CreateTaskAsync(template.ToDraft(_time.GetLocalNow().DateTime));
        if (created is not null) _navigator.ShowTask(created);
    }

    private async Task EditAsync(LibraryScript script)
    {
        if (await _editor.OpenAsync(script)) _navigator.ShowScriptEditor();
    }

    private async Task DeleteAsync(LibraryScript script)
    {
        string question = Loc.Format("ScriptLibrary.DeleteTemplate.Content", script.Name);
        if (script.HasText) question += "\n\n" + Loc.Get("ScriptLibrary.DeleteScript.TasksWarning");

        bool confirmed = await _prompts.ConfirmAsync(
            Loc.Get("ScriptLibrary.DeleteTemplate.Title"), question, Loc.Get("Dialog.Common.Delete"), isDestructive: true);
        if (!confirmed) return;

        try
        {
            _scripts.Delete(script.Id);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Serilog.Log.Error(ex, "The script {Name} could not be deleted", script.Name);
            await _prompts.ShowErrorAsync(Loc.Format("Library.Scripts.DeleteFailed", ex.Message));
        }
    }

    private void Scripts_Changed(object? sender, EventArgs e)
    {
        LoadScripts();
        ApplySearch();
    }

    private void LoadScripts()
    {
        try
        {
            _allScripts = _scripts.GetScripts()
                .Select(script => new ScriptCard(
                    script,
                    new AsyncRelayCommand(() => CreateTaskAsync(script.ToTemplate())),
                    new AsyncRelayCommand(() => EditAsync(script)),
                    new AsyncRelayCommand(() => DeleteAsync(script))))
                .ToList();
            ScriptsProblem = _scripts.UnreadableListBackup is { } backup ? Loc.Format("Library.Scripts.ListSetAside", backup) : "";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Serilog.Log.Error(ex, "The scripts of the library could not be listed");
            _allScripts = [];
            ScriptsProblem = Loc.Format("Library.Scripts.LoadFailed", ex.Message);
        }
    }

    private void ApplySearch()
    {
        string search = SearchText.Trim();
        bool Matches(string name, string description) =>
            name.Contains(search, StringComparison.CurrentCultureIgnoreCase)
            || description.Contains(search, StringComparison.CurrentCultureIgnoreCase);

        TemplateSections.Clear();
        foreach (var group in _allTemplates.Where(card => Matches(card.Name, card.Description)).GroupBy(card => card.Group))
        {
            TemplateSections.Add(new TemplateSection(Loc.Get("Templates.Group." + group.Key), group.ToList()));
        }
        HasNoTemplates = TemplateSections.Count == 0;

        Scripts.Clear();
        foreach (ScriptCard card in _allScripts.Where(card => Matches(card.Name, card.Description))) Scripts.Add(card);
        HasNoScripts = Scripts.Count == 0;
    }
}
