using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using FluentTaskScheduler.Models;
using FluentTaskScheduler.Models.Enums;
using FluentTaskScheduler.Services;
using Microsoft.Extensions.DependencyInjection;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.UI.Dispatching;
using FluentTaskScheduler.ViewModels;
using System;
using Windows.ApplicationModel.DataTransfer;

namespace FluentTaskScheduler
{
    public sealed partial class MainPage : Page
    {
        public MainViewModel ViewModel { get; } = new();

        private ISettingsService Settings => App.Container.GetRequiredService<ISettingsService>();

        // Forwarding property for x:Bind compatibility
        public ObservableCollection<ScheduledTaskModel> FilteredTasks => ViewModel.FilteredTasks;

        private DispatcherQueueTimer _searchDebounceTimer;
        private List<TaskHistoryEntry> _fullHistory = new List<TaskHistoryEntry>(); 
        private string _historyStatusFilter = "Total";
        
        // Dialog State
        private ObservableCollection<TaskActionModel> _tempActions = new();
        private ObservableCollection<TaskTriggerModel> _tempTriggers = new();
        private bool _isPopulatingDetails = false;

        /// <summary>Pipeline being edited in the currently open task dialog.</summary>
        private TaskPipeline _tempPipeline = new();
        
        // Current folder path for new task creation
        private string _currentFolderPath = "\\";
        private Dictionary<string, bool> _folderExpandedState = new();

        public static MainPage? Current { get; private set; }

        public MainPage()
        {
            Current = this;
            this.InitializeComponent();
            this.Loaded += MainPage_Loaded;
            this.Unloaded += MainPage_Unloaded;
            LocalizationService.LanguageChanged += LocalizationService_LanguageChanged;
            
            _searchDebounceTimer = DispatcherQueue.CreateTimer();
            _searchDebounceTimer.Interval = TimeSpan.FromMilliseconds(300);
            _searchDebounceTimer.Tick += (s, e) =>
            {
                _searchDebounceTimer.Stop();
                ViewModel.SearchText = SearchBox.Text;
            };
            
            NavView.SelectedItem = NavAllTasks;
            ApplyLocalizedUi();

            SnoozeService.SnoozeChanged += SnoozeService_SnoozeChanged;
            TrayIconService.CustomSnoozeRequested += TrayIconService_CustomSnoozeRequested;
            UpdateSnoozeBanner();
        }

        private void MainPage_Unloaded(object sender, RoutedEventArgs e)
        {
            LocalizationService.LanguageChanged -= LocalizationService_LanguageChanged;
            SnoozeService.SnoozeChanged -= SnoozeService_SnoozeChanged;
            TrayIconService.CustomSnoozeRequested -= TrayIconService_CustomSnoozeRequested;
            ViewModel.Cleanup();
            if (ReferenceEquals(Current, this))
            {
                Current = null;
            }
        }

        private bool _isDialogOpen = false;

        private async Task ShowErrorDialog(string message)
        {
            if (_isDialogOpen) return;
            _isDialogOpen = true;
            try
            {
                await Helpers.DialogHelper.ShowErrorAsync(this.XamlRoot, message);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to show error dialog: {ex.Message}");
            }
            finally { _isDialogOpen = false; }
        }
    }
}
