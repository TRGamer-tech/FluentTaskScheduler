using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
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
                Command = "cmd.exe",
                Arguments = "/c del /q /s %temp%\\*"
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
                Arguments = "/renew"
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
                        if (!IsRunningAsAdmin())
                        {
                            throw new UnauthorizedAccessException("This action requires Administrator privileges. Please restart the app as Administrator.");
                        }
                    }

                    using (var process = Process.Start(startInfo))
                    {
                        if (process == null) throw new Exception("Failed to start process.");
                        process.WaitForExit();

                        if (process.ExitCode != 0)
                        {
                            var error = process.StandardError.ReadToEnd();
                            throw new Exception($"Process exited with code {process.ExitCode}. {error}");
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

            // Reset to idle after a few seconds
            _ = Task.Delay(5000).ContinueWith(_ =>
            {
                action.Status = QuickActionStatus.Idle;
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        private bool IsRunningAsAdmin()
        {
            using (var identity = System.Security.Principal.WindowsIdentity.GetCurrent())
            {
                var principal = new System.Security.Principal.WindowsPrincipal(identity);
                return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
        }

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
