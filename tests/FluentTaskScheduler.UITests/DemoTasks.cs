using System.Text.Json.Nodes;
using System.Xml.Linq;

namespace FluentTaskScheduler.UITests;

/// <summary>
/// Tasks that could be somebody's own, for the pictures of the README: a folder of them in the
/// machine's Task Scheduler, with a few runs on record and one of them running. Everything else
/// the UI tests create has a name that begins with FtsUiTests_. This folder has the name a user
/// would give it, so it is only made where the machine has no folder of that name, and it is
/// removed again when the tests that share it are done.
/// </summary>
/// <remarks>
/// The tasks run as SYSTEM, which has no desktop for a console window to come up on, and all
/// they run is ping, for as long as a run is to take. Making them needs administrator rights,
/// and so does the history, which Windows only records while it is switched on.
/// </remarks>
public sealed class DemoTasks : IDisposable
{
    /// <summary>The name of the folder, without the backslash its path begins with.</summary>
    public const string Folder = "Housekeeping";

    /// <summary>The category of the tasks whose runs are worth looking at: several that succeeded, one that failed and one going on.</summary>
    public const string BackupCategory = "Backup";

    /// <summary>The task that is running while the pictures are taken.</summary>
    public const string VerifyBackup = "Verify last backup";

    /// <summary>The task whose run failed. Its trigger names days of the week, which is more than the trigger of a new task shows.</summary>
    public const string SyncPhotos = "Sync photos to NAS";

    private const string NightlyBackup = "Nightly backup";
    private const string Author = "Alex";

    private static readonly XNamespace Ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    /// <param name="Trigger">The trigger as task XML; null for a task that only runs when something starts it.</param>
    private sealed record DemoTask(string Name, string Description, string Category, string[] Tags, XElement? Trigger)
    {
        public bool Enabled { get; init; } = true;

        /// <summary>How long a run takes. It only matters for the tasks that get to run.</summary>
        public int Seconds { get; init; } = 5;

        /// <summary>A run ends with exit code 1.</summary>
        public bool Fails { get; init; }

        /// <summary>The task this one starts through a completion action once it has succeeded.</summary>
        public string? ThenStarts { get; init; }
    }

    private static readonly DemoTask[] All =
    [
        new("Clear temp files", "Deletes what has been in the temp folder for more than a week.",
            "Maintenance", ["cleanup", "disk"], Daily("03:00")),
        new("Defragment data drive", "Optimizes drive D: while nobody is using the computer.",
            "Maintenance", ["disk", "performance"], Weekly("23:00", "Saturday")) { Enabled = false },
        new("Empty recycle bin", "Removes for good what was deleted more than a month ago.",
            "Maintenance", ["cleanup"], Weekly("20:00", "Friday")),
        new(NightlyBackup, "Copies documents and projects to the backup share on the NAS.",
            BackupCategory, ["nas", "nightly"], Daily("02:00")) { Seconds = 14, ThenStarts = VerifyBackup },
        new(SyncPhotos, "Mirrors the Pictures folder to the photos share on the NAS.",
            BackupCategory, ["nas", "photos"], Weekly("04:00", "Saturday", "Sunday")) { Seconds = 8, Fails = true },
        new(VerifyBackup, "Compares the newest backup on the NAS with the files it was made from.",
            BackupCategory, ["nas"], Trigger: null) { Seconds = 1200 },
        new("Weekly disk report", "Mails how much room is left on every drive.",
            "Monitoring", ["disk", "report"], Weekly("08:00", "Monday"))
    ];

    /// <summary>
    /// The runs on record when the pictures are taken, oldest first: three backups that succeed
    /// and a sync that fails. A run takes long enough to be seen running by someone who asks
    /// Task Scheduler about it, which takes a moment each time.
    /// </summary>
    private static readonly string[] History = [NightlyBackup, NightlyBackup, NightlyBackup, SyncPhotos];

    private readonly Lazy<WindowsTasks> _made;
    private WindowsTasks? _tasks;
    private HistoryLog? _history;

    public DemoTasks() => _made = new Lazy<WindowsTasks>(Make);

    /// <summary>The names of all the tasks.</summary>
    public static IReadOnlyList<string> Names { get; } = [.. All.Select(task => task.Name)];

    /// <summary>The names of the tasks of one category.</summary>
    public static IReadOnlyList<string> NamesIn(string category) => [.. All.Where(task => task.Category == category).Select(task => task.Name)];

