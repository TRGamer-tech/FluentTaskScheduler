using System.Text.Json;
using FluentTaskScheduler.Library;
using FluentTaskScheduler.Tests.Support;

namespace FluentTaskScheduler.Tests.Library;

public sealed class ScriptStoreTests : IDisposable
{
    private readonly TempFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    private string ScriptsFolder => _folder.Paths.ScriptsFolder;
    private string ListFile => Path.Combine(ScriptsFolder, "scripts.json");

    private ScriptStore NewStore() => NewStore(_folder.Paths);

    private ScriptStore NewStore(AppPaths paths)
    {
        string builtIn = _folder.File("built-in.json");
        File.WriteAllText(builtIn, """[{ "Id": "Ping", "Command": "ping.exe", "Arguments": "localhost", "RunAsAdmin": true }]""");
        return new ScriptStore(paths, builtIn);
    }

    private static LibraryScript OnlyUserScript(ScriptStore store) => Assert.Single(store.GetScripts(), script => !script.IsBuiltIn);

    [Fact]
    public void WithoutSavedScripts_TheLibraryHoldsTheBuiltInOnes_AndWritesNothing()
    {
        LibraryScript script = Assert.Single(NewStore().GetScripts());

        Assert.Equal(new LibraryScript
        {
            Id = "Ping",
            Name = "Scripts.Ping.Name",
            Description = "Scripts.Ping.Description",
            Command = "ping.exe",
            Arguments = "localhost",
            RunElevated = true,
            IsBuiltIn = true
        }, script);
        Assert.False(Directory.Exists(ScriptsFolder));
    }

    [Fact]
    public void ASavedScript_IsListedBeforeTheBuiltInOnes_AndRunsItsOwnFile()
    {
        ScriptStore store = NewStore();

        LibraryScript saved = store.Save(null, "Tidy up", "Removes old files.", "Remove-Item old.txt");

        Assert.Equal([saved.Id, "Ping"], store.GetScripts().Select(script => script.Id));
        Assert.Equal("Tidy up", saved.Name);
        Assert.Equal("Removes old files.", saved.Description);
        Assert.True(saved.HasText);
        Assert.False(saved.IsBuiltIn);
        Assert.Equal("powershell.exe", saved.Command);

        string file = WindowsCommandLine.Split(saved.Arguments)[^1];
        Assert.Equal(ScriptsFolder, Path.GetDirectoryName(file));
        Assert.Equal("Remove-Item old.txt", File.ReadAllText(file));
        Assert.Equal("Remove-Item old.txt", store.ReadText(saved.Id));
    }

    [Fact]
    public void AScriptFile_IsWrittenSoThatWindowsPowerShellReadsItRight()
    {
        ScriptStore store = NewStore();

        LibraryScript saved = store.Save(null, "Greeting", "", "Write-Output 'Zürich'\rWrite-Output '東京'\n");

        byte[] bytes = File.ReadAllBytes(WindowsCommandLine.Split(saved.Arguments)[^1]);
        // Windows PowerShell 5.1 needs the byte order mark to recognise UTF-8.
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
        Assert.Equal("Write-Output 'Zürich'\r\nWrite-Output '東京'\r\n", System.Text.Encoding.UTF8.GetString(bytes[3..]));
    }

    [Fact]
    public void SavingAgain_ChangesNameDescriptionAndText_ButNotTheFileTasksReferTo()
    {
        ScriptStore store = NewStore();
        LibraryScript first = store.Save(null, "Tidy up", "", "Write-Output 1");

        LibraryScript second = store.Save(first.Id, "Tidy up thoroughly", "Now with more.", "Write-Output 2");

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(first.Arguments, second.Arguments);
        LibraryScript listed = OnlyUserScript(store);
        Assert.Equal("Tidy up thoroughly", listed.Name);
        Assert.Equal("Now with more.", listed.Description);
        Assert.Equal("Write-Output 2", store.ReadText(first.Id));
    }

    [Fact]
    public void SavingUnderAnIdTheLibraryDoesNotHave_MakesANewScript()
    {
        ScriptStore store = NewStore();
        LibraryScript deleted = store.Save(null, "Gone", "", "Write-Output 1");
        store.Delete(deleted.Id);

        LibraryScript again = store.Save(deleted.Id, "Back", "", "Write-Output 2");

        Assert.NotEqual(deleted.Id, again.Id);
        Assert.Equal("Back", OnlyUserScript(store).Name);
    }

