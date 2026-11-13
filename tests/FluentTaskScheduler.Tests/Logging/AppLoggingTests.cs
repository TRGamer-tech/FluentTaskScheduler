using FluentTaskScheduler.Logging;
using FluentTaskScheduler.Settings;
using FluentTaskScheduler.Tests.Support;

namespace FluentTaskScheduler.Tests.Logging;

public sealed class AppLoggingTests : IDisposable
{
    private readonly TempFolder _folder = new();
    private AppLogging? _logging;

    public void Dispose()
    {
        _logging?.Logger.Dispose();
        _folder.Dispose();
    }

    private AppLogging NewLogging(long maxFileBytes = 1024 * 1024) => _logging = new AppLogging(_folder.Paths, maxFileBytes);

    private void LogOneOfEach(AppLogging logging)
    {
        logging.Logger.Information("Opened {Count} tasks", 12);
        logging.Logger.Warning("Something looks off");
        logging.Logger.Error(new IOException("disk full"), "Saving failed");
        logging.Logger.Fatal(new InvalidOperationException("boom"), "Unhandled exception");
    }

    // The logger keeps its files open for writing; reading them meanwhile has to allow for that.
    private static string Written(AppLogging logging, LogKind kind)
    {
        if (logging.CurrentFile(kind) is not { } file) return "";
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    [Fact]
    public void NothingIsWritten_UntilSomethingIsLogged()
    {
        AppLogging logging = NewLogging();

        Assert.Empty(Directory.GetFiles(_folder.Path));
        Assert.Null(logging.CurrentFile(LogKind.App));
        Assert.Null(logging.CurrentFile(LogKind.Errors));
        Assert.Null(logging.CurrentFile(LogKind.Crashes));
    }

    [Fact]
    public void BeforeTheSettingsAreKnown_TheDefaultsApply_EachKindOfEventInItsOwnFile()
    {
        AppLogging logging = NewLogging();

        LogOneOfEach(logging);

        Assert.Equal(_folder.File("App_Log.txt"), logging.CurrentFile(LogKind.App));
        Assert.Equal(_folder.File("Error_Log.txt"), logging.CurrentFile(LogKind.Errors));
        Assert.Equal(_folder.File("Crash_Log.txt"), logging.CurrentFile(LogKind.Crashes));

        string app = Written(logging, LogKind.App);
        Assert.Contains("[INF] Opened 12 tasks", app);
        Assert.Contains("[WRN] Something looks off", app);
        Assert.DoesNotContain("Saving failed", app);
        Assert.DoesNotContain("Unhandled exception", app);

        string errors = Written(logging, LogKind.Errors);
        Assert.Contains("[ERR] Saving failed", errors);
        Assert.Contains("System.IO.IOException: disk full", errors);
        Assert.DoesNotContain("Unhandled exception", errors);

        string crashes = Written(logging, LogKind.Crashes);
        Assert.Contains("[FTL] Unhandled exception", crashes);
        Assert.Contains("System.InvalidOperationException: boom", crashes);
        Assert.DoesNotContain("Saving failed", crashes);
    }

    [Fact]
    public void WithoutSeparateFiles_TheAppLogHoldsEverything_AndACrashIsStillInTheCrashLog()
    {
        using var settings = new SettingsStore(_folder.Paths);
        settings.Update(s => s with { SeparateLogFiles = false });
        AppLogging logging = NewLogging();
        logging.Follow(settings);

        LogOneOfEach(logging);

        string app = Written(logging, LogKind.App);
        Assert.Contains("Opened 12 tasks", app);
        Assert.Contains("[ERR] Saving failed", app);
        Assert.Contains("[FTL] Unhandled exception", app);
        Assert.Null(logging.CurrentFile(LogKind.Errors));
        Assert.Contains("[FTL] Unhandled exception", Written(logging, LogKind.Crashes));
    }

    [Fact]
    public void WithLoggingOff_OnlyACrashIsRecorded()
    {
        using var settings = new SettingsStore(_folder.Paths);
        settings.Update(s => s with { EnableLogging = false });
        AppLogging logging = NewLogging();
        logging.Follow(settings);

        LogOneOfEach(logging);

        Assert.Null(logging.CurrentFile(LogKind.App));
        Assert.Null(logging.CurrentFile(LogKind.Errors));
        Assert.Contains("[FTL] Unhandled exception", Written(logging, LogKind.Crashes));
    }

    [Fact]
    public void AChangedSetting_AppliesToWhatIsLoggedFromThenOn()
    {
        using var settings = new SettingsStore(_folder.Paths);
        AppLogging logging = NewLogging();
        logging.Follow(settings);

        logging.Logger.Information("first");
        settings.Update(s => s with { EnableLogging = false });
        logging.Logger.Information("second");
        logging.Logger.Error("an error while logging is off");
        settings.Update(s => s with { EnableLogging = true, SeparateLogFiles = false });
        logging.Logger.Error("an error for the app log");

        string app = Written(logging, LogKind.App);
        Assert.Contains("first", app);
        Assert.DoesNotContain("second", app);
        Assert.DoesNotContain("while logging is off", app);
        Assert.Contains("an error for the app log", app);
        Assert.Null(logging.CurrentFile(LogKind.Errors));
    }

    [Fact]
    public void WhenAFileIsFull_LoggingCarriesOnInANewOne_WhichIsThenTheCurrentFile()
    {
        AppLogging logging = NewLogging(maxFileBytes: 300);

        for (int i = 1; i <= 40; i++) logging.Logger.Information("Entry number {Number} of a log that outgrows its file", i);

        string current = logging.CurrentFile(LogKind.App)!;
        Assert.NotEqual(_folder.File("App_Log.txt"), current);
        Assert.Contains("Entry number 40 ", Written(logging, LogKind.App));
        Assert.Equal(2, Directory.GetFiles(_folder.Path, "App_Log*.txt").Length);
    }

    [Fact]
    public void AFileThatOnlyLooksLikeALog_IsNotTakenForTheCurrentOne()
    {
        AppLogging logging = NewLogging();
        logging.Logger.Information("real");
        File.WriteAllText(_folder.File("App_Log - Copy.txt"), "a copy someone made");
        File.WriteAllText(_folder.File("App_Log_old.txt"), "renamed by hand");

        Assert.Equal(_folder.File("App_Log.txt"), logging.CurrentFile(LogKind.App));
    }
}
