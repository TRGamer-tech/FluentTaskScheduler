using FluentTaskScheduler.Scheduling;
using FluentTaskScheduler.Tests.Support;
using FluentTaskScheduler.ViewModels;

namespace FluentTaskScheduler.Tests.ViewModels;

public class ImportTaskViewModelTests
{
    private const string Xml = "<Task/>";
    private static readonly TaskAccount Foreign = new(TaskAccountKind.User, "S-1-5-21-1111111111-2222222222-3333333333-1001");

    private readonly FakeTaskStore _store = new();
    private readonly FakeAppLifetime _app = new() { IsElevated = true };

    private ImportTaskViewModel Import(TaskImportInfo info, string? currentFolder = null) =>
        new(_store, _app, Xml, info, "Backup", ["\\", @"\Jobs"], currentFolder);

    private static TaskImportInfo Info(TaskAccount account, bool exists = true, bool highest = false, bool password = false) =>
        new(account, exists, highest, password);

    [Fact]
    public async Task ATaskFromThisMachine_KeepsItsAccountByDefault()
    {
        var import = Import(Info(TaskAccount.CurrentUser), currentFolder: @"\jobs");
        Assert.Equal(0, import.AccountIndex);
        Assert.False(import.AccountIsMissing);

        Assert.True(await import.ImportAsync());

        Assert.Equal(@"\Jobs\Backup", import.ImportedPath);
        Assert.Equal(new TaskImportOptions(@"\Jobs", "Backup", Account: null, HighestPrivileges: false, Password: null), _store.LastImport);
    }

    [Fact]
    public async Task ATaskWhoseAccountDoesNotExistHere_IsImportedForTheCurrentUserByDefault()
    {
        var import = Import(Info(Foreign, exists: false, highest: true));

        Assert.True(import.AccountIsMissing);
        Assert.Equal(1, import.AccountIndex);
        Assert.True(import.HighestPrivileges);
        Assert.Equal(Foreign.Name, import.AccountName);

        Assert.True(await import.ImportAsync());
        Assert.Equal(TaskAccount.CurrentUser, _store.LastImport!.Account);
        Assert.True(_store.LastImport.HighestPrivileges);
    }

    [Fact]
    public async Task TheAccountAndElevation_CanBeChosen()
    {
        var import = Import(Info(Foreign, exists: false, highest: true));

        import.AccountIndex = 2;
        import.HighestPrivileges = false;
        import.Name = " Renamed ";
        import.FolderIndex = 1;

        Assert.True(await import.ImportAsync());
        Assert.Equal(new TaskImportOptions(@"\Jobs", "Renamed", TaskAccount.System, false, null), _store.LastImport);
    }

    [Fact]
    public async Task KeepingAnAccountWithAStoredPassword_NeedsThePassword_AnotherAccountDoesNot()
    {
        var import = Import(Info(new TaskAccount(TaskAccountKind.User, @"PC\svc"), password: true));
        Assert.True(import.NeedsPassword);

        Assert.False(await import.ImportAsync());
        Assert.Equal("Editor.Error.PasswordNeeded", import.Problem);
        Assert.Null(_store.LastImport);

        import.Password = "s3cret";
        Assert.True(await import.ImportAsync());
        Assert.Equal("s3cret", _store.LastImport!.Password);

        import.AccountIndex = 1;
        Assert.False(import.NeedsPassword);
    }

    [Theory]
    [InlineData("", "Dialog.Error.EmptyName")]
    [InlineData(@"a\b", "Editor.Error.NameCharacters")]
    public async Task ANameTaskSchedulerWouldReject_IsCaughtFirst(string name, string expected)
    {
        var import = Import(Info(TaskAccount.CurrentUser));
        import.Name = name;

        Assert.False(await import.ImportAsync());

        Assert.Equal(expected, import.Problem);
        Assert.True(import.HasProblem);
        Assert.Null(_store.LastImport);
    }

    [Theory]
    [InlineData(true, TaskAccountKind.CurrentUser, 2, false, false)]
    [InlineData(false, TaskAccountKind.CurrentUser, 2, false, true)]
    [InlineData(false, TaskAccountKind.CurrentUser, 1, true, true)]
    [InlineData(false, TaskAccountKind.CurrentUser, 1, false, false)]
    [InlineData(false, TaskAccountKind.CurrentUser, 0, false, false)]
    [InlineData(false, TaskAccountKind.User, 0, false, false)]
    [InlineData(false, TaskAccountKind.System, 0, false, true)]
    [InlineData(false, TaskAccountKind.LocalService, 0, false, true)]
    [InlineData(false, TaskAccountKind.NetworkService, 0, false, true)]
    [InlineData(false, TaskAccountKind.Group, 0, false, true)]
    [InlineData(false, TaskAccountKind.System, 1, false, false)]
    [InlineData(true, TaskAccountKind.System, 0, false, false)]
    public void WhatNeedsAdministratorRights_IsFlaggedWhenTheAppHasNone(bool elevated, TaskAccountKind exported, int accountIndex, bool highest, bool expected)
    {
        _app.IsElevated = elevated;
        var import = Import(Info(new TaskAccount(exported, @"PC\Somebody")));

        import.AccountIndex = accountIndex;
        import.HighestPrivileges = highest;

        Assert.Equal(expected, import.NeedsElevation);
    }

    [Fact]
    public async Task ATaskOfTheSameNameInTheFolder_IsNotReplaced()
    {
        _store.Add(@"\Backup", draft: new TaskDraft { Description = "The one already there" });
        var import = Import(Info(TaskAccount.CurrentUser));

        Assert.False(await import.ImportAsync());

        Assert.Equal("Import.Error.Exists", import.Problem);
        Assert.Null(import.ImportedPath);
        Assert.Equal("The one already there", _store.Load(@"\Backup").Description);
    }

    [Theory]
    [InlineData(true, "Editor.Error.AccessDenied")]
    [InlineData(false, "Editor.Error.AccessDeniedNotElevated")]
    public async Task WhenWindowsRefusesTheImport_TheReasonIsShown(bool elevated, string expected)
    {
        _app.IsElevated = elevated;
        _store.Before = (_, path) => throw new TaskAccessDeniedException(path, new UnauthorizedAccessException());
        var import = Import(Info(TaskAccount.CurrentUser));

        Assert.False(await import.ImportAsync());

        Assert.Equal(expected, import.Problem);
        Assert.Null(import.ImportedPath);
    }
}
