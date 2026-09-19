using System;
using System.IO;
using FluentTaskScheduler.ViewModels;

namespace FluentTaskScheduler.Tests
{
    // Covers item 2.9d: deleting a Script Editor-saved template must also delete its backing .ps1
    // file under LocalAppData\Scripts, but must never touch a file outside that folder (e.g. a
    // built-in template's Command/Arguments, or something a user hand-typed into a custom template).
    public class ScriptLibraryViewModelTests : IDisposable
    {
        private readonly string _scriptPath;

        public ScriptLibraryViewModelTests()
        {
            Directory.CreateDirectory(ScriptLibraryViewModel._userScriptsDir);
            _scriptPath = Path.Combine(ScriptLibraryViewModel._userScriptsDir, Guid.NewGuid().ToString("N") + ".ps1");
        }

        public void Dispose()
        {
            try { if (File.Exists(_scriptPath)) File.Delete(_scriptPath); } catch { }
        }

        [Fact]
        public void DeletesTheBackingScriptFile_WhenArgumentsPointInsideTheScriptsFolder()
        {
            File.WriteAllText(_scriptPath, "Write-Host 'hi'");
            var model = new ScriptTemplateModel
            {
                Name = "Test",
                Command = "powershell.exe",
                Arguments = $"-ExecutionPolicy Bypass -File \"{_scriptPath}\""
            };

            ScriptLibraryViewModel.DeleteBackingScriptFileIfOwned(model);

            Assert.False(File.Exists(_scriptPath));
        }

        [Fact]
        public void DoesNotDeleteFiles_OutsideTheUserScriptsFolder()
        {
            string outsidePath = Path.Combine(Path.GetTempPath(), "FTS_Tests_outside_" + Guid.NewGuid().ToString("N") + ".ps1");
            File.WriteAllText(outsidePath, "Write-Host 'hi'");
            try
            {
                var model = new ScriptTemplateModel
                {
                    Name = "Test",
                    Command = "powershell.exe",
                    Arguments = $"-ExecutionPolicy Bypass -File \"{outsidePath}\""
                };

                ScriptLibraryViewModel.DeleteBackingScriptFileIfOwned(model);

                Assert.True(File.Exists(outsidePath));
            }
            finally
            {
                try { File.Delete(outsidePath); } catch { }
            }
        }

        [Fact]
        public void DoesNothing_WhenArgumentsHaveNoFileSwitch()
        {
            var model = new ScriptTemplateModel
            {
                Name = "Built-in",
                Command = "sfc",
                Arguments = "/scannow"
            };

            // Should not throw even though there's nothing to parse out.
            ScriptLibraryViewModel.DeleteBackingScriptFileIfOwned(model);
        }
    }
}
