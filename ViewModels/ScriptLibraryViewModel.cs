using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Windows.ApplicationModel;

namespace FluentTaskScheduler.ViewModels
{
    public class ScriptTemplateModel
    {
        /// <summary>
        /// Stable identifier for a built-in script, used to look up its translated name and
        /// description (Scripts.&lt;Id&gt;.Name / .Description). Empty for user templates, whose
        /// text the user wrote themselves and must never be replaced by a translation.
        /// </summary>
        public string Id { get; set; } = "";

        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string Command { get; set; } = "";
        public string Arguments { get; set; } = "";
        public bool RunAsAdmin { get; set; }

        // Runtime-only, not serialized
        [JsonIgnore] public bool IsUserTemplate { get; set; }
        [JsonIgnore] public Visibility DeleteVisibility => IsUserTemplate ? Visibility.Visible : Visibility.Collapsed;
        [JsonIgnore] public string UseLabel =>
            Services.LocalizationService.GetString("ScriptLibraryUseTemplateBtn.Content", "Create Task from Script");
    }

    public class ScriptLibraryViewModel
    {
        private static readonly string _userTemplatesPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FluentTaskScheduler", "user_templates.json");

        private static readonly JsonSerializerOptions _json = new() { WriteIndented = true };

        /// <summary>Every loaded script (built-in + user), independent of the current search filter.</summary>
        private readonly List<ScriptTemplateModel> _allScripts = new();
        private string _currentFilter = "";

        /// <summary>The subset of <see cref="_allScripts"/> matching the current search filter — this is what the UI binds to.</summary>
        public ObservableCollection<ScriptTemplateModel> Scripts { get; } = new();

        /// <summary>
        /// Loads built-in and user scripts. Pass <paramref name="force"/> to rebuild the list after
        /// a language change — built-in names/descriptions are translated at load time, and
        /// ScriptTemplateModel has no change notification, so the collection has to be repopulated
        /// for the new locale to reach the UI.
        /// </summary>
        public async Task LoadScriptsAsync(bool force = false)
        {
            if (_allScripts.Count > 0 && !force) return;
            if (force) _allScripts.Clear();

            // Built-in templates
            try
            {
                string json = "";
                string fullPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Scripts.json");

                if (File.Exists(fullPath))
                    json = await File.ReadAllTextAsync(fullPath);
                else
                {
                    try
                    {
                        var storageFile = await Package.Current.InstalledLocation.GetFileAsync("Assets\\Scripts.json");
                        json = await Windows.Storage.FileIO.ReadTextAsync(storageFile);
                    }
                    catch { }
                }

                if (!string.IsNullOrWhiteSpace(json))
                {
                    var data = JsonSerializer.Deserialize<List<ScriptTemplateModel>>(json);
                    if (data != null)
                    {
                        foreach (var script in data) LocalizeBuiltIn(script);
                        _allScripts.AddRange(data);
                    }
                }
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Error loading scripts: {ex}"); }

            // User templates (appended after built-ins)
            LoadUserTemplates();
            ApplyFilter(_currentFilter);
        }

        /// <summary>
        /// Swaps a built-in script's English name/description for the current language's. The text
        /// shipped in Scripts.json stays the fallback, so a script with no Id — or a missing
        /// translation — keeps reading correctly instead of showing a raw resource key.
        /// </summary>
        private static void LocalizeBuiltIn(ScriptTemplateModel script)
        {
            if (string.IsNullOrWhiteSpace(script.Id)) return;

            script.Name = Services.LocalizationService.GetString($"Scripts.{script.Id}.Name", script.Name);
            script.Description = Services.LocalizationService.GetString($"Scripts.{script.Id}.Description", script.Description);
        }

        public void AddUserTemplate(ScriptTemplateModel model)
        {
            model.IsUserTemplate = true;
            _allScripts.Add(model);
            SaveUserTemplates();
            ApplyFilter(_currentFilter);
        }

        internal static readonly string _userScriptsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FluentTaskScheduler", "Scripts");

        public void DeleteUserTemplate(ScriptTemplateModel model)
        {
            _allScripts.Remove(model);
            SaveUserTemplates();
            ApplyFilter(_currentFilter);
            DeleteBackingScriptFileIfOwned(model);
        }

        /// <summary>
        /// Deletes the .ps1 file a Script Editor-saved template points at, if any. Without this,
        /// removing the template left an orphaned script under LocalAppData\Scripts forever — and
        /// if any scheduled task still referenced that file's path directly, that task would now
        /// silently fail to run (see 2.9).
        /// </summary>
        internal static void DeleteBackingScriptFileIfOwned(ScriptTemplateModel model)
        {
            try
            {
                var match = System.Text.RegularExpressions.Regex.Match(model.Arguments ?? "", "-File\\s+\"([^\"]+)\"");
                if (!match.Success) return;

                string scriptPath = match.Groups[1].Value;
                string fullScriptPath = Path.GetFullPath(scriptPath);
                string fullScriptsDir = Path.GetFullPath(_userScriptsDir);

                if (fullScriptPath.StartsWith(fullScriptsDir, StringComparison.OrdinalIgnoreCase) && File.Exists(fullScriptPath))
                {
                    File.Delete(fullScriptPath);
                }
            }
            catch (Exception ex)
            {
                Services.LogService.Warn($"Could not delete backing script file for template '{model.Name}': {ex.Message}");
            }
        }

        /// <summary>Filters the displayed <see cref="Scripts"/> by name or description. Empty/null clears the filter.</summary>
        public void ApplyFilter(string? query)
        {
            _currentFilter = query ?? "";

            IEnumerable<ScriptTemplateModel> results = _allScripts;
            if (!string.IsNullOrWhiteSpace(_currentFilter))
            {
                results = _allScripts.Where(s =>
                    (s.Name != null && s.Name.Contains(_currentFilter, StringComparison.OrdinalIgnoreCase)) ||
                    (s.Description != null && s.Description.Contains(_currentFilter, StringComparison.OrdinalIgnoreCase)));
            }

            Scripts.Clear();
            foreach (var s in results) Scripts.Add(s);
        }

        private void LoadUserTemplates()
        {
            try
            {
                if (!File.Exists(_userTemplatesPath)) return;
                var json = File.ReadAllText(_userTemplatesPath);
                var data = JsonSerializer.Deserialize<List<ScriptTemplateModel>>(json);
                if (data != null)
                    foreach (var item in data) { item.IsUserTemplate = true; _allScripts.Add(item); }
            }
            catch { }
        }

        private void SaveUserTemplates()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_userTemplatesPath)!);
                var userTemplates = _allScripts.Where(s => s.IsUserTemplate).ToList();
                File.WriteAllText(_userTemplatesPath, JsonSerializer.Serialize(userTemplates, _json));
            }
            catch { }
        }
    }
}