    [Fact]
    public void ABuiltInScript_CanBeNeitherOverwrittenNorDeleted()
    {
        ScriptStore store = NewStore();

        LibraryScript saved = store.Save("Ping", "My ping", "", "ping.exe example.org");
        store.Delete("Ping");

        Assert.NotEqual("Ping", saved.Id);
        LibraryScript builtIn = Assert.Single(store.GetScripts(), script => script.IsBuiltIn);
        Assert.Equal("ping.exe", builtIn.Command);
        Assert.Equal("localhost", builtIn.Arguments);
        Assert.Throws<FileNotFoundException>(() => store.ReadText("Ping"));
    }

    [Fact]
    public void DeletingAScript_RemovesItFromTheListAndItsFileFromTheFolder_AndNothingElse()
    {
        ScriptStore store = NewStore();
        LibraryScript keep = store.Save(null, "Keep", "", "Write-Output 'keep'");
        LibraryScript delete = store.Save(null, "Delete", "", "Write-Output 'delete'");
        string deletedFile = WindowsCommandLine.Split(delete.Arguments)[^1];

        store.Delete(delete.Id);

        Assert.Equal(keep.Id, OnlyUserScript(store).Id);
        Assert.False(File.Exists(deletedFile));
        Assert.Equal("Write-Output 'keep'", store.ReadText(keep.Id));
        Assert.Throws<FileNotFoundException>(() => store.ReadText(delete.Id));
    }

    [Theory]
    [InlineData("../outside.ps1")]
    [InlineData(@"..\outside.ps1")]
    [InlineData("../Scripts2/outside.ps1")]
    [InlineData("C:outside.ps1")]
    [InlineData("outside.txt")]
    [InlineData("..")]
    public void AListEntryThatPointsOutOfTheScriptsFolder_IsNeverFollowed(string fileInList)
    {
        // The folder next door starts with the same characters as the scripts folder.
        Directory.CreateDirectory(ScriptsFolder + "2");
        string[] outside = [_folder.File("outside.ps1"), _folder.File("outside.txt"), Path.Combine(ScriptsFolder + "2", "outside.ps1"), Path.Combine(ScriptsFolder, "outside.txt")];
        Directory.CreateDirectory(ScriptsFolder);
        foreach (string file in outside) File.WriteAllText(file, "untouched");
        File.WriteAllText(ListFile, JsonSerializer.Serialize(new[] { new { Id = "x", Name = "Trap", File = fileInList } }));
        ScriptStore store = NewStore();

        Assert.DoesNotContain(store.GetScripts(), script => script.Id == "x");
        Assert.Throws<FileNotFoundException>(() => store.ReadText("x"));
        store.Save("x", "Trap", "", "overwritten");
        store.Delete("x");

        Assert.All(outside, file => Assert.Equal("untouched", File.ReadAllText(file)));
    }

    [Fact]
    public void Changed_IsRaisedForEverySaveAndDelete()
    {
        ScriptStore store = NewStore();
        int raised = 0;
        store.Changed += (_, _) => raised++;

        LibraryScript saved = store.Save(null, "One", "", "1");
        store.Save(saved.Id, "One", "", "2");
        store.Delete(saved.Id);
        store.Delete(saved.Id);

        Assert.Equal(3, raised);
    }

    [Fact]
    public void AnotherStoreOnTheSameFolder_SeesWhatWasSaved()
    {
        LibraryScript saved = NewStore().Save(null, "Tidy up", "Removes old files.", "Remove-Item old.txt");

        Assert.Equal(saved, OnlyUserScript(NewStore()));
    }

    [Theory]
    [InlineData("")]
    [InlineData("[{ \"Id\": \"a\", \"Name\": \"Half")]
    [InlineData("{ \"Id\": \"a\" }")]
    public void AListThatCannotBeRead_IsSetAside_AndTheLibraryStartsOver(string content)
    {
        Directory.CreateDirectory(ScriptsFolder);
        File.WriteAllText(ListFile, content);
        // The list of version 1.9 was taken over when the broken list was first written; starting
        // over must not bring its scripts back.
        File.WriteAllText(_folder.File("user_templates.json"), """[{ "Name": "From 1.9", "Command": "robocopy.exe" }]""");
        ScriptStore store = NewStore();

        Assert.All(store.GetScripts(), script => Assert.True(script.IsBuiltIn));
        Assert.All(store.GetScripts(), script => Assert.True(script.IsBuiltIn));

        Assert.NotNull(store.UnreadableListBackup);
        Assert.Equal(content, File.ReadAllText(store.UnreadableListBackup));
        store.Save(null, "New", "", "Write-Output 1");
        Assert.Equal("New", OnlyUserScript(store).Name);
        Assert.Equal(content, File.ReadAllText(store.UnreadableListBackup));
    }

