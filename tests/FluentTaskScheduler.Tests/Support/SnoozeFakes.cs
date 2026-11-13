using FluentTaskScheduler.Snoozing;
using Microsoft.Extensions.Time.Testing;

namespace FluentTaskScheduler.Tests.Support;

/// <summary>The Windows session as a test sets it; give it a new <see cref="Id"/> to sign out or restart.</summary>
public sealed class FakeWindowsSession : IWindowsSession
{
    public string Id { get; set; } = "session-1";
}

/// <summary>
/// Fake time whose clock can be moved while the timers keep their pace: what happens when
/// Windows corrects the time, or when the computer sleeps and the timers stand still.
/// </summary>
public sealed class AdjustableClock(DateTimeOffset start) : FakeTimeProvider(start)
{
    private TimeSpan _shift;

    public void ShiftClock(TimeSpan by) => _shift += by;

    public override DateTimeOffset GetUtcNow() => base.GetUtcNow() + _shift;
}
