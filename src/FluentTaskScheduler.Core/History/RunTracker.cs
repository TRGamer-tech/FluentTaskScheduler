namespace FluentTaskScheduler.History;

/// <summary>
/// Puts task events together into runs. Events of one run share an instance ID; what a run
/// amounts to follows from the kinds of event it has, as real runs log them:
/// <list type="bullet">
/// <item>A normal run is 100, then 200 and 201 per action, then 102. 102 is logged whatever the actions returned.</item>
/// <item>A stopped run is 330, 201, 102, 111. One that hit its time limit is 201, 102, 329: the reason comes last.</item>
/// <item>If the first action cannot be launched, 203 and 101 take the place of 100, and 101 names no
/// instance. Later actions still run and end in 102; if there are none, nothing more is logged.</item>
/// <item>If a later action cannot be launched, the run ends with its 203 and without a 102.</item>
/// <item>If the task's account cannot be logged on, a single 101 is all there is.</item>
/// </list>
/// Not thread-safe: one caller at a time.
/// </summary>
internal sealed class RunTracker
{
    /// <summary>How many runs that are over are remembered, so that an event arriving late for one of them does not start it anew.</summary>
    private const int RememberedRuns = 512;

    /// <summary>A failed start (101) follows the failed launch (203) that caused it within a fraction of a millisecond; this is generous.</summary>
    private static readonly TimeSpan StartFailureFollowsWithin = TimeSpan.FromSeconds(2);

    private readonly int _capacity;
    private readonly List<Group> _open = [];
    private readonly Dictionary<Guid, Group> _byInstance = [];

    /// <summary>Per task, the run whose action could not be launched last: where a failed start that follows belongs.</summary>
    private readonly Dictionary<string, Group> _failedLaunch = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<Guid> _overInOrder = new();
    private readonly HashSet<Guid> _over = [];

    /// <param name="capacity">How many runs are followed at once. Beyond that the oldest is given up; if it ends after all, it is reported without its beginning.</param>
    public RunTracker(int capacity) => _capacity = capacity;

    /// <summary>Takes in one event.</summary>
    /// <param name="received">When the event reached the tracker; see <see cref="TakeFinished"/>.</param>
    /// <returns>The run, if this event is the one that shows it starting.</returns>
    public TaskRun? Add(TaskEvent e, DateTimeOffset received)
    {
        if (GroupFor(e) is not { } group) return null;

        bool wasGoing = group.IsGoing;
        group.Add(e, received);
        if (e.Kind == TaskEventKind.ActionFailed) _failedLaunch[group.TaskPath] = group;
        return !wasGoing && group.IsGoing ? group.Run : null;
    }

    /// <summary>
    /// Removes and returns the runs that are over and have had no event since
    /// <paramref name="quietSince"/>. A run is not final the moment it looks over: the reason for
    /// a stop can follow the completion, and a failed launch can be followed by the next action.
    /// </summary>
    public IReadOnlyList<TaskRun> TakeFinished(DateTimeOffset quietSince)
    {
        List<TaskRun>? finished = null;
        for (int i = 0; i < _open.Count;)
        {
            var group = _open[i];
            if (!group.IsSettling || group.LastReceived > quietSince)
            {
                i++;
                continue;
            }

            _open.RemoveAt(i);
            Forget(group);
            if (group.Run is not { } run) continue;

            if (run.InstanceId is { } over) Remember(over);
            (finished ??= []).Add(run);
        }
        return finished ?? [];
    }

    /// <summary>When the first of the runs that look over, or of the start requests that came to nothing, received its last event; null if there is none.</summary>
    public DateTimeOffset? OldestSettling
    {
        get
        {
            DateTimeOffset? oldest = null;
            foreach (var group in _open)
            {
                if (group.IsSettling && (oldest is null || group.LastReceived < oldest)) oldest = group.LastReceived;
            }
            return oldest;
        }
    }

    /// <summary>Every run seen so far that was not taken, finished or not.</summary>
    public IEnumerable<TaskRun> Runs() => _open.Select(group => group.Run).OfType<TaskRun>();

    private Group? GroupFor(TaskEvent e)
    {
        if (e.InstanceId is { } id)
        {
            if (_over.Contains(id)) return null;
            if (!_byInstance.TryGetValue(id, out var known))
            {
                known = Open(new Group(e.TaskPath, id));
                _byInstance[id] = known;
            }
            return known;
        }

        if (e.Kind != TaskEventKind.StartFailed) return null;

        // A failed start names no instance. When it is the launch of the first action that failed,
        // it directly follows that action's event, and the two are one run.
        if (_failedLaunch.Remove(e.TaskPath, out var failed)
            && failed.Events[^1] is { Kind: TaskEventKind.ActionFailed } launch
            && e.Time - launch.Time is var gap && gap >= TimeSpan.Zero && gap <= StartFailureFollowsWithin)
        {
            return failed;
        }
        return Open(new Group(e.TaskPath, null));
    }