    [Fact]
    public void AListThatCannotBeRead_IsSetAside_WithoutReplacingOneSetAsideBefore()
    {
        Directory.CreateDirectory(ScriptsFolder);
        File.WriteAllText(ListFile, "{ broken");
        ScriptStore first = NewStore();
        first.GetScripts();

        File.WriteAllText(ListFile, "{ broken again");
        ScriptStore afterARestart = NewStore();
        afterARestart.GetScripts();

        Assert.Equal("{ broken", File.ReadAllText(first.UnreadableListBackup!));
        Assert.Equal("{ broken again", File.ReadAllText(afterARestart.UnreadableListBackup!));
    }

    [Fact]
    public void ScriptsSavedByVersion19_AreTakenOver_WithoutMovingTheirFiles()
    {
        // As 1.9 wrote it: one list for everything, next to the Scripts folder, with a script
        // from its editor as a command line that names the file.
        Directory.CreateDirectory(ScriptsFolder);
        string editorScript = Path.Combine(ScriptsFolder, "0f8fad5b-d9cb-469f-a165-70867728950e.ps1");
        File.WriteAllText(editorScript, "Write-Output 'from 1.9'");
        // A script of the user's own, in a folder whose path merely starts like the scripts folder.
        string elsewhere = Path.Combine(Directory.CreateDirectory(ScriptsFolder + "2").FullName, "mine.ps1");
        File.WriteAllText(elsewhere, "Write-Output 'mine'");
        File.WriteAllText(_folder.File("user_templates.json"), JsonSerializer.Serialize(new object[]
        {
            new { Id = "", Name = "From the editor", Description = "d1", Command = "powershell.exe", Arguments = $"-ExecutionPolicy Bypass -File \"{editorScript}\"", RunAsAdmin = false },
            new { Id = "", Name = "Plain command", Description = "d2", Command = "robocopy.exe", Arguments = @"C:\a D:\b /MIR", RunAsAdmin = true },
            new { Id = "", Name = "Own file", Description = "d3", Command = "powershell.exe", Arguments = $"-File \"{elsewhere}\"", RunAsAdmin = false }
        }));
        ScriptStore store = NewStore();

        var scripts = store.GetScripts().Where(script => !script.IsBuiltIn).ToList();

        Assert.Equal(["From the editor", "Plain command", "Own file"], scripts.Select(script => script.Name));
        Assert.Equal(3, scripts.Select(script => script.Id).Distinct().Count());

        Assert.True(scripts[0].HasText);
        Assert.Equal("Write-Output 'from 1.9'", store.ReadText(scripts[0].Id));
        Assert.Equal(editorScript, WindowsCommandLine.Split(scripts[0].Arguments)[^1]);

        Assert.Equal(("robocopy.exe", @"C:\a D:\b /MIR", true, false), (scripts[1].Command, scripts[1].Arguments, scripts[1].RunElevated, scripts[1].HasText));
        Assert.Equal(("powershell.exe", $"-File \"{elsewhere}\"", false), (scripts[2].Command, scripts[2].Arguments, scripts[2].HasText));

        // Deleting what 1.9 saved follows the same rule: only files in the scripts folder are the library's.
        foreach (LibraryScript script in scripts) store.Delete(script.Id);
        Assert.False(File.Exists(editorScript));
        Assert.True(File.Exists(elsewhere));
        Assert.All(NewStore().GetScripts(), script => Assert.True(script.IsBuiltIn));
    }