    /// <summary>
    /// Makes the tasks and their history, the first time it is called: a test that is skipped never
    /// calls it, so nothing is made for tests that do not run. Where the machine already has a
    /// folder of the name, the calling test is skipped. What went wrong the first time is what
    /// every later call reports, too.
    /// </summary>
    public void Prepare() => _ = _made.Value;

    public void Dispose()
    {
        // Deleting a task leaves a run of it going.
        _tasks?.Stop(VerifyBackup);
        _tasks?.Dispose();
        _history?.Dispose();
    }

    private WindowsTasks Make()
    {
        WindowsTasks? tasks = _tasks = WindowsTasks.Named(Folder);
        Skip.If(tasks is null,
            $@"This machine already has a task folder \{Folder}, which these tests would remove with everything in it. " +
            "Delete it in Task Scheduler if an earlier run of them left it behind.");

        _history = new HistoryLog();
        foreach (DemoTask task in All) tasks.Create(task.Name, Xml(task));

        foreach (string name in History)
        {
            tasks.Start(name);
            WaitUntil(() => tasks.IsRunning(name), $"The task '{name}' did not start.");
            WaitUntil(() => !tasks.IsRunning(name), $"The task '{name}' did not come to an end.");
        }
        tasks.Start(VerifyBackup);
        WaitUntil(() => tasks.IsRunning(VerifyBackup), $"The task '{VerifyBackup}' did not start.");
        return tasks;
    }

    private static void WaitUntil(Func<bool> condition, string otherwise)
    {
        if (!AppSession.Eventually(condition, seconds: 60)) throw new InvalidOperationException(otherwise);
    }

    /// <summary>The task as task XML, the way an export of it would read.</summary>
    private static string Xml(DemoTask task)
    {
        // ping sends a request a second, here to the computer itself, which answers at once.
        string wait = $"-n {task.Seconds + 1} 127.0.0.1";
        var (command, arguments) = task.Fails ? ("cmd.exe", $"/c ping {wait} >nul & exit 1") : ("ping", wait);

        var definition = new XElement(Ns + "Task", new XAttribute("version", "1.2"),
            new XElement(Ns + "RegistrationInfo",
                new XElement(Ns + "Author", Author),
                new XElement(Ns + "Description", task.Description + "\n\n" + Metadata(task))),
            new XElement(Ns + "Triggers", task.Trigger),
            new XElement(Ns + "Principals",
                new XElement(Ns + "Principal", new XAttribute("id", "Author"), new XElement(Ns + "UserId", "S-1-5-18"))),
            new XElement(Ns + "Settings",
                new XElement(Ns + "DisallowStartIfOnBatteries", false),
                new XElement(Ns + "Enabled", task.Enabled)),
            new XElement(Ns + "Actions", new XAttribute("Context", "Author"),
                new XElement(Ns + "Exec", new XElement(Ns + "Command", command), new XElement(Ns + "Arguments", arguments))));
        return "<?xml version=\"1.0\" encoding=\"UTF-16\"?>\n" + definition;
    }

    /// <summary>
    /// Category, tags and completion actions the way the app keeps them: as a comment with JSON
    /// in it, after the text of the description.
    /// </summary>
    private static string Metadata(DemoTask task) => "<!-- FTS_META:" + new JsonObject
    {
        ["Category"] = task.Category,
        ["Tags"] = new JsonArray([.. task.Tags.Select(tag => (JsonNode?)tag)]),
        ["Pipeline"] = task.ThenStarts is { } next
            ? new JsonObject { ["IsEnabled"] = true, ["OnSuccessTasks"] = new JsonArray($@"\{Folder}\{next}"), ["OnFailureTasks"] = new JsonArray() }
            : null
    }.ToJsonString() + " -->";

    private static XElement Daily(string time) =>
        At(time, new XElement(Ns + "ScheduleByDay", new XElement(Ns + "DaysInterval", 1)));

    private static XElement Weekly(string time, params string[] days) =>
        At(time, new XElement(Ns + "ScheduleByWeek",
            new XElement(Ns + "DaysOfWeek", days.Select(day => new XElement(Ns + day))),
            new XElement(Ns + "WeeksInterval", 1)));

    private static XElement At(string time, XElement schedule) =>
        new(Ns + "CalendarTrigger", new XElement(Ns + "StartBoundary", $"2026-01-01T{time}:00"), new XElement(Ns + "Enabled", true), schedule);
}
