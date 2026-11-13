using System.Diagnostics.Tracing;
using System.Xml.Linq;
using FluentTaskScheduler.History;

namespace FluentTaskScheduler.Tests.History;

/// <summary>Without the app's resources every text resolves to its key, so these tests see which key is asked for.</summary>
public sealed class HistoryTextTests
{
    private static readonly string[] Languages = ["en-US", "de-DE", "ja-JP", "zh-CN"];

    private static TaskEvent Event(int id) => new() { Kind = TaskEventKind.Other, EventId = id, Time = DateTimeOffset.UnixEpoch, TaskPath = @"\Any" };

    [Theory]
    [InlineData(100, "EventResult.100")]
    [InlineData(101, "EventResult.101")]
    [InlineData(102, "EventResult.102")]
    [InlineData(201, "EventResult.201")]
    [InlineData(330, "EventResult.330")]
    public void AnEventWithANameOfItsOwn_IsLabelledWithIt(int eventId, string key)
    {
        Assert.Equal(key, HistoryText.Label(Event(eventId)));
    }

    [Theory]
    [InlineData(103, "EventResult.101")] // a failed start, with an instance
    [InlineData(108, "EventResult.107")] // triggered by an event
    [InlineData(117, "EventResult.107")] // triggered by idleness
    [InlineData(123, "EventResult.107")] // triggered by a session change
    [InlineData(325, "EventResult.324")] // queued
    [InlineData(326, "EventResult.332")] // not started: on battery
    [InlineData(135, "EventResult.332")] // not started: not idle
    public void EventsThatMeanTheSameToAUser_ShareALabel(int eventId, string key)
    {
        Assert.Equal(key, HistoryText.Label(Event(eventId)));
    }

    [Theory]
    [InlineData(104)]
    [InlineData(319)]
    [InlineData(331)] // "stopped (on battery)" in the resources, which it is not: 331 is a time limit that could not be enforced
    [InlineData(4711)]
    public void AnEventTheAppHasNoNameFor_IsLabelledAsAnEventWithItsNumber(int eventId)
    {
        Assert.Null(HistoryText.LabelKey(eventId));
        Assert.Equal("EventResult.Unknown", HistoryText.Label(Event(eventId)));
    }

    [Theory]
    [InlineData(EventLevel.Critical, "EventLevel.Critical")]
    [InlineData(EventLevel.Error, "EventLevel.Error")]
    [InlineData(EventLevel.Warning, "EventLevel.Warning")]
    [InlineData(EventLevel.Informational, "EventLevel.Information")]
    [InlineData(EventLevel.Verbose, "EventLevel.Verbose")]
    [InlineData(EventLevel.LogAlways, "EventLevel.Information")]
    public void ALevel_IsNamedLikeWindowsNamesIt(EventLevel level, string key)
    {
        Assert.Equal(key, HistoryText.Level(level));
    }

    [Theory]
    [InlineData(0u, "0")]
    [InlineData(3u, "3")]
    [InlineData(65535u, "65535")]
    [InlineData(65536u, "0x00010000")]
    [InlineData(0x80070002u, "0x80070002")]
    [InlineData(uint.MaxValue, "0xFFFFFFFF")]
    public void AResultCode_IsDecimalWhenItIsAnExitCode_AndHexWhenItIsAWindowsError(uint code, string expected)
    {
        Assert.Equal(expected, HistoryText.Code(code));
    }

    [Fact]
    public void EveryTextTheHistoryAsksFor_ExistsInAllFourLanguages()
    {
        var keys = Enumerable.Range(1, 999).Select(HistoryText.LabelKey).OfType<string>()
            .Append("EventResult.Unknown")
            .Concat(Enum.GetValues<EventLevel>().Select(HistoryText.Level))
            .Distinct()
            .ToList();
        Assert.True(keys.Count > 25);

        foreach (string language in Languages)
        {
            var texts = ResourcesOf(language);
            var missing = keys.Where(key => !texts.TryGetValue(key, out string? text) || string.IsNullOrWhiteSpace(text)).ToList();
            Assert.True(missing.Count == 0, $"{language} lacks: {string.Join(", ", missing)}");
            Assert.Contains("{0}", texts["EventResult.Unknown"]);
        }
    }

    [Theory]
    [InlineData("EventResult.101")]
    [InlineData("EventResult.323")]
    [InlineData("EventResult.324")]
    [InlineData("EventResult.327")]
    [InlineData("EventResult.328")]
    [InlineData("EventResult.330")]
    public void TheTextsAddedForTheHistory_AreTranslated_AndKeepToTheAppsSpelling(string key)
    {
        // The app writes neither a sharp s nor an em dash; named by code so this file has neither.
        const char sharpS = (char)0xDF, emDash = (char)0x2014;
        string english = ResourcesOf("en-US")[key];

        foreach (string language in Languages)
        {
            string text = ResourcesOf(language)[key];
            if (language != "en-US") Assert.NotEqual(english, text);
            Assert.DoesNotContain(sharpS, text);
            Assert.DoesNotContain(emDash, text);
        }
    }

    /// <summary>The texts of one language, read from the app's resource file in this repository.</summary>
    private static Dictionary<string, string> ResourcesOf(string language)
    {
        string? folder = AppContext.BaseDirectory;
        while (folder is not null && !File.Exists(Path.Combine(folder, "FluentTaskScheduler.sln"))) folder = Path.GetDirectoryName(folder);
        if (folder is null) throw new InvalidOperationException("The tests are not running inside the repository, so the resource files cannot be found.");

        string file = Path.Combine(folder, "src", "FluentTaskScheduler", "Strings", language, "Resources.resw");
        return XDocument.Load(file).Root!.Elements("data").ToDictionary(data => (string)data.Attribute("name")!, data => (string)data.Element("value")!);
    }
}
