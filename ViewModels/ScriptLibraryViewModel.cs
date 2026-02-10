using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Windows.ApplicationModel;

namespace FluentTaskScheduler.ViewModels
{
    public class ScriptTemplateModel
    {
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string Command { get; set; } = "";
        public string Arguments { get; set; } = "";
        public bool RunAsAdmin { get; set; }
    }

    public class ScriptLibraryViewModel
    {
        public ObservableCollection<ScriptTemplateModel> Scripts { get; } = new();

        public async Task LoadScriptsAsync()
        {
            if (Scripts.Count > 0) return;

            try
            {
                // Path relative to execution or package
                // In packaged app, use Package.Current.InstalledLocation
                // But this is unpacked potentially? Let's try basic IO first, fallback to Package.
                
                string json = "";
                string fullPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Scripts.json");
                
                if (File.Exists(fullPath))
                {
                    json = await File.ReadAllTextAsync(fullPath);
                }
                else
                {
                    // Fallback for packaged app
                    try
                    {
                        var storageFile = await Package.Current.InstalledLocation.GetFileAsync("Assets\\Scripts.json");
                        json = await Windows.Storage.FileIO.ReadTextAsync(storageFile);
                    }
                    catch { }
                }

                if (!string.IsNullOrWhiteSpace(json))
                {
                    var data = JsonSerializer.Deserialize<System.Collections.Generic.List<ScriptTemplateModel>>(json);
                    if (data != null)
                    {
                        foreach (var item in data) Scripts.Add(item);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading scripts: {ex}");
            }
        }
    }
}
