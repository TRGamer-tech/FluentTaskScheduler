using System.Xml.Linq;
using FluentTaskScheduler.Localization;

namespace FluentTaskScheduler.Tests.Library;

/// <summary>
/// Files of the app project that the library depends on at run time. The tests read them from
/// the source tree, so what is checked is what ships.
/// </summary>
internal static class RepoFiles
{
    private static readonly string AppProject = Path.Combine(FindRoot(), "src", "FluentTaskScheduler");

    /// <summary>The scripts that ship with the app.</summary>
    public static string BuiltInScripts => Path.Combine(AppProject, "Assets", "Scripts.json");

    /// <summary>The app's icon, which the notification area icon is drawn from.</summary>
    public static string AppIcon => Path.Combine(AppProject, "Assets", "AppIcon.ico");

    public static TheoryData<string> Languages => new(AppLanguages.Supported);

    /// <summary>The texts of one language, by resource name.</summary>
    public static IReadOnlyDictionary<string, string> Texts(string language) =>
        XDocument.Load(Path.Combine(AppProject, "Strings", language, "Resources.resw"))
            .Root!.Elements("data")
            .ToDictionary(data => (string)data.Attribute("name")!, data => (string?)data.Element("value") ?? "");

    private static string FindRoot()
    {
        for (DirectoryInfo? folder = new(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            if (File.Exists(Path.Combine(folder.FullName, "FluentTaskScheduler.sln"))) return folder.FullName;
        }
        throw new InvalidOperationException($"No FluentTaskScheduler.sln above {AppContext.BaseDirectory}: these tests have to run from within the repository.");
    }
}
