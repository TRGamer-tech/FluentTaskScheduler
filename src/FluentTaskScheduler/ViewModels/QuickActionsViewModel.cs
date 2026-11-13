using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using FluentTaskScheduler.Helpers;
using FluentTaskScheduler.Services;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml;

namespace FluentTaskScheduler.ViewModels
{
    public enum QuickActionStatus
    {
        Idle,
        Running,
        Success,
        Error
    }

    public class QuickActionItemViewModel : INotifyPropertyChanged
    {
        private QuickActionStatus _status = QuickActionStatus.Idle;
        private string _statusMessage = "";

        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public string Icon { get; set; } = "\uE71D"; // Default System icon
        public string Command { get; set; } = "";
        public string Arguments { get; set; } = "";
        public bool AdminRequired { get; set; }
        public string RunText { get; set; } = LocalizationService.GetString("QuickActions.RunBtn", "Run");

        // Incremented on every ExecuteAction call so a stale reset-to-Idle timer from a
        // previous run can't clobber the status of a newer run started within the reset delay.
        internal int RunToken { get; set; }

        public QuickActionStatus Status
        {
            get => _status;
            set { if (_status != value) { _status = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusText)); OnPropertyChanged(nameof(StatusColor)); } }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set { if (_statusMessage != value) { _statusMessage = value; OnPropertyChanged(); } }
        }

        public string StatusText => Status switch
        {
            QuickActionStatus.Running => LocalizationService.GetString("QuickActions.Status.Running", "Running..."),
            QuickActionStatus.Success => LocalizationService.GetString("QuickActions.Status.Success", "Success"),
            QuickActionStatus.Error => LocalizationService.GetString("QuickActions.Status.Error", "Error"),
            _ => ""
        };

        public Brush StatusColor => Status switch
        {
            QuickActionStatus.Running => (Brush)Application.Current.Resources["SystemControlBackgroundAccentBrush"],
            QuickActionStatus.Success => (Brush)Application.Current.Resources["SystemFillColorSuccessBrush"],
            QuickActionStatus.Error => (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"],
            _ => new SolidColorBrush(Colors.Transparent)
        };

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class QuickActionsViewModel : INotifyPropertyChanged
    {
        private bool _isLoading;
        public ObservableCollection<QuickActionItemViewModel> Actions { get; } = new ObservableCollection<QuickActionItemViewModel>();

        public bool IsLoading
        {
            get => _isLoading;
            set { if (_isLoading != value) { _isLoading = value; OnPropertyChanged(); } }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public QuickActionsViewModel()
        {
            InitializeActions();
        }

        private void InitializeActions()
        {
            Actions.Add(new QuickActionItemViewModel
            {
                Id = "flushdns",
                Title = LocalizationService.GetString("QuickActions.FlushDNS.Title", "Flush DNS"),
                Description = LocalizationService.GetString("QuickActions.FlushDNS.Desc", "Clears the DNS resolver cache."),
                Icon = "\uE945", // Cloud
                Command = "ipconfig",
                Arguments = "/flushdns"
            });

            Actions.Add(new QuickActionItemViewModel
            {
                Id = "cleartemp",
                Title = LocalizationService.GetString("QuickActions.ClearTemp.Title", "Clear Temp Files"),
                Description = LocalizationService.GetString("QuickActions.ClearTemp.Desc", "Deletes files from the temporary folders."),
                Icon = "\uE74D", // Delete
                // "del /q /s" has an unreliable exit code (e.g. it still returns 0 on partial
                // failures and vice versa); Remove-Item gives a real success/failure signal.
                Command = "powershell.exe",
                Arguments = "-ExecutionPolicy Bypass -Command \"Remove-Item -Path $env:TEMP\\* -Recurse -Force -ErrorAction SilentlyContinue\""
            });

            Actions.Add(new QuickActionItemViewModel
            {
                Id = "restartexplorer",
                Title = LocalizationService.GetString("QuickActions.RestartExplorer.Title", "Restart Explorer"),
                Description = LocalizationService.GetString("QuickActions.RestartExplorer.Desc", "Restarts the Windows Explorer process."),
                Icon = "\uE895", // Refresh
                Command = "cmd.exe",
                Arguments = "/c taskkill /f /im explorer.exe & start explorer.exe"
            });

            Actions.Add(new QuickActionItemViewModel
            {
                Id = "iprenew",
                Title = LocalizationService.GetString("QuickActions.IPReleaseRenew.Title", "Renew IP Address"),
                Description = LocalizationService.GetString("QuickActions.IPReleaseRenew.Desc", "Releases and renews the current IP address."),
                Icon = "\uE839", // Ethernet
                Command = "ipconfig",
                Arguments = "/renew",
                AdminRequired = true
            });

            Actions.Add(new QuickActionItemViewModel
            {
                Id = "sfc",
                Title = LocalizationService.GetString("QuickActions.SfcScannow.Title", "System File Checker"),
                Description = LocalizationService.GetString("QuickActions.SfcScannow.Desc", "Scans and repairs corrupted system files (Requires Admin)."),
                Icon = "\uE73D", // Shield
                Command = "sfc",
                Arguments = "/scannow",
                AdminRequired = true
            });

            Actions.Add(new QuickActionItemViewModel
            {
                Id = "dism",
                Title = LocalizationService.GetString("QuickActions.DismHealth.Title", "DISM Health Check"),
                Description = LocalizationService.GetString("QuickActions.DismHealth.Desc", "Checks for component store corruption (Requires Admin)."),
                Icon = "\uE9D9", // Health
                Command = "DISM",
                Arguments = "/Online /Cleanup-Image /CheckHealth",
                AdminRequired = true
            });
        }

        public async Task ExecuteAction(QuickActionItemViewModel action)
        {
            if (action.Status == QuickActionStatus.Running) return;

            int runToken = ++action.RunToken;
            action.Status = QuickActionStatus.Running;
            action.StatusMessage = "";

            try
            {
                await Task.Run(() =>
                {
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = action.Command,
                        Arguments = action.Arguments,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };

                    if (action.AdminRequired)
                    {
                        // Check if we are already elevated
                        if (!ElevationHelper.IsElevated())
                        {
                            throw new UnauthorizedAccessException("This action requires Administrator privileges. Please restart the app as Administrator.");
                        }
                    }

                    using (var process = Process.Start(startInfo))
                    {
                        if (process == null) throw new Exception("Failed to start process.");

                        // Read stdout/stderr asynchronously while waiting - reading only one stream
                        // (or reading after WaitForExit) can deadlock if the child fills the OS pipe
                        // buffer on the other stream before exiting (classic Process redirect deadlock).
                        var errorBuilder = new System.Text.StringBuilder();
                        process.OutputDataReceived += (_, args) => { };
                        process.ErrorDataReceived += (_, args) => { if (args.Data != null) errorBuilder.AppendLine(args.Data); };
                        process.BeginOutputReadLine();
                        process.BeginErrorReadLine();

                        process.WaitForExit();

                        if (process.ExitCode != 0)
                        {
                            throw new Exception($"Process exited with code {process.ExitCode}. {errorBuilder}");
                        }
                    }
                });

                action.Status = QuickActionStatus.Success;
            }
            catch (Exception ex)
            {
                action.Status = QuickActionStatus.Error;
                action.StatusMessage = ex.Message;
                Serilog.Log.Error("{Message}", $"Quick Action '{action.Title}' failed: {ex.Message}");
            }

            // Reset to idle after a few seconds - only if a newer run hasn't started in the meantime.
            _ = Task.Delay(5000).ContinueWith(_ =>
            {
                if (action.RunToken == runToken)
                    action.Status = QuickActionStatus.Idle;
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
