using System.Text.RegularExpressions;
using FluentTaskScheduler.Tests.Library;

namespace FluentTaskScheduler.Tests.Localization;

/// <summary>
/// The four files of texts, checked for what the build on Windows or a user would otherwise be
/// the first to find.
/// </summary>
public partial class ResourceFilesTests
{
    private const string Reference = "en-US";

    public static TheoryData<string> Languages => RepoFiles.Languages;

    [Theory]
    [MemberData(nameof(Languages))]
    public void EveryLanguage_HasExactlyTheTextsEnglishHas(string language)
    {
        var english = RepoFiles.Texts(Reference).Keys.ToHashSet();
        var other = RepoFiles.Texts(language).Keys.ToHashSet();

        Assert.Empty(english.Except(other).Order());
        Assert.Empty(other.Except(english).Order());
    }

    /// <summary>
    /// The dots in a name are a path to the resource compiler, and a path cannot be a text and a
    /// folder of texts at once: with "A.B" there can be no "A.B.C". It compares without regard to
    /// case. A violation fails the build of the app, but only on Windows.
    /// </summary>
    [Fact]
    public void NoName_IsAlsoTheBeginningOfAnother()
    {
        string[] names = RepoFiles.Texts(Reference).Keys.Select(name => name.ToLowerInvariant()).ToArray();
        var all = names.ToHashSet();

        var both = names.Where(name => Parents(name).Any(all.Contains)).Order().ToArray();

        Assert.Empty(both);
        Assert.Equal(names.Length, all.Count);
    }

    private static IEnumerable<string> Parents(string name)
    {
        for (int dot = name.LastIndexOf('.'); dot > 0; dot = name.LastIndexOf('.', dot - 1)) yield return name[..dot];
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void ATranslation_HasThePlaceholdersOfTheEnglishText(string language)
    {
        var english = RepoFiles.Texts(Reference);
        var translated = RepoFiles.Texts(language);

        var different = english.Keys
            .Where(translated.ContainsKey)
            .Where(name => !Placeholders(english[name]).SetEquals(Placeholders(translated[name])))
            .Order().ToArray();

        Assert.Empty(different);
    }

    private static HashSet<string> Placeholders(string text) => Placeholder().Matches(text).Select(match => match.Value).ToHashSet();

    [GeneratedRegex(@"\{\d+\}")]
    private static partial Regex Placeholder();
}
