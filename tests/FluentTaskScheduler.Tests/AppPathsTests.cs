namespace FluentTaskScheduler.Tests;

public class AppPathsTests
{
    // What Windows answers for %LOCALAPPDATA%, and what stands in for it here.
    private static readonly string LocalAppData = Path.Combine(Path.GetTempPath(), "Local");

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void TheDataFolder_IsThePublishersFolderForTheApp_InWhatTheAccountKeepsOnThisComputer(string? variable)
    {
        var paths = new AppPaths(variable, LocalAppData);

        Assert.Equal(Path.Combine(LocalAppData, "TRGamer-tech", "FluentTaskScheduler"), paths.DataFolder);
    }

    [Fact]
    public void VersionsBefore2_KeptTheFilesInAFolderOfTheAppsNameAlone()
    {
        var paths = new AppPaths(null, LocalAppData);

        Assert.Equal(Path.Combine(LocalAppData, "FluentTaskScheduler"), paths.Version19?.DataFolder);
        Assert.Null(paths.Version19?.Version19);
    }

    [Fact]
    public void AFolderNamedByTheEnvironmentVariable_IsTheDataFolder_AndHasNoFolderOfOlderVersions()
    {
        string chosen = Path.Combine(Path.GetTempPath(), "Chosen");

        var paths = new AppPaths(chosen, LocalAppData);

        Assert.Equal(chosen, paths.DataFolder);
        Assert.Null(paths.Version19);
    }

    [Fact]
    public void AFolderGivenInCode_IsTheDataFolder_AndHasNoFolderOfOlderVersions()
    {
        var paths = new AppPaths(LocalAppData);

        Assert.Equal(LocalAppData, paths.DataFolder);
        Assert.Null(paths.Version19);
    }

    [Fact]
    public void EverythingTheAppKeeps_LiesInTheDataFolder()
    {
        var paths = new AppPaths(LocalAppData);

        Assert.Equal(Path.Combine(LocalAppData, "settings.json"), paths.SettingsFile);
        Assert.Equal(Path.Combine(LocalAppData, "Scripts"), paths.ScriptsFolder);
        Assert.Equal(Path.Combine(LocalAppData, "ExecutionHistory"), paths.ExecutionHistoryFolder);
        Assert.Equal(Path.Combine(LocalAppData, "snooze.json"), paths.File("snooze.json"));
    }

    [Fact]
    public void TheApp_AsksWindowsForTheFolderOfTheAccount_AndTheEnvironmentForTheVariable()
    {
        string? before = Environment.GetEnvironmentVariable(AppPaths.DataFolderVariable);
        string chosen = Path.Combine(Path.GetTempPath(), "Chosen");
        try
        {
            Environment.SetEnvironmentVariable(AppPaths.DataFolderVariable, null);
            string ofTheAccount = new AppPaths().DataFolder;
            Environment.SetEnvironmentVariable(AppPaths.DataFolderVariable, chosen);
            string named = new AppPaths().DataFolder;

            Assert.Equal(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TRGamer-tech", "FluentTaskScheduler"),
                ofTheAccount);
            Assert.Equal(chosen, named);
        }
        finally
        {
            Environment.SetEnvironmentVariable(AppPaths.DataFolderVariable, before);
        }
    }
}