    private Group Open(Group group)
    {
        _open.Add(group);
        if (_open.Count > _capacity)
        {
            Forget(_open[0]);
            _open.RemoveAt(0);
        }
        return group;
    }

    private void Forget(Group group)
    {
        if (group.InstanceId is { } id) _byInstance.Remove(id);
        if (_failedLaunch.GetValueOrDefault(group.TaskPath) == group) _failedLaunch.Remove(group.TaskPath);
    }

    private void Remember(Guid instanceId)
    {
        _over.Add(instanceId);
        _overInOrder.Enqueue(instanceId);
        if (_overInOrder.Count > RememberedRuns) _over.Remove(_overInOrder.Dequeue());
    }

    /// <summary>Null when the events only show a request to start the task: nothing ran and nothing failed.</summary>
    private static TaskRun? Summarize(Group group)
    {
        DateTimeOffset? started = null;
        TaskEvent? failure = null, completed = null, last = null;
        bool ran = false, stopping = false, timedOut = false, terminated = false;
        string user = "";

        foreach (var e in group.Events)
        {
            if (e.Kind == TaskEventKind.Other) continue;
            last = e;
            switch (e.Kind)
            {
                case TaskEventKind.RunStarted:
                case TaskEventKind.ActionStarted:
                    started ??= e.Time;
                    ran = true;
                    break;
                case TaskEventKind.ActionCompleted:
                    ran = true;
                    if (e.ResultCode is not (null or 0)) failure ??= e;
                    break;
                case TaskEventKind.StartFailed:
                case TaskEventKind.ActionFailed:
                    failure ??= e;
                    break;
                case TaskEventKind.RunCompleted:
                    ran = true;
                    completed = e;
                    break;
                case TaskEventKind.Stopping:
                    stopping = true;
                    break;
                case TaskEventKind.TimedOut:
                    timedOut = true;
                    break;
                case TaskEventKind.Terminated:
                    terminated = true;
                    break;
            }
            if (user.Length == 0 && e.Kind is TaskEventKind.RunStarted or TaskEventKind.RunCompleted or TaskEventKind.StartFailed) user = e.User;
        }

        if (last is null) return null;

        // Without a completion, a failure is the end only as long as nothing follows it.
        bool over = completed is not null || terminated || last.Kind is TaskEventKind.StartFailed or TaskEventKind.ActionFailed;
        var status = !over ? RunStatus.Running
            : timedOut ? RunStatus.TimedOut
            : stopping || terminated ? RunStatus.Stopped
            : failure is null ? RunStatus.Succeeded
            : ran ? RunStatus.Failed
            : RunStatus.CouldNotStart;

        return new TaskRun
        {
            TaskPath = group.TaskPath,
            Status = status,
            InstanceId = group.InstanceId,
            Started = started,
            Ended = over ? (completed ?? last).Time : null,
            ResultCode = status switch
            {
                RunStatus.Succeeded => 0,
                RunStatus.Failed or RunStatus.CouldNotStart => failure!.ResultCode,
                _ => null
            },
            User = user,
            Events = group.Events.ToValueList()
        };
    }

    private sealed class Group(string taskPath, Guid? instanceId)
    {
        private TaskRun? _run;
        private bool _runIsCurrent;

        public string TaskPath { get; } = taskPath;
        public Guid? InstanceId { get; } = instanceId;
        public List<TaskEvent> Events { get; } = [];
        public DateTimeOffset LastReceived { get; private set; }

        /// <summary>True once an event shows something being launched.</summary>
        public bool IsGoing { get; private set; }

        /// <summary>What the events add up to so far; null while they only show a request to start the task.</summary>
        public TaskRun? Run
        {
            get
            {
                if (!_runIsCurrent)
                {
                    _run = Summarize(this);
                    _runIsCurrent = true;
                }
                return _run;
            }
        }

        /// <summary>True for a run that looks over and for a request that came to nothing: both are dropped once they stay quiet.</summary>
        public bool IsSettling => Run is null or { IsFinished: true };

        public void Add(TaskEvent e, DateTimeOffset received)
        {
            Events.Add(e);
            LastReceived = received;
            _runIsCurrent = false;
            if (e.Kind is TaskEventKind.RunStarted or TaskEventKind.ActionStarted) IsGoing = true;
        }
    }
}
