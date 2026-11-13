namespace FluentTaskScheduler.Scheduling;

/// <summary>
/// Completion actions name the tasks they start by path, and a rename or a move gives a task a
/// new path. Windows knows nothing of these links, so they are taken along here: a task that starts
/// a task that moved is changed to start it where it is now.
/// </summary>
public static class CompletionActionReferences
{
    /// <summary>Where the task that was at <paramref name="path"/> is after <paramref name="moves"/>, which are taken in order.</summary>
    public static string PathAfter(IEnumerable<TaskMove> moves, string path) =>
        moves.Aggregate(path, (now, move) => string.Equals(now, move.From, StringComparison.OrdinalIgnoreCase) ? move.To : now);

    /// <summary>
    /// <paramref name="actions"/>, starting the tasks where they are after <paramref name="moves"/>.
    /// A path that only changed case stays as it was: Task Scheduler finds a task whatever the case.
    /// </summary>
    public static CompletionActions Follow(CompletionActions actions, IReadOnlyList<TaskMove> moves) => actions with
    {
        OnSuccess = Follow(actions.OnSuccess, moves),
        OnFailure = Follow(actions.OnFailure, moves)
    };

    /// <summary>
    /// Changes the completion actions of the task at <paramref name="path"/> to start the tasks
    /// where they are after <paramref name="moves"/>. Only the task's description is written,
    /// which is where they are kept; the task keeps its account.
    /// </summary>
    /// <returns>
    /// False, with nothing changed, for a task Windows keeps the password of its account for: it
    /// cannot be saved without that password.
    /// </returns>
    /// <exception cref="TaskNotFoundException">There is no task at <paramref name="path"/>.</exception>
    /// <exception cref="TaskAccessDeniedException">Windows refused the change.</exception>
    public static bool Follow(ITaskStore store, string path, IReadOnlyList<TaskMove> moves)
    {
        TaskDraft task = store.Load(path);
        CompletionActions followed = Follow(task.CompletionActions, moves);
        if (followed == task.CompletionActions) return true;
        if (task.StoresPassword) return false;

        store.Save(task with { CompletionActions = followed }, TaskPaths.ParentOf(path), path);
        return true;
    }

    private static ValueList<string> Follow(ValueList<string> targets, IReadOnlyList<TaskMove> moves) =>
        targets.Select(target => PathAfter(moves, target) is var now && !string.Equals(now, target, StringComparison.OrdinalIgnoreCase) ? now : target)
               .ToValueList();
}