    /// <summary>
    /// An account that version 1.9 saved two scripts for, after version 2.0 has started once:
    /// the list and a copy of each script are in the data folder of today, and the scripts are
    /// also still where version 1.9 put them and where its list says they are.
    /// </summary>
    private sealed class MovedFromVersion19
    {
        public MovedFromVersion19(string localAppData)
        {
            Paths = new AppPaths(null, localAppData);
            string oldFolder = Paths.Version19!.ScriptsFolder;
            OldCopy = Path.Combine(oldFolder, "0f8fad5b-d9cb-469f-a165-70867728950e.ps1");
            OtherOldCopy = Path.Combine(oldFolder, "7c9e6679-7425-40de-944b-e07fc1f90ae7.ps1");
            Directory.CreateDirectory(oldFolder);
            File.WriteAllText(OldCopy, "Write-Output 'tidy'");
            File.WriteAllText(OtherOldCopy, "Write-Output 'other'");
            File.WriteAllText(Paths.Version19.File("user_templates.json"), JsonSerializer.Serialize(new object[]
            {
                new { Id = "", Name = "Tidy up", Description = "", Command = "powershell.exe", Arguments = $"-ExecutionPolicy Bypass -File \"{OldCopy}\"", RunAsAdmin = false },
                new { Id = "", Name = "Other", Description = "", Command = "powershell.exe", Arguments = $"-ExecutionPolicy Bypass -File \"{OtherOldCopy}\"", RunAsAdmin = false }
            }));

            Assert.Empty(DataFolderMove.Run(Paths).Failures);
        }

        public AppPaths Paths { get; }

        /// <summary>The first script where version 1.9 saved it, which is where the tasks it created from the script look for it.</summary>
        public string OldCopy { get; }
        public string OtherOldCopy { get; }

        /// <summary>The first script in the scripts folder of today.</summary>
        public string Script => Path.Combine(Paths.ScriptsFolder, Path.GetFileName(OldCopy));
    }

    [Fact]
    public void AScriptSavedByVersion19_IsTheLibrarysOwn_OnceTheAppsFilesHaveMoved()
    {
        var account = new MovedFromVersion19(_folder.Path);
        ScriptStore store = NewStore(account.Paths);

        LibraryScript script = store.GetScripts().First(script => script.Name == "Tidy up");

        Assert.True(script.HasText);
        Assert.Equal("Write-Output 'tidy'", store.ReadText(script.Id));
        // A task created from it now runs the script from the folder of today.
        Assert.Equal(account.Script, WindowsCommandLine.Split(script.Arguments)[^1]);
    }

    [Fact]
    public void ChangingAScriptSavedByVersion19_AlsoChangesTheCopyThatOlderTasksRun()
    {
        var account = new MovedFromVersion19(_folder.Path);
        ScriptStore store = NewStore(account.Paths);
        LibraryScript script = store.GetScripts().First(script => script.Name == "Tidy up");

        store.Save(script.Id, "Tidy up", "", "Write-Output 'tidier'");

        Assert.Equal("Write-Output 'tidier'", File.ReadAllText(account.Script));
        Assert.Equal(File.ReadAllBytes(account.Script), File.ReadAllBytes(account.OldCopy));
        Assert.Equal("Write-Output 'other'", File.ReadAllText(account.OtherOldCopy));
    }

    [Fact]
    public void DeletingAScriptSavedByVersion19_AlsoRemovesTheCopyThatOlderTasksRun()
    {
        var account = new MovedFromVersion19(_folder.Path);
        ScriptStore store = NewStore(account.Paths);
        LibraryScript script = store.GetScripts().First(script => script.Name == "Tidy up");

        store.Delete(script.Id);

        Assert.False(File.Exists(account.Script));
        Assert.False(File.Exists(account.OldCopy));
        Assert.True(File.Exists(account.OtherOldCopy));
    }

    [Fact]
    public void AScriptSavedNow_IsNotPutWhereVersion19KeptItsScripts()
    {
        var account = new MovedFromVersion19(_folder.Path);
        ScriptStore store = NewStore(account.Paths);

        LibraryScript saved = store.Save(null, "New", "", "Write-Output 'new'");
        store.Save(saved.Id, "New", "", "Write-Output 'newer'");

        Assert.Equal([account.OldCopy, account.OtherOldCopy], Directory.GetFiles(account.Paths.Version19!.ScriptsFolder).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void AScriptOfVersion19ThatHasNotComeOver_StaysTheCommandLineThatRunsItWhereItIs()
    {
        var account = new MovedFromVersion19(_folder.Path);
        File.Delete(account.Script);
        ScriptStore store = NewStore(account.Paths);

        LibraryScript script = store.GetScripts().First(script => script.Name == "Tidy up");

        Assert.False(script.HasText);
        Assert.Equal(("powershell.exe", $"-ExecutionPolicy Bypass -File \"{account.OldCopy}\""), (script.Command, script.Arguments));
        store.Delete(script.Id);
        Assert.True(File.Exists(account.OldCopy));
    }
}
