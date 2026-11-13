namespace FluentTaskScheduler.Snoozing;

/// <summary>When a snooze is to end, as the user chooses it. The service works out the moment.</summary>
public sealed record SnoozeEnd
{
    private SnoozeEnd() { }

    internal TimeSpan? Duration { get; private init; }
    internal TimeOnly? TimeOfDay { get; private init; }

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

    /// <summary>When the user signs out or Windows restarts.</summary>
    public static SnoozeEnd AtRestart { get; } = new();
}
