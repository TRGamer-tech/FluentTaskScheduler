namespace FluentTaskScheduler;

/// <summary>What the running app is: the version of this build, and where the project lives.</summary>
public sealed record AppInfo(Version Version)
{
    /// <summary>Owner and name of the project on GitHub.</summary>
    public const string Repository = "TRGamer-tech/FluentTaskScheduler";
    public const string RepositoryUrl = "https://github.com/" + Repository;
    public const string IssuesUrl = RepositoryUrl + "/issues";

    /// <summary>The version the way releases are numbered, e.g. "1.9.0".</summary>
    public string VersionText { get; } = ThreeParts(Version).ToString();

    /// <summary>
    /// True when this build is newer than <paramref name="version"/>, which may be written like a
    /// release ("1.9.0") or like its tag ("V1.9.0"). Text that is no version counts as older.
    /// </summary>
    public bool IsNewerThan(string? version) => ParseVersion(version) is not { } other || ThreeParts(Version) > other;

    /// <summary>
    /// Reads "1.9", "1.9.0", "V1.9.0" and "1.9.0-beta.2+build" as the release they name; null for
    /// anything else.
    /// </summary>
    public static Version? ParseVersion(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        string numbers = text.Trim().TrimStart('v', 'V');
        int suffix = numbers.IndexOfAny(['-', '+']);
        if (suffix >= 0) numbers = numbers[..suffix];

        // A lone number is not a version to System.Version; releases always have at least two parts anyway.
        return System.Version.TryParse(numbers, out Version? version) ? ThreeParts(version) : null;
    }

    private static Version ThreeParts(Version version) => new(version.Major, version.Minor, Math.Max(version.Build, 0));
}
