namespace FluentTaskScheduler.Snoozing;

/// <summary>When a snooze is to end, as the user chooses it. The service works out the moment.</summary>
public sealed record SnoozeEnd
{
    private SnoozeEnd() { }

    internal TimeSpan? Duration { get; private init; }
    internal TimeOnly? TimeOfDay { get; private init; }
    internal DateTimeOffset? Moment { get; private init; }

    /// <summary>Once <paramref name="duration"/> has passed.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The duration is not positive.</exception>
    public static SnoozeEnd After(TimeSpan duration)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(duration, TimeSpan.Zero);
        return new SnoozeEnd { Duration = duration };
    }

    /// <summary>
    /// The next time this computer's clock shows <paramref name="timeOfDay"/>: later today, or
    /// tomorrow if that time has already passed today.
    /// </summary>
    public static SnoozeEnd At(TimeOnly timeOfDay) => new() { TimeOfDay = timeOfDay };

    /// <summary>
    /// At <paramref name="moment"/>, however long the snooze takes to start. A moment that has
    /// passed by then ends the snooze as soon as it has started.
    /// </summary>
    public static SnoozeEnd Until(DateTimeOffset moment) => new() { Moment = moment };

    /// <summary>When the user signs out or Windows restarts.</summary>
    public static SnoozeEnd AtRestart { get; } = new();
}
