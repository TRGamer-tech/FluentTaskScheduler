using FluentTaskScheduler.Settings;
using FluentTaskScheduler.Updates;
using FluentTaskScheduler.ViewModels;

namespace FluentTaskScheduler.Tests.Support;

/// <summary>Start with Windows, in memory. Set <see cref="Refuses"/> to make Windows turn every change down.</summary>
public sealed class FakeStartWithWindows : IStartWithWindows
{
    public bool IsEnabled { get; set; }
    public bool Refuses { get; set; }

    public bool TrySetEnabled(bool enabled)
    {
        if (Refuses) return false;
        IsEnabled = enabled;
        return true;
    }
}

/// <summary>Opens nothing and records what it was asked to open. Set <see cref="Fails"/> to make every attempt fail.</summary>
public sealed class RecordingLauncher : ILauncher
{
    public List<string> Opened { get; } = [];
    public bool Fails { get; set; }

    public bool TryOpen(string target)
    {
        Opened.Add(target);
        return !Fails;
    }
}

/// <summary>Records which of the introducing dialogs were shown, as "Tour" or "ReleaseNotes 1.9.0".</summary>
public sealed class RecordingWelcomeDialogs : IWelcomeDialogs
{
    public List<string> Shown { get; } = [];

    public Task ShowTourAsync()
    {
        Shown.Add("Tour");
        return Task.CompletedTask;
    }

    public Task ShowReleaseNotesAsync(ReleaseNotes notes)
    {
        Shown.Add($"ReleaseNotes {notes.Version}");
        return Task.CompletedTask;
    }
}

/// <summary>Release notes for the versions a test adds; none for any other, as when offline.</summary>
public sealed class FakeReleaseNotes : IReleaseNotesSource
{
    private readonly Dictionary<string, ReleaseNotes> _notes = [];

    public List<string> Asked { get; } = [];

    /// <summary>When set, a request stays unanswered until the test completes this.</summary>
    public TaskCompletionSource? Hold { get; set; }

    public FakeReleaseNotes Add(string version, string markdown = "- Something new")
    {
        _notes[version] = new ReleaseNotes(version, "", markdown, "https://example.invalid/" + version);
        return this;
    }

    public async Task<ReleaseNotes?> FindAsync(string version)
    {
        Asked.Add(version);
        if (Hold is not null) await Hold.Task;
        return _notes.GetValueOrDefault(version);
    }
}

/// <summary>An update library with a scripted outcome.</summary>
public sealed class FakeUpdater : IUpdater
{
    public bool CanUpdate { get; set; } = true;

    /// <summary>The version a check finds and downloads; null for none.</summary>
    public string? Available { get; set; }

    /// <summary>Thrown by the next check or by starting the installer, when set.</summary>
    public Exception? Failure { get; set; }

    /// <summary>When set, a check does not finish until the test completes this.</summary>
    public TaskCompletionSource? Hold { get; set; }

    public int Checks { get; private set; }
    public int InstallerStarts { get; private set; }

    public async Task<string?> DownloadLatestAsync()
    {
        Checks++;
        if (Hold is not null) await Hold.Task;
        if (Failure is not null) throw Failure;
        return Available;
    }

    public Task StartInstallerAsync()
    {
        if (Failure is not null) throw Failure;
        InstallerStarts++;
        return Task.CompletedTask;
    }
}
