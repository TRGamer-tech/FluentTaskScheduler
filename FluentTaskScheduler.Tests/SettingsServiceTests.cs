using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentTaskScheduler.Services;

namespace FluentTaskScheduler.Tests
{
    [Collection("StaticServices")]
    public class SettingsServiceTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly SettingsService _settings;

        public SettingsServiceTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "FTS_Tests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
            _settings = new SettingsService(_tempDir);
        }

        public void Dispose()
        {
            _settings.Dispose();
            try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); } catch { }
        }

        [Fact]
        public void SettingChanged_IsReadableImmediately_EvenBeforeDebouncedSaveFlushes()
        {
            _settings.ConfirmDelete = false;
            Assert.False(_settings.ConfirmDelete);
        }

        [Fact]
        public void Flush_WritesPendingChangeToDiskImmediately()
        {
            _settings.IsMicaEnabled = false;
            _settings.Flush();

            string settingsFile = Path.Combine(_tempDir, "settings.json");
            Assert.True(File.Exists(settingsFile));

            var onDisk = JsonSerializer.Deserialize<JsonDocument>(File.ReadAllText(settingsFile));
            Assert.False(onDisk!.RootElement.GetProperty("IsMicaEnabled").GetBoolean());
        }

        [Fact]
        public async Task RapidSuccessiveWrites_AreCoalescedIntoOneDebouncedSave()
        {
            // Covers item 1.8: continuous window-resize events must not thrash the disk with one
            // write per tick.
            for (int i = 0; i < 50; i++)
            {
                _settings.SetWindowSize(1000 + i, 700 + i);
            }

            // Nothing should have hit disk yet — the debounce window hasn't elapsed.
            string settingsFile = Path.Combine(_tempDir, "settings.json");
            Assert.False(File.Exists(settingsFile));

            _settings.Flush();

            Assert.True(File.Exists(settingsFile));
            Assert.Equal(1049, _settings.WindowWidth);
            Assert.Equal(749, _settings.WindowHeight);
        }

        [Fact]
        public void ConcurrentWrites_FromMultipleThreads_DoNotCorruptTheFile()
        {
            // Covers item 1.8: Save() is called from the UI thread, the snooze timer thread, and
            // pipeline/reminder threads concurrently.
            var threads = new Thread[8];
            for (int t = 0; t < threads.Length; t++)
            {
                int captured = t;
                threads[t] = new Thread(() =>
                {
                    for (int i = 0; i < 20; i++)
                    {
                        _settings.ReminderLeadMinutes = captured * 100 + i;
                    }
                });
                threads[t].Start();
            }
            foreach (var th in threads) th.Join();

            _settings.Flush();

            string settingsFile = Path.Combine(_tempDir, "settings.json");
            string json = File.ReadAllText(settingsFile);
            // A corrupted/interleaved write would fail to parse.
            var doc = JsonSerializer.Deserialize<JsonDocument>(json);
            Assert.NotNull(doc);
        }

        [Fact]
        public void SaveSnoozeState_WritesImmediately_NotDebounced()
        {
            // Covers item 1.6c: the disabled-task list must be durable *before* SnoozeService starts
            // disabling tasks, so a crash mid-sweep is recoverable. SaveSnoozeState must therefore
            // bypass the debounce window entirely.
            _settings.SaveSnoozeState(true, DateTime.UtcNow.AddHours(1), false, "", new() { "\\Some\\Task" });

            string settingsFile = Path.Combine(_tempDir, "settings.json");
            Assert.True(File.Exists(settingsFile));

            var onDisk = JsonSerializer.Deserialize<JsonDocument>(File.ReadAllText(settingsFile));
            Assert.True(onDisk!.RootElement.GetProperty("IsSnoozed").GetBoolean());
        }

        [Fact]
        public void Load_AfterSaveImmediate_RoundTripsAllFields()
        {
            _settings.EnableTaskPipelines = false;
            _settings.SavedCategories = new() { "A", "B" };
            _settings.Flush();

            _settings.Load();

            Assert.False(_settings.EnableTaskPipelines);
            Assert.Equal(new[] { "A", "B" }, _settings.SavedCategories);
        }

        // Covers item 4.5: proper add/remove methods instead of "mutate the live list, then
        // reassign the property to itself to trigger a save".
        [Fact]
        public void AddSavedCategory_PersistsAndIsIdempotent()
        {
            _settings.SavedCategories = new();
            _settings.AddSavedCategory("Work");
            _settings.AddSavedCategory("Work"); // duplicate — must not be added twice

            Assert.Equal(new[] { "Work" }, _settings.SavedCategories);
        }

        [Fact]
        public void RemoveSavedCategory_RemovesOnlyThatEntry()
        {
            _settings.SavedCategories = new() { "Work", "Personal" };
            _settings.RemoveSavedCategory("Work");

            Assert.Equal(new[] { "Personal" }, _settings.SavedCategories);
        }

        [Fact]
        public void AddSavedTag_PersistsAndIsIdempotent()
        {
            _settings.SavedTags = new();
            _settings.AddSavedTag("urgent");
            _settings.AddSavedTag("urgent");

            Assert.Equal(new[] { "urgent" }, _settings.SavedTags);
        }

        [Fact]
        public void RemoveSavedTag_RemovesOnlyThatEntry()
        {
            _settings.SavedTags = new() { "urgent", "sync" };
            _settings.RemoveSavedTag("urgent");

            Assert.Equal(new[] { "sync" }, _settings.SavedTags);
        }

        [Fact]
        public void AddSavedCategory_IgnoresEmptyOrWhitespace()
        {
            _settings.SavedCategories = new();
            _settings.AddSavedCategory("");
            _settings.AddSavedCategory("   ");

            Assert.Empty(_settings.SavedCategories);
        }

        // Covers item 4.5: exported settings must not carry this machine's live runtime state
        // (window size, active snooze) into another machine's config.
        [Fact]
        public void ExportSettings_ClearsRuntimeState()
        {
            _settings.SetWindowSize(2000, 1500);
            _settings.SaveSnoozeState(true, DateTime.UtcNow.AddHours(1), false, "", new() { "\\Some\\Task" });
            _settings.Flush();

            string exportPath = Path.Combine(_tempDir, "export.json");
            _settings.ExportSettings(exportPath);

            var exported = JsonSerializer.Deserialize<JsonDocument>(File.ReadAllText(exportPath))!.RootElement;
            Assert.False(exported.GetProperty("IsSnoozed").GetBoolean());
            Assert.Equal(0, exported.GetProperty("SnoozeDisabledTaskPaths").GetArrayLength());
            Assert.Equal(1200, exported.GetProperty("WindowWidth").GetInt32());
            Assert.Equal(800, exported.GetProperty("WindowHeight").GetInt32());

            // The exported file must not have mutated the live in-memory settings.
            Assert.True(_settings.IsSnoozed);
            Assert.Equal(2000, _settings.WindowWidth);
        }

        [Fact]
        public void ImportSettings_ClearsRuntimeStateEvenIfPresentInTheFile()
        {
            // Simulates importing a hand-edited or foreign settings.json that still has runtime
            // fields populated — those must not be allowed to resurrect a stale snooze.
            string importPath = Path.Combine(_tempDir, "import.json");
            File.WriteAllText(importPath, """
                {
                  "WindowWidth": 3000,
                  "WindowHeight": 2000,
                  "IsSnoozed": true,
                  "SnoozeUntilReboot": true,
                  "SnoozeDisabledTaskPaths": ["\\Some\\Task"]
                }
                """);

            _settings.ImportSettings(importPath);

            Assert.False(_settings.IsSnoozed);
            Assert.False(_settings.SnoozeUntilReboot);
            Assert.Empty(_settings.SnoozeDisabledTaskPaths);
            Assert.Equal(1200, _settings.WindowWidth);
            Assert.Equal(800, _settings.WindowHeight);
        }

        [Fact]
        public void ImportSettings_ThrowsOnInvalidJson()
        {
            string importPath = Path.Combine(_tempDir, "bad.json");
            File.WriteAllText(importPath, "not valid json");

            Assert.ThrowsAny<Exception>(() => _settings.ImportSettings(importPath));
        }
    }
}
