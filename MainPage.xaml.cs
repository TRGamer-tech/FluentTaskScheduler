using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using FluentTaskScheduler.Models;
using FluentTaskScheduler.Services;
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

        // Forwarding property for x:Bind compatibility
        public ObservableCollection<ScheduledTaskModel> FilteredTasks => ViewModel.FilteredTasks;

        private DispatcherQueueTimer _searchDebounceTimer;
        private List<TaskHistoryEntry> _fullHistory = new List<TaskHistoryEntry>(); 
        private string _historyStatusFilter = "Total";
        
        // Dialog State
        private ObservableCollection<TaskActionModel> _tempActions = new();
        private ObservableCollection<TaskTriggerModel> _tempTriggers = new();
        private bool _isEditMode = false;
        private bool _isPopulatingDetails = false;
        private bool _isFromTemplate = false;
        
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
            
            NavView.SelectedItem = NavView.FooterMenuItems[0];  // Select "All Tasks" 
            ApplyLocalizedUi();
        }

        private void MainPage_Unloaded(object sender, RoutedEventArgs e)
        {
            LocalizationService.LanguageChanged -= LocalizationService_LanguageChanged;
            if (ReferenceEquals(Current, this))
            {
                Current = null;
            }
        }

        private void LocalizationService_LanguageChanged(object? sender, EventArgs e)
        {
            if (DispatcherQueue == null) return;
            DispatcherQueue.TryEnqueue(ApplyLocalizedUi);
        }

        private static string L(string key, string fallback) => LocalizationService.GetString(key, fallback);

        public void RefreshLocalizedUi() => ApplyLocalizedUi();

        private void ApplyLocalizedUi()
        {
            NavDashboard.Content = L("Main.Nav.Dashboard", "Dashboard");
            NavScriptLibrary.Content = L("Main.Nav.ScriptLibrary", "Script Library");
            NavAdd.Content = L("Main.Nav.NewTask", "New Task");
            NavAllTasks.Content = L("Main.Nav.AllTasks", "All Tasks");
            NavRunning.Content = L("Main.Nav.Running", "Running");
            NavEnabled.Content = L("Main.Nav.Enabled", "Enabled");
            NavDisabled.Content = L("Main.Nav.Disabled", "Disabled");
            NavSettings.Content = L("Main.Nav.Settings", "Settings");

            RefreshButton.Content = L("Main.Toolbar.Refresh", "Refresh");
            ImportTaskButton.Content = L("Main.Toolbar.ImportTask", "Import Task");
            ShortcutsButton.Content = L("Main.Toolbar.ShortcutsButton", "?");
            ToolTipService.SetToolTip(ShortcutsButton, L("Main.Toolbar.ShortcutsTooltip", "Keyboard Shortcuts (F1)"));
            UpdateSortButtonText();

            CopyHistoryBtn.Content = L("Main.History.Copy", "📋 Copy");
            TaskHistoryDialog.Title = L("Main.HistoryDialog.Title", "Task History");
            TaskHistoryDialog.CloseButtonText = L("Dialog.Common.Close", "Close");

            ShortcutsDialog.Title = L("Main.ShortcutsDialog.Title", "Keyboard Shortcuts");
            ShortcutsDialog.CloseButtonText = L("Dialog.Common.Close", "Close");

            TaskDetailsDialog.CloseButtonText = L("Dialog.Common.Close", "Close");
            RunTaskButton.Content = L("Main.Task.RunNow", "Run Now");
            StopTaskButton.Content = L("Main.Task.Stop", "Stop");
            EditTaskButton.Content = L("Main.Task.Edit", "Edit");
            ExportTaskButton.Content = L("Main.Task.Export", "Export");
            DeleteTaskButton.Content = L("Main.Task.Delete", "Delete");

            TaskEditDialog.PrimaryButtonText = L("Dialog.Common.Save", "Save");
            TaskEditDialog.CloseButtonText = L("Dialog.Common.Cancel", "Cancel");

            AdminDragWarning.Title = L("Main.AdminDragWarning.Title", "Drag & Drop Restricted");
            AdminDragWarning.Message = L("Main.AdminDragWarning.Message", "Windows does not support drag-and-drop operations when the app is running as Administrator.");

            // --- Edit/Add Dialog ---
            DlgTaskNameLabel.Text = L("Dialog.TaskName", "Task Name");
            DlgDescLabel.Text = L("Dialog.Description", "Description");
            DlgAuthorLabel.Text = L("Dialog.Author", "Author");
            DlgCategoryLabel.Text = L("Dialog.Category", "Category");
            DlgTagsLabel.Text = L("Dialog.Tags", "Tags");
            DlgEnabledLabel.Text = L("Dialog.Enabled", "Enabled");

            // Trigger types
            DlgTriggerTypeLabel.Text = L("Dialog.TriggerType", "Trigger Type");
            DlgTriggerDaily.Content = L("Dialog.Trigger.Daily", "Daily");
            DlgTriggerWeekly.Content = L("Dialog.Trigger.Weekly", "Weekly");
            DlgTriggerMonthly.Content = L("Dialog.Trigger.Monthly", "Monthly");
            DlgTriggerLogon.Content = L("Dialog.Trigger.AtLogon", "At Logon");
            DlgTriggerStartup.Content = L("Dialog.Trigger.AtStartup", "At Startup");
            DlgTriggerOnce.Content = L("Dialog.Trigger.OneTime", "One Time");
            DlgTriggerEvent.Content = L("Dialog.Trigger.OnEvent", "On an event");
            DlgTriggerSession.Content = L("Dialog.Trigger.Session", "On Workstation Lock/Unlock");

            EditTaskRandomDelay.Content = L("Dialog.RandomDelay", "Delay task for up to (random delay):");
            EditTaskStopAfter.Content = L("Dialog.StopAfter", "Stop task if runs longer than:");

            // Actions
            DlgProgramLabel.Text = L("Dialog.ProgramScript", "Program / Script");
            DlgArgsLabel.Text = L("Dialog.Arguments", "Arguments (optional)");
            DlgWorkDirLabel.Text = L("Dialog.WorkingDir", "Run in (optional)");
            DlgPsTip.Text = L("Dialog.PsTip", "Tip: For PowerShell scripts, use 'powershell.exe' as Program and '-ExecutionPolicy Bypass -File \"C:\\path\\to\\script.ps1\"' as Arguments");

            // Settings section
            EditTaskRunWithHighestPrivileges.Content = L("Dialog.HighPriv", "Run with highest privileges");
            EditTaskIsHidden.Content = L("Dialog.Hidden", "Hidden task");
            EditTaskDeleteExpired.Content = L("Dialog.DeleteExpired", "Delete the task if it is not scheduled to run again");
            EditTaskAllowHardTerminate.Content = L("Dialog.HardTerminate", "Allow task to be forcefully terminated");

            DlgMultiInstanceLabel.Text = L("Dialog.MultiInstance", "If the task is already running:");
            DlgMultiIgnore.Content = L("Dialog.Multi.Ignore", "Do not start a new instance");
            DlgMultiParallel.Content = L("Dialog.Multi.Parallel", "Run a new instance in parallel");
            DlgMultiQueue.Content = L("Dialog.Multi.Queue", "Queue a new instance");
            DlgMultiStop.Content = L("Dialog.Multi.Stop", "Stop the existing instance");

            DlgPriorityLabel.Text = L("Dialog.Priority", "Task Priority:");

            DlgRestartUpTo.Text = L("Dialog.RestartUpTo", "Attempt to restart up to:");
            DlgRestartTimes.Text = L("Dialog.RestartTimes", "times");

            // User Context
            DlgUserContextTitle.Text = L("Dialog.UserContext", "User Context");
            RunAsCurrentUser.Content = L("Dialog.RunAsCurrent", "Run as current user");
            RunAsSpecificUser.Content = L("Dialog.RunAsSpecific", "Run as specific user");
            RunAsSystem.Content = L("Dialog.RunAsSystem", "Run as SYSTEM (requires admin)");

            // Placeholder texts
            EditTaskName.PlaceholderText = L("Dialog.Ph.TaskName", "Enter task name");
            EditTaskCategory.PlaceholderText = L("Dialog.Ph.Category", "e.g. Work");
            EditTaskTags.PlaceholderText = L("Dialog.Ph.Tags", "e.g. urgent, sync (comma separated)");
            EditTaskActionCommand.PlaceholderText = L("Dialog.Ph.Command", "e.g., notepad.exe or C:\\Scripts\\myscript.ps1");
            EditTaskArguments.PlaceholderText = L("Dialog.Ph.Args", "e.g., /c echo hello or -File script.ps1");
            EditTaskWorkingDirectory.PlaceholderText = L("Dialog.Ph.WorkDir", "e.g., C:\\Scripts");
            EditTaskRandomDelayVal.PlaceholderText = L("Dialog.Ph.Delay", "e.g. 1 hour");
            EditTaskIdleDuration.PlaceholderText = L("Dialog.Ph.Idle", "e.g., 10m");
            EditTaskIdleDurationSetting.PlaceholderText = L("Dialog.Ph.Idle", "e.g., 10m");
            MonthlyDaysInput.PlaceholderText = L("Dialog.Ph.MonthDays", "e.g. 1, 15, Last");
            EditTaskRestartInterval.PlaceholderText = L("Dialog.Ph.RestartInt", "e.g. 1 minute");
            EditTaskRunAsUser.PlaceholderText = L("Dialog.Ph.Username", "DOMAIN\\Username or username@domain.com");
            EditTaskRunAsUser.Header = L("Dialog.UsernameHeader", "Username");
            EditTaskEventLog.Header = L("Dialog.EventLog", "Log");
            EditTaskEventLog.PlaceholderText = L("Dialog.Ph.EventLog", "Application, System, Security, etc.");
            EditTaskEventSource.Header = L("Dialog.EventSource", "Source");
            EditTaskEventSource.PlaceholderText = L("Dialog.Ph.EventSource", "e.g., VSS, Outlook (Optional)");
            EditTaskEventId.Header = L("Dialog.EventId", "Event ID");
            EditTaskEventId.PlaceholderText = L("Dialog.Ph.EventId", "e.g., 1000 (Optional)");

            // Hint texts
            DlgDelayHint.Text = L("Dialog.Hint.Delay", "(e.g. 30s, 1m, 1h)");
            DlgIdleHint.Text = L("Dialog.Hint.Idle", "(e.g. 5m, 10m, 30m)");
            DlgRestartHint.Text = L("Dialog.Hint.Restart", "(e.g. 30s, 1m, 5m)");
            DlgMonthlyDaysHint.Text = L("Dialog.Hint.MonthDays", "(comma separated, use 'Last' for last day)");

            // Daily/Weekly/Monthly labels
            EditTaskDailyRecurrence.Content = L("Dialog.RecurEvery", "Recur every");
            DlgDaysSuffix.Text = L("Dialog.DaysSuffix", "day(s)");
            DlgWeeklyRecur.Text = L("Dialog.RecurEvery", "Recur every");
            DlgWeeksOn.Text = L("Dialog.WeeksOn", "weeks on:");
            DlgMonthsLabel.Text = L("Dialog.Months", "Months:");
            MonthlyRadioDays.Content = L("Dialog.Days", "Days");
            MonthlyRadioOn.Content = L("Dialog.On", "On");
            DlgIdleWait.Text = L("Dialog.IdleWait", "Wait for the computer to be idle for:");

            // Weekday checkboxes
            WeeklyMon.Content = L("Dialog.Day.Mon", "Mon");
            WeeklyTue.Content = L("Dialog.Day.Tue", "Tue");
            WeeklyWed.Content = L("Dialog.Day.Wed", "Wed");
            WeeklyThu.Content = L("Dialog.Day.Thu", "Thu");
            WeeklyFri.Content = L("Dialog.Day.Fri", "Fri");
            WeeklySat.Content = L("Dialog.Day.Sat", "Sat");
            WeeklySun.Content = L("Dialog.Day.Sun", "Sun");

            // Month abbreviations
            MonthJan.Content = L("Dialog.Month.Jan", "Jan");
            MonthFeb.Content = L("Dialog.Month.Feb", "Feb");
            MonthMar.Content = L("Dialog.Month.Mar", "Mar");
            MonthApr.Content = L("Dialog.Month.Apr", "Apr");
            MonthMay.Content = L("Dialog.Month.May", "May");
            MonthJun.Content = L("Dialog.Month.Jun", "Jun");
            MonthJul.Content = L("Dialog.Month.Jul", "Jul");
            MonthAug.Content = L("Dialog.Month.Aug", "Aug");
            MonthSep.Content = L("Dialog.Month.Sep", "Sep");
            MonthOct.Content = L("Dialog.Month.Oct", "Oct");
            MonthNov.Content = L("Dialog.Month.Nov", "Nov");
            MonthDec.Content = L("Dialog.Month.Dec", "Dec");

            // Monthly week ordinals
            DlgWeekFirst.Content = L("Dialog.Week.First", "First");
            DlgWeekSecond.Content = L("Dialog.Week.Second", "Second");
            DlgWeekThird.Content = L("Dialog.Week.Third", "Third");
            DlgWeekFourth.Content = L("Dialog.Week.Fourth", "Fourth");
            DlgWeekLast.Content = L("Dialog.Week.Last", "Last");

            // Monthly day names
            DlgDayMon.Content = L("Dialog.Weekday.Mon", "Monday");
            DlgDayTue.Content = L("Dialog.Weekday.Tue", "Tuesday");
            DlgDayWed.Content = L("Dialog.Weekday.Wed", "Wednesday");
            DlgDayThu.Content = L("Dialog.Weekday.Thu", "Thursday");
            DlgDayFri.Content = L("Dialog.Weekday.Fri", "Friday");
            DlgDaySat.Content = L("Dialog.Weekday.Sat", "Saturday");
            DlgDaySun.Content = L("Dialog.Weekday.Sun", "Sunday");

            // Session state items
            EditTaskSessionStateType.Header = L("Dialog.TriggerOn", "Trigger on");
            DlgSessLock.Content = L("Dialog.Sess.Lock", "Workstation Lock");
            DlgSessUnlock.Content = L("Dialog.Sess.Unlock", "Workstation Unlock");
            DlgSessRdpOn.Content = L("Dialog.Sess.RdpConnect", "Remote Desktop Connect");
            DlgSessRdpOff.Content = L("Dialog.Sess.RdpDisconnect", "Remote Desktop Disconnect");

            EditTaskExpires.Content = L("Dialog.Expire", "Expire task on:");

            // Action menu items
            DlgActionRunProg.Text = L("Dialog.Action.RunProg", "Run Program");
            DlgActionEmail.Text = L("Dialog.Action.Email", "Send Email");
            DlgActionNotif.Text = L("Dialog.Action.Notif", "Show Notification");
            BrowseActionButton.Content = L("Dialog.Browse", "Browse...");

            // Time duration items - Stop After
            DlgStop15m.Content = L("Dialog.Time.15m", "15 minutes");
            DlgStop30m.Content = L("Dialog.Time.30m", "30 minutes");
            DlgStop1h.Content = L("Dialog.Time.1h", "1 hour");
            DlgStop2h.Content = L("Dialog.Time.2h", "2 hours");
            DlgStop4h.Content = L("Dialog.Time.4h", "4 hours");
            DlgStop8h.Content = L("Dialog.Time.8h", "8 hours");
            DlgStop12h.Content = L("Dialog.Time.12h", "12 hours");
            DlgStop1d.Content = L("Dialog.Time.1d", "1 day");
            DlgStop2d.Content = L("Dialog.Time.2d", "2 days");
            DlgStop3d.Content = L("Dialog.Time.3d", "3 days");
            DlgStop5d.Content = L("Dialog.Time.5d", "5 days");

            // Repetition interval items
            DlgRep5m.Content = L("Dialog.Time.5m", "5 minutes");
            DlgRep10m.Content = L("Dialog.Time.10m", "10 minutes");
            DlgRep15m.Content = L("Dialog.Time.15m", "15 minutes");
            DlgRep30m.Content = L("Dialog.Time.30m", "30 minutes");
            DlgRep1h.Content = L("Dialog.Time.1h", "1 hour");
            DlgRep2h.Content = L("Dialog.Time.2h", "2 hours");
            DlgRep4h.Content = L("Dialog.Time.4h", "4 hours");
            DlgRep6h.Content = L("Dialog.Time.6h", "6 hours");
            DlgRep12h.Content = L("Dialog.Time.12h", "12 hours");

            // Repetition duration items
            DlgDur1h.Content = L("Dialog.Time.1h", "1 hour");
            DlgDur2h.Content = L("Dialog.Time.2h", "2 hours");
            DlgDur4h.Content = L("Dialog.Time.4h", "4 hours");
            DlgDur6h.Content = L("Dialog.Time.6h", "6 hours");
            DlgDur12h.Content = L("Dialog.Time.12h", "12 hours");
            DlgDur24h.Content = L("Dialog.Time.24h", "24 hours");
            DlgDur1d.Content = L("Dialog.Time.1d", "1 day");

            // Priority items
            DlgPri0.Content = L("Dialog.Pri.Realtime", "Realtime (0)");
            DlgPri1.Content = L("Dialog.Pri.High", "High (1)");
            DlgPri3.Content = L("Dialog.Pri.AboveNormal", "Above Normal (3)");
            DlgPri7.Content = L("Dialog.Pri.Normal", "Normal (7)");
            DlgPri9.Content = L("Dialog.Pri.BelowNormal", "Below Normal (9)");
            DlgPri10.Content = L("Dialog.Pri.Idle", "Idle (10)");

            // InfoBars
            NetworkAdminNotice.Title = L("Dialog.NetAdmin.Title", "Administrator required");
            NetworkAdminNotice.Message = L("Dialog.NetAdmin.Msg", "Specific network selection requires the app to run as administrator.");
            SystemUserWarning.Title = L("Dialog.SysWarn.Title", "Administrator Privileges Required");
            SystemUserWarning.Message = L("Dialog.SysWarn.Msg", "To create tasks that run as SYSTEM, this application must be running with administrator privileges. Right-click the app and select \"Run as administrator\".");

            if (NavView.SelectedItem is NavigationViewItem selectedItem && selectedItem.Tag != null)
            {
                string tag = selectedItem.Tag.ToString() ?? string.Empty;
                NavView.Header = tag switch
                {
                    "Dashboard" => L("Main.Header.Dashboard", "Dashboard"),
                    "ScriptLibrary" => L("Main.Header.ScriptLibrary", "Script Library"),
                    "settings" => L("Main.Header.Settings", "Settings"),
                    _ => L("Main.Header.ScheduledTasks", "Scheduled Tasks")
                };
            }
            else
            {
                NavView.Header = L("Main.Header.ScheduledTasks", "Scheduled Tasks");
            }
        }

        public void OpenCreateTaskFromTemplate(ViewModels.ScriptTemplateModel template) => OpenCreateTaskDialog(template);
        private void NewTaskButton_Click(object sender, RoutedEventArgs e) => OpenCreateTaskDialog(null);

        private async void OpenCreateTaskDialog(ViewModels.ScriptTemplateModel? template)
        {
            if (this.Content?.XamlRoot == null) return;
            _isEditMode = false;
            _isFromTemplate = template != null;
            
            EditTaskName.Text = template?.Name ?? "";
            EditTaskDescription.Text = template?.Description ?? "";
            EditTaskAuthor.Text = Environment.UserName;
            EditTaskCategory.Text = "";
            EditTaskTags.Text = "";
            EditTaskEnabled.IsOn = true;
            
            _tempActions = new ObservableCollection<TaskActionModel>();
            if (template != null)
            {
                _tempActions.Add(new TaskActionModel { Command = template.Command, Arguments = template.Arguments });
            }
            else
            {
                _tempActions.Add(new TaskActionModel { Command = "notepad.exe" });
            }

            _tempTriggers = new ObservableCollection<TaskTriggerModel> { new TaskTriggerModel { TriggerType = "Daily", ScheduleInfo = DateTime.Now.ToString("g"), DailyInterval = 1 } };
            
            ActionList.ItemsSource = _tempActions;
            TriggerList.ItemsSource = _tempTriggers;
            ActionList.SelectedIndex = 0;
            TriggerList.SelectedIndex = 0;
            
            // Settings defaults
            EditTaskRunWithHighestPrivileges.IsChecked = template?.RunAsAdmin ?? false;

            PopulateNetworkList();
            TaskEditDialog.XamlRoot = this.Content.XamlRoot;
            await TaskEditDialog.ShowAsync();
        }

        private void MainPage_Loaded(object sender, RoutedEventArgs e)
        {
            LoadFolderStructure();
            _ = ViewModel.LoadTasksAsync();
            TaskListView.Focus(FocusState.Programmatic);
            UpdateFolderTreeMaxHeight();

            // Feature 3: restore last-used folder
            /*
            string saved = Services.SettingsService.LastFolderPath;
            if (!string.IsNullOrEmpty(saved) && saved != "\\")
            {
                _currentFolderPath = saved;
                ViewModel.SetFilter(saved);
            }
            */

            // Defer one frame so the ListView control template is fully applied before we set its internal ScrollViewer
            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
            {
                ApplySmoothScrollingSelf(Services.SettingsService.SmoothScrolling);
                
                // Set custom title bar drag region
                App.m_window?.SetTitleBar(AppTitleBarDragArea);

                // Check for elevation and handle drag-and-drop limitations
                if (Helpers.ElevationHelper.IsElevated())
                {
                    AdminDragWarning.Visibility = Visibility.Collapsed; // We handle it via custom drag
                    TaskListView.CanDragItems = false;
                    TaskListView.AllowDrop = false;
                    FolderTreeView.AllowDrop = false;
                    
                    // Hook up custom drag events
                    this.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnCustomDragPointerPressed), true);
                    this.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(OnCustomDragPointerMoved), true);
                    this.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(OnCustomDragPointerReleased), true);
                    // Note: Folder item dragging is disabled via early-return in FolderItem_DragStarting
                }
            });

            // Show startup dialogs in order: onboarding first, then changelog
            _ = CheckStartupDialogsAsync();
        }

        private async System.Threading.Tasks.Task CheckStartupDialogsAsync()
        {
            // Await onboarding first â€” on a fresh install the user must finish the
            // walkthrough before the "What's New" popup is shown on top.
            await CheckAndShowOnboardingAsync();

            // Only reaches here once onboarding is fully dismissed.
            await CheckAndShowChangelogAsync();
        }

        private async System.Threading.Tasks.Task CheckAndShowOnboardingAsync()
        {
            if (Services.SettingsService.HasCompletedOnboarding) return;

            var tcs = new System.Threading.Tasks.TaskCompletionSource();
            DispatcherQueue.TryEnqueue(async () =>
            {
                try
                {
                    var dialog = new Dialogs.OnboardingDialog { XamlRoot = this.XamlRoot, RequestedTheme = Services.SettingsService.Theme };
                    await dialog.ShowAsync();
                }
                catch { /* XamlRoot not ready or dialog already open â€” skip silently */ }
                finally { tcs.TrySetResult(); }
            });
            await tcs.Task;
        }

        private async System.Threading.Tasks.Task CheckAndShowChangelogAsync()
        {
            try
            {
                var release = await Services.GitHubReleaseService.GetLatestReleaseAsync();
                if (release == null) return;

                string lastSeen = Services.SettingsService.LastSeenVersion;
                if (string.Equals(release.TagName, lastSeen, StringComparison.OrdinalIgnoreCase)) return;

                // New version â€” marshal back to UI thread via TCS
                var tcs = new System.Threading.Tasks.TaskCompletionSource();
                DispatcherQueue.TryEnqueue(async () =>
                {
                    try
                    {
                        var dialog = new Dialogs.WhatsNewDialog(release)
                        {
                            XamlRoot = this.XamlRoot,
                            RequestedTheme = Services.SettingsService.Theme
                        };
                        await dialog.ShowAsync();
                        // Only persist after the user has actually seen the dialog
                        Services.SettingsService.LastSeenVersion = release.TagName;
                    }
                    catch { /* dialog already open or XamlRoot not ready â€” skip silently */ }
                    finally { tcs.TrySetResult(); }
                });
                await tcs.Task;
            }
            catch { /* network unavailable or any other error â€” fail silently */ }
        }

        /// <summary>Directly applies smooth scrolling to all ScrollViewers owned by MainPage,
        /// including hidden dialog content and the ListView's internal ScrollViewer.
        /// Called both from Loaded and from the Settings toggle handler.</summary>
        public void ApplySmoothScrollingSelf(bool enable)
        {
            DetailsScrollViewer.IsScrollInertiaEnabled = enable;
            EditScrollViewer.IsScrollInertiaEnabled = enable;
            HistoryScrollViewer.IsScrollInertiaEnabled = enable;
            // TaskListView has an internal ScrollViewer in its control template
            foreach (var sv in FindDescendants<ScrollViewer>(TaskListView))
                sv.IsScrollInertiaEnabled = enable;
        }

        private static IEnumerable<T> FindDescendants<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null) yield break;
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T match) yield return match;
                foreach (var descendant in FindDescendants<T>(child))
                    yield return descendant;
            }
        }

        private static T? FindParent<T>(DependencyObject child) where T : DependencyObject
        {
            var parent = VisualTreeHelper.GetParent(child);
            if (parent == null) return null;
            if (parent is T match) return match;
            return FindParent<T>(parent);
        }

        private void Page_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateFolderTreeMaxHeight();


        private void UpdateFolderTreeMaxHeight()
        {
            if (NavView == null || FolderTreeView == null) return;
            // Estimated height of Footer Items (4 items + Settings) + Header ("New Task") + Margins
            // Footer: ~200px
            // Header (PaneCustomContent top part): 
            //   Dashboard (40) + ScriptLib (40) + NewTask (40) + Separator (10) + Margins (~20) = ~150px
            // "Folders" Label: ~30px
            // Buffer: ~50px 
            // Total deduction: ~430px
            double availableHeight = NavView.ActualHeight - 430; 
            if (availableHeight < 100) availableHeight = 100;
            FolderTreeView.MaxHeight = availableHeight;
        }

        // ========================================================================================================
        // Navigation & Loading
        // ========================================================================================================

        private void LoadFolderStructure()
        {
            try
            {
                var rootFolder = ViewModel.TaskService.GetFolderStructure();
                _treeNodeFolderMap.Clear();
                FolderTreeView.RootNodes.Clear();
                AddFolderToTree(rootFolder, null);
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex.ToString()); }
        }

        private Dictionary<TreeViewNode, TaskFolderModel> _treeNodeFolderMap = new();

        private void AddFolderToTree(TaskFolderModel folder, TreeViewNode? parentNode)
        {
            var displayName = folder.Name == "\\" ? "Task Scheduler Library" : folder.Name;
            var treeNode = new TreeViewNode
            {
                Content = displayName,  
                IsExpanded = _folderExpandedState.ContainsKey(folder.Path) ? _folderExpandedState[folder.Path] : (folder.Path == "\\")
            };

            // Store folder in our mapping dictionary
            _treeNodeFolderMap[treeNode] = folder;

            // Track expansion state changes
            treeNode.RegisterPropertyChangedCallback(TreeViewNode.IsExpandedProperty, (sender, dp) =>
            {
                if (sender is TreeViewNode node && _treeNodeFolderMap.TryGetValue(node, out var f))
                    _folderExpandedState[f.Path] = node.IsExpanded;
            });
            
            // Add to parent or root
            if (parentNode != null)
                parentNode.Children.Add(treeNode);
            else
                FolderTreeView.RootNodes.Add(treeNode);

            // Add subfolders
            foreach (var sub in folder.SubFolders)
                AddFolderToTree(sub, treeNode);
        }

        private void FolderTreeView_ItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
        {
            if (args.InvokedItem is TreeViewNode node && _treeNodeFolderMap.TryGetValue(node, out var folder))
            {
                _currentFolderPath = folder.Path;
                // Services.SettingsService.LastFolderPath = folder.Path; // Feature 3: persist
                ViewModel.SetFilter(folder.Path);
                
                // Restore Task View
                NavView.Header = L("Main.Header.ScheduledTasks", "Scheduled Tasks");
                TasksViewGrid.Visibility = Visibility.Visible;
                ContentFrame.Visibility = Visibility.Collapsed;
                
                NavView.SelectedItem = null; // Native indicator for Dashboard/ScriptLib disappears
                FolderTreeView.SelectedItem = node; 
            }
        }

        private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (args.IsSettingsSelected || (args.SelectedItem is NavigationViewItem settingsItem && settingsItem.Tag?.ToString() == "settings")) 
            {
                 NavView.Header = L("Main.Header.Settings", "Settings");
                 ContentFrame.Visibility = Visibility.Visible;
                 TasksViewGrid.Visibility = Visibility.Collapsed;
                 ContentFrame.Navigate(typeof(SettingsPage));
            }
            else if (args.SelectedItem is NavigationViewItem item && item.Tag != null)
            {
                var tag = item.Tag.ToString() ?? "";

                if (tag == "Dashboard")
                {
                    NavView.Header = L("Main.Header.Dashboard", "Dashboard");
                    TasksViewGrid.Visibility = Visibility.Collapsed;
                    ContentFrame.Visibility = Visibility.Visible;
                    ContentFrame.Navigate(typeof(DashboardPage));
                    FolderTreeView.SelectedItem = null;
                }
                else if (tag == "ScriptLibrary")
                {
                    NavView.Header = L("Main.Header.ScriptLibrary", "Script Library");
                    TasksViewGrid.Visibility = Visibility.Collapsed;
                    ContentFrame.Visibility = Visibility.Visible;
                    ContentFrame.Navigate(typeof(ScriptLibraryPage), this);
                    FolderTreeView.SelectedItem = null;
                }
                else
                {
                    // Standard Task Views (if any)
                    NavView.Header = L("Main.Header.ScheduledTasks", "Scheduled Tasks");
                    TasksViewGrid.Visibility = Visibility.Visible;
                    ContentFrame.Visibility = Visibility.Collapsed;
                    FolderTreeView.SelectedItem = null;

                    if (tag.StartsWith("\\"))
                        _currentFolderPath = tag;
                    
                    ViewModel.SetFilter(tag);
                }
            }
        }

        private void NavView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
        {
            if (args.InvokedItemContainer is NavigationViewItem item && item.Tag?.ToString() == "Add") 
            {
                NewTaskButton_Click(sender, new RoutedEventArgs());
            }
        }

        private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            _searchDebounceTimer.Stop();
            _searchDebounceTimer.Start();
        }
        
        private void RefreshButton_Click(object sender, RoutedEventArgs e) => _ = ViewModel.LoadTasksAsync();
        private void ImportTask_Click(object sender, RoutedEventArgs e) => ImportTask(); // Implement if needed, kept generic

        public async void NavigateToTask(string taskPath)
        {
            // Switch to Tasks View
            NavView.SelectedItem = null; // Clear selection to indicate custom state or select "All Tasks"
            NavView.Header = L("Main.Header.ScheduledTasks", "Scheduled Tasks");
            TasksViewGrid.Visibility = Visibility.Visible;
            ContentFrame.Visibility = Visibility.Collapsed;
            FolderTreeView.SelectedItem = null;

            // Set filter to show this task (or all tasks)
            _currentFolderPath = System.IO.Path.GetDirectoryName(taskPath) ?? "\\";
            ViewModel.SetFilter("all"); // Reset filter to show everything in the folder, or just "all" global
            
            // Wait for load if needed
            if (ViewModel.FilteredTasks.Count == 0 && !ViewModel.IsLoading)
            {
                await ViewModel.LoadTasksAsync();
            }

            // Find the task
            var task = ViewModel.FilteredTasks.FirstOrDefault(t => t.Path.Equals(taskPath, StringComparison.OrdinalIgnoreCase));
            
            // If not found in current view, try to load specific folder? 
            // For now, let's assume it's in the list if we load all. 
            // Actually SetFilter("all") loads everything? No, SetFilter("all") is global filter.
            
            if (task == null)
            {
                // Try reloading
                await ViewModel.LoadTasksAsync();
                task = ViewModel.FilteredTasks.FirstOrDefault(t => t.Path.Equals(taskPath, StringComparison.OrdinalIgnoreCase));
            }

            if (task != null)
            {
                ViewModel.SelectedTask = task;
                TaskListView.ScrollIntoView(task);
                await ShowTaskDetails();
            }
        }

        // ========================================================================================================
        // Task List & Selection
        // ========================================================================================================

        private async void TaskListView_ItemClick(object sender, ItemClickEventArgs e)
        {
            var ctrl = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control);
            var shift = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift);
            
            if (TaskListView.SelectedItems.Count > 1 || ctrl.HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down) || shift.HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down)) return;

            if (e.ClickedItem is ScheduledTaskModel task)
            {
               ViewModel.SelectedTask = task;
               await ShowTaskDetails();
            }
        }

        private void TaskListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            foreach (ScheduledTaskModel added in e.AddedItems) added.IsSelected = true;
            foreach (ScheduledTaskModel removed in e.RemovedItems) removed.IsSelected = false;

            int count = TaskListView.SelectedItems.Count;
            if (BatchActionBar != null)
            {
                BatchActionBar.Visibility = count > 1 ? Visibility.Visible : Visibility.Collapsed;
                if (BatchCountText != null) BatchCountText.Text = $"{count} selected";
                UpdateBatchActionsState();
            }
            if (count == 1) ViewModel.SelectedTask = (ScheduledTaskModel)TaskListView.SelectedItem;
        }

        private void TaskCheckBox_Click(object sender, RoutedEventArgs e)
        {
             if (sender is CheckBox cb && cb.DataContext is ScheduledTaskModel task)
             {
                 var shift = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift);
                 bool isShiftHeld = shift.HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

                 if (isShiftHeld && ViewModel.SelectedTask != null && ViewModel.SelectedTask != task)
                 {
                     var list = FilteredTasks;
                     int start = list.IndexOf(ViewModel.SelectedTask);
                     int end = list.IndexOf(task);

                     if (start > -1 && end > -1)
                     {
                         int min = Math.Min(start, end);
                         int max = Math.Max(start, end);
                         for (int i = min; i <= max; i++)
                         {
                             if (!TaskListView.SelectedItems.Contains(list[i])) TaskListView.SelectedItems.Add(list[i]);
                         }
                     }
                 }
                 else
                 {
                     if (cb.IsChecked == true) { TaskListView.SelectedItems.Add(task); ViewModel.SelectedTask = task; }
                     else TaskListView.SelectedItems.Remove(task);
                 }
             }
        }
        
        private void ToggleSwitch_PointerPressed(object sender, PointerRoutedEventArgs e) => e.Handled = true; // Prevent row click
        
        private async void ToggleSwitch_Toggled(object sender, RoutedEventArgs e)
        {
            if (ViewModel.IsLoading) return;
            if (sender is ToggleSwitch ts && ts.IsLoaded && ts.DataContext is ScheduledTaskModel task)
            {
                // Only act if the toggle was likely user-initiated (has focus).
                // Programmatic changes during virtualization/recycling will not have focus.
                if (ts.FocusState == FocusState.Unfocused) return;

                try
                {
                    if (task.IsEnabled != ts.IsOn) 
                        ViewModel.TaskService.SetTaskEnabled(task.Path, ts.IsOn);
                    task.IsEnabled = ts.IsOn;
                }
                catch (Exception ex) 
                { 
                    // Revert UI if failed
                    ts.Toggled -= ToggleSwitch_Toggled;
                    ts.IsOn = !ts.IsOn;
                    ts.Toggled += ToggleSwitch_Toggled;
                    await ShowErrorDialog(ex.Message);
                }
            }
        }

        // ========================================================================================================
        // Task Details & History
        // ========================================================================================================

        private async Task ShowTaskDetails()
        {
            var task = ViewModel.SelectedTask;
            if (task == null) return;

            DialogTaskName.Text = task.Name;
            DialogTaskDescription.Text = task.Description;
            DialogTaskAuthor.Text = task.Author;
            DialogTaskCategory.Text = task.Category;
            DialogTaskTagsItems.ItemsSource = task.Tags;
            DialogTaskTagsPanel.Visibility = task.Tags.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            DialogTaskCategory.Visibility = !string.IsNullOrEmpty(task.Category) ? Visibility.Visible : Visibility.Collapsed;
            
            // Load History
            _fullHistory = await Task.Run(() => ViewModel.TaskService.GetTaskHistory(task.Path));
            UpdateHistoryList();
            UpdateHistoryStats();
            
            TaskDetailsDialog.XamlRoot = this.Content.XamlRoot;
            await TaskDetailsDialog.ShowAsync();
        }

        private void CategoryBadge_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            string cat = "";
            if (sender is Border b && b.Child is TextBlock tb) cat = tb.Text;
            else if (sender is Grid g && g.Children.LastOrDefault() is TextBlock tbg) cat = tbg.Text;

            if (!string.IsNullOrEmpty(cat))
            {
                SearchBox.Text = cat;
                TaskDetailsDialog.Hide();
            }
        }

        private void TagBadge_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            if (sender is Border b && b.Child is TextBlock tb)
            {
                SearchBox.Text = tb.Text;
                TaskDetailsDialog.Hide();
            }
        }

        private void UpdateHistoryList()
        {
            if (InlineHistoryListView == null) return;
            if (_historyStatusFilter == "Total") InlineHistoryListView.ItemsSource = _fullHistory;
            else if (_historyStatusFilter == "Success") InlineHistoryListView.ItemsSource = _fullHistory.Where(h => h.Result == "Task Completed");
            else if (_historyStatusFilter == "Failed") InlineHistoryListView.ItemsSource = _fullHistory.Where(h => h.Result != "Task Completed" && h.Result != "Task Started" && h.Result != "Task Registered");

            // Date filtering (Combo)
            if (HistoryFilterCombo != null && HistoryFilterCombo.SelectedItem is ComboBoxItem item)
            {
                 // To implement if needed, currently reusing logic
            }
        }
        
        private void UpdateHistoryStats()
        {
            StatTotalRuns.Text = _fullHistory.Count.ToString();
            StatSuccess.Text = _fullHistory.Count(h => h.Result == "Task Completed").ToString();
            StatFailed.Text = _fullHistory.Count(h => h.Result != "Task Completed" && h.Result != "Task Started").ToString();
            StatLastResult.Text = _fullHistory.FirstOrDefault()?.Result ?? "-";
            HistoryStatsGrid.Visibility = Visibility.Visible;
        }

        private void HistoryFilter_Changed(object sender, SelectionChangedEventArgs e) => UpdateHistoryList(); // Placeholder for actual date logic
        private async void ExportHistoryCsv_Click(object sender, RoutedEventArgs e)
        {
            if (_fullHistory == null || _fullHistory.Count == 0) return;
            var picker = new Windows.Storage.Pickers.FileSavePicker();
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.m_window);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            picker.FileTypeChoices.Add("CSV File", new List<string>() { ".csv" });
            picker.SuggestedFileName = (ViewModel.SelectedTask?.Name ?? "history") + "_history";
            var file = await picker.PickSaveFileAsync();
            if (file != null)
            {
                try
                {
                    var sb = new System.Text.StringBuilder();
                    sb.AppendLine("Time,EventId,Result,User,ExitCode,Message");
                    foreach (var h in _fullHistory)
                    {
                        sb.AppendLine($"\"{h.Time}\",{h.EventId},\"{h.Result}\",\"{h.User}\",{h.ExitCode},\"{h.Message?.Replace("\"", "\"\"") ?? ""}\"");
                    }
                    System.IO.File.WriteAllText(file.Path, sb.ToString());
                }
                catch (Exception ex) { await ShowErrorDialog(ex.Message); }
            }
        }
        
        private async void CopyHistory_Click(object sender, RoutedEventArgs e)
        {
             var dp = new DataPackage();
             dp.SetText(string.Join("\n", _fullHistory.Select(h => $"{h.Time}\t{h.Result}\t{h.Message}")));
             Clipboard.SetContent(dp);
             CopyHistoryBtn.Content = L("Main.History.Copied", "✅ Copied!");
             await Task.Delay(2000);
             CopyHistoryBtn.Content = L("Main.History.Copy", "📋 Copy");
         }
        
        private void StatTotal_Tapped(object sender, TappedRoutedEventArgs e) { _historyStatusFilter = "Total"; UpdateHistoryList(); }
        private void StatSuccess_Tapped(object sender, TappedRoutedEventArgs e) { _historyStatusFilter = "Success"; UpdateHistoryList(); }
        private void StatFailed_Tapped(object sender, TappedRoutedEventArgs e) { _historyStatusFilter = "Failed"; UpdateHistoryList(); }
        
        private async void RefreshHistory_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel.SelectedTask == null) return;
            RefreshHistoryBtn.IsEnabled = false;
            try { await RefreshTaskHistoryAsync(ViewModel.SelectedTask); }
            finally { RefreshHistoryBtn.IsEnabled = true; }
        }

        private async System.Threading.Tasks.Task RefreshTaskHistoryAsync(ScheduledTaskModel task)
        {
            if (task == null) return;
            var history = await System.Threading.Tasks.Task.Run(() => ViewModel.TaskService.GetTaskHistory(task.Path));
            
            // Only update if the user is still looking at the same task
            if (ViewModel.SelectedTask != null && ViewModel.SelectedTask.Path == task.Path)
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    _fullHistory = history;
                    UpdateHistoryList();
                    UpdateHistoryStats();
                });
            }
        }

        private void HistoryList_KeyDown(object sender, KeyRoutedEventArgs e) { /* Copy logic */ }

        // ========================================================================================================
        // Task Operations (Single)
        // ========================================================================================================

        private void RunTask_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel.SelectedTask == null) return;
            try
            {
                ViewModel.TaskService.RunTask(ViewModel.SelectedTask.Path);
                ViewModel.SelectedTask.State = "Running";
                ViewModel.SelectedTask.IsRunning = true;
                _ = WatchTaskUntilFinished(ViewModel.SelectedTask);
                _ = RefreshTaskHistoryAsync(ViewModel.SelectedTask); // Refresh to show "Task Started"
            }
            catch (Exception ex) { _ = ShowErrorDialog(ex.Message); }
        }

        private void StopTask_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel.SelectedTask == null) return;
            try
            {
                ViewModel.TaskService.StopTask(ViewModel.SelectedTask.Path);
                ViewModel.SelectedTask.State = "Ready";
                ViewModel.SelectedTask.IsRunning = false;
                _ = RefreshTaskHistoryAsync(ViewModel.SelectedTask);
            }
            catch (Exception ex) { _ = ShowErrorDialog(ex.Message); }
        }

        /// <summary>
        /// Polls Task Scheduler every 2 s until the task leaves the Running state,
        /// then writes the real state back to the model on the UI thread.
        /// </summary>
        private async System.Threading.Tasks.Task WatchTaskUntilFinished(ScheduledTaskModel task)
        {
            const int pollIntervalMs = 2000;
            const int maxPolls = 300; // 10 minutes max
            for (int i = 0; i < maxPolls; i++)
            {
                await System.Threading.Tasks.Task.Delay(pollIntervalMs);
                try
                {
                    string? liveState = await System.Threading.Tasks.Task.Run(
                        () => ViewModel.TaskService.GetTaskDetails(task.Path)?.State);

                    if (liveState == null) break; // task was deleted

                    DispatcherQueue.TryEnqueue(() =>
                    {
                        task.State = liveState;
                        if (liveState != "Running")
                            task.IsRunning = false;  // hide the ring
                    });

                    if (liveState != "Running") break;
                }
                catch { break; }
            }
            // Safety net: ensure the ring is cleared even if we exit via maxPolls or exception
            DispatcherQueue.TryEnqueue(() => 
            {
                task.IsRunning = false;
                _ = RefreshTaskHistoryAsync(task); // Final refresh when finished
            });
        }

        private async void DeleteTask_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel.SelectedTask == null) return;
            
            // Hide the details dialog first to avoid "Only a single ContentDialog can be open" error
            try { TaskDetailsDialog.Hide(); } catch { }

            var dialog = new ContentDialog 
            { 
                Title = L("Dialog.ConfirmDelete.Title", "Confirm Delete"), 
                Content = string.Format(L("Dialog.DeleteTask.ContentFormat", "Are you sure you want to delete '{0}'?"), ViewModel.SelectedTask.Name), 
                PrimaryButtonText = L("Dialog.Common.Delete", "Delete"), 
                CloseButtonText = L("Dialog.Common.Cancel", "Cancel"), 
                DefaultButton = ContentDialogButton.Close, 
                XamlRoot = this.XamlRoot 
            };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                try 
                { 
                    ViewModel.TaskService.DeleteTask(ViewModel.SelectedTask.Path); 
                    _ = ViewModel.LoadTasksAsync();
                } 
                catch (Exception ex) { await ShowErrorDialog(ex.Message); }
            }
        }
        
        private async void ExportTask_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel.SelectedTask == null) return;
            string? filePath = null;

            if (Helpers.ElevationHelper.IsElevated())
            {
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.m_window);
                filePath = Helpers.Win32FilePicker.PickSaveFile(hwnd, "Export Task", "XML File (*.xml)|*.xml|All files (*.*)|*.*", "xml", ViewModel.SelectedTask.Name);
            }
            else
            {
                var picker = new Windows.Storage.Pickers.FileSavePicker();
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.m_window);
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
                picker.FileTypeChoices.Add("XML File", new List<string>() { ".xml" });
                picker.SuggestedFileName = ViewModel.SelectedTask.Name;
                var file = await picker.PickSaveFileAsync();
                if (file != null) filePath = file.Path;
            }

            if (!string.IsNullOrEmpty(filePath))
            {
                try { ViewModel.TaskService.ExportTask(ViewModel.SelectedTask.Path, filePath); } 
                catch (Exception ex) { await ShowErrorDialog(ex.Message); }
            }
        }
        
        private async void ImportTask()
        {
            string? filePath = null;

            if (Helpers.ElevationHelper.IsElevated())
            {
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.m_window);
                filePath = Helpers.Win32FilePicker.PickOpenFile(hwnd, "Import Task", "XML File (*.xml)|*.xml|All files (*.*)|*.*");
            }
            else
            {
                var picker = new Windows.Storage.Pickers.FileOpenPicker();
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.m_window);
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
                picker.FileTypeFilter.Add(".xml");
                var file = await picker.PickSingleFileAsync();
                if (file != null) filePath = file.Path;
            }

            if (!string.IsNullOrEmpty(filePath))
            {
                var folderList = _treeNodeFolderMap.Values.Select(f => f.Path).Distinct().OrderBy(p => p).ToList();
                if (folderList.Count == 0) folderList.Add("\\");

                var comboBox = new ComboBox
                {
                    ItemsSource = folderList,
                    SelectedItem = _currentFolderPath ?? "\\",
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Margin = new Thickness(0, 10, 0, 0)
                };

                var panel = new StackPanel();
                panel.Children.Add(new TextBlock { Text = L("Dialog.ImportTask.SelectFolder", "Select the folder to import this task into:") });
                panel.Children.Add(comboBox);

                var dialog = new ContentDialog
                {
                    Title = L("Dialog.ImportTask.Title", "Import Task"),
                    Content = panel,
                    PrimaryButtonText = L("Dialog.Common.Import", "Import"),
                    CloseButtonText = L("Dialog.Common.Cancel", "Cancel"),
                    DefaultButton = ContentDialogButton.Primary,
                    XamlRoot = this.XamlRoot
                };

                var result = await dialog.ShowAsync();
                if (result != ContentDialogResult.Primary) return;

                var folder = comboBox.SelectedItem?.ToString() ?? "\\";
                
                try
                {
                    string xml = System.IO.File.ReadAllText(filePath);
                    string taskName = System.IO.Path.GetFileNameWithoutExtension(filePath);
                    ViewModel.TaskService.RegisterTaskFromXml(folder, taskName, xml);
                    _ = ViewModel.LoadTasksAsync();
                }
                catch (Exception ex) { await ShowErrorDialog(ex.Message); }
            }
        }

        // ========================================================================================================
        // Task Editing (Dialog)
        // ========================================================================================================

        private async void EditTask_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel.SelectedTask == null) return;
            try { TaskDetailsDialog.Hide(); } catch { }

            _isEditMode = true;
            _isPopulatingDetails = true;
            _isFromTemplate = false;
            
            // Populate Dialog
            EditTaskName.Text = ViewModel.SelectedTask.Name;
            EditTaskDescription.Text = ViewModel.SelectedTask.Description;
            EditTaskAuthor.Text = ViewModel.SelectedTask.Author;
            EditTaskCategory.Text = ViewModel.SelectedTask.Category;
            EditTaskTags.Text = ViewModel.SelectedTask.Tags != null ? string.Join(", ", ViewModel.SelectedTask.Tags) : "";
            EditTaskEnabled.IsOn = ViewModel.SelectedTask.IsEnabled;
            
            // Triggers
            _tempTriggers = new ObservableCollection<TaskTriggerModel>(ViewModel.SelectedTask.TriggersList);
            TriggerList.ItemsSource = _tempTriggers;
            
            // Actions
            _tempActions = new ObservableCollection<TaskActionModel>(ViewModel.SelectedTask.Actions);
            ActionList.ItemsSource = _tempActions;
            
            // Settings - simplified map back?
            // This is hard to "Refactor Cleanly" without binding everything.
            // For now, retaining basic load logic manually.
            EditTaskOnlyIfIdle.IsChecked = ViewModel.SelectedTask.OnlyIfIdle;
            EditTaskIsHidden.IsChecked = ViewModel.SelectedTask.IsHidden;
            EditTaskRunWithHighestPrivileges.IsChecked = ViewModel.SelectedTask.RunWithHighestPrivileges;
            
            if (ViewModel.SelectedTask.RunAsSystem)
            {
                RunAsSystem.IsChecked = true;
            }
            else if (!string.IsNullOrEmpty(ViewModel.SelectedTask.RunAsUser))
            {
                RunAsSpecificUser.IsChecked = true;
                EditTaskRunAsUser.Text = ViewModel.SelectedTask.RunAsUser;
            }
            else
            {
                RunAsCurrentUser.IsChecked = true;
            }
            EditTaskRunIfMissed.IsChecked = ViewModel.SelectedTask.RunIfMissed;
            foreach (var item in EditTaskMultipleInstances.Items.Cast<Microsoft.UI.Xaml.Controls.ComboBoxItem>())
                if (item.Tag?.ToString() == ViewModel.SelectedTask.MultipleInstancesPolicy) { EditTaskMultipleInstances.SelectedItem = item; break; }
            foreach (var item in EditTaskPriority.Items.Cast<Microsoft.UI.Xaml.Controls.ComboBoxItem>())
                if (item.Tag?.ToString() == ViewModel.SelectedTask.TaskPriority.ToString()) { EditTaskPriority.SelectedItem = item; break; }
            EditTaskDeleteExpired.IsChecked = ViewModel.SelectedTask.DeleteExpiredTaskAfter;
            EditTaskAllowHardTerminate.IsChecked = ViewModel.SelectedTask.AllowHardTerminate;
            EditTaskRestartOnFailure.IsChecked = ViewModel.SelectedTask.RestartOnFailure;
            EditTaskRestartInterval.Text = ViewModel.SelectedTask.RestartInterval;
            if (EditTaskRestartCount != null) EditTaskRestartCount.Value = ViewModel.SelectedTask.RestartCount;
            // All settings mapped
            
            PopulateNetworkList();
            if (!string.IsNullOrEmpty(ViewModel.SelectedTask.NetworkId) &&
                Guid.TryParse(ViewModel.SelectedTask.NetworkId, out var taskNetworkGuid))
            {
                foreach (Microsoft.UI.Xaml.Controls.ComboBoxItem item in EditTaskNetworkSelection.Items)
                {
                    if (Guid.TryParse(item.Tag?.ToString(), out var itemGuid) && itemGuid == taskNetworkGuid)
                    {
                        EditTaskNetworkSelection.SelectedItem = item;
                        break;
                    }
                }
            }
            else
                EditTaskNetworkSelection.SelectedIndex = 0;
            
            _isPopulatingDetails = false;
            TaskEditDialog.XamlRoot = this.Content.XamlRoot;
            EditTaskErrorBar.IsOpen = false;
            await TaskEditDialog.ShowAsync();
        }



        private async void TaskEditDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            args.Cancel = true; // Handle async manually
            
            if (string.IsNullOrWhiteSpace(EditTaskName.Text)) return;

            var model = new ScheduledTaskModel
            {
                Name = EditTaskName.Text,
                Description = EditTaskDescription.Text,
                Author = EditTaskAuthor.Text,
                IsEnabled = EditTaskEnabled.IsOn,
                Category = EditTaskCategory.Text,
                Tags = new ObservableCollection<string>(EditTaskTags.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)),
                Actions = new ObservableCollection<TaskActionModel>(_tempActions),
                TriggersList = new ObservableCollection<TaskTriggerModel>(_tempTriggers),
                // Map Settings
                OnlyIfIdle = EditTaskOnlyIfIdle.IsChecked == true,
                OnlyIfAC = EditTaskOnlyIfAC.IsChecked == true,
                OnlyIfNetwork = EditTaskOnlyIfNetwork.IsChecked == true,
                NetworkId = (EditTaskNetworkSelection.SelectedItem as Microsoft.UI.Xaml.Controls.ComboBoxItem)?.Tag?.ToString() ?? "",
                NetworkName = (EditTaskNetworkSelection.SelectedItem as Microsoft.UI.Xaml.Controls.ComboBoxItem)?.Content?.ToString() ?? "",
                WakeToRun = EditTaskWakeToRun.IsChecked == true,
                IsHidden = EditTaskIsHidden.IsChecked == true,
                RunWithHighestPrivileges = EditTaskRunWithHighestPrivileges.IsChecked == true,
                RunAsSystem = RunAsSystem.IsChecked == true,
                RunAsUser = RunAsSpecificUser.IsChecked == true ? EditTaskRunAsUser.Text : "",
                RunIfMissed = EditTaskRunIfMissed.IsChecked == true,
                MultipleInstancesPolicy = (EditTaskMultipleInstances.SelectedItem as Microsoft.UI.Xaml.Controls.ComboBoxItem)?.Tag?.ToString() ?? "IgnoreNew",
                TaskPriority = int.TryParse((EditTaskPriority.SelectedItem as Microsoft.UI.Xaml.Controls.ComboBoxItem)?.Tag?.ToString(), out int p) ? p : 7,
                DeleteExpiredTaskAfter = EditTaskDeleteExpired.IsChecked == true,
                AllowHardTerminate = EditTaskAllowHardTerminate.IsChecked == true,
                RestartOnFailure = EditTaskRestartOnFailure.IsChecked == true,
                RestartInterval = EditTaskRestartInterval.Text ?? "",
                RestartCount = EditTaskRestartCount != null ? (int)double.Round(EditTaskRestartCount.Value) : 3
            };
            
            // Handle folder
            string folder = "\\";
            if (_isFromTemplate)
            {
                folder = "\\";
            }
            else if (!_isEditMode) // New Task
            {
                folder = _currentFolderPath;  // Use tracked folder path
            }
            else // Edit - keep original folder logic (extracted from Path)
            {
                if (ViewModel.SelectedTask != null)
                    folder = System.IO.Path.GetDirectoryName(ViewModel.SelectedTask.Path) ?? "\\";
            }

            try
            {
                ViewModel.TaskService.RegisterTask(folder ?? "\\", model);

                // Handle renaming: if name changed (case-sensitive check for the file system/TS behavior)
                // but Task Scheduler is case-insensitive, so we only delete if it's truly a different task
                if (_isEditMode && ViewModel.SelectedTask != null && 
                    !model.Name.Equals(ViewModel.SelectedTask.Name, StringComparison.OrdinalIgnoreCase))
                {
                    ViewModel.TaskService.DeleteTask(ViewModel.SelectedTask.Path);
                    LogService.Info($"Renamed task - deleted old task at '{ViewModel.SelectedTask.Path}'");
                }

                TaskEditDialog.Hide();
                await ViewModel.LoadTasksAsync();
            }
            catch (Exception ex) 
            { 
                EditTaskErrorBar.Message = "Failed to save task: " + ex.Message;
                EditTaskErrorBar.IsOpen = true;
            }
        }

        // ========================================================================================================
        // UI Logic (Dialogs)
        // ========================================================================================================

        private void TriggerList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (TriggerList.SelectedItem is TaskTriggerModel tr)
            {
                _isPopulatingDetails = true;
                // Map Trigger Model -> UI
                foreach(var item in EditTaskTriggerType.Items.Cast<ComboBoxItem>()) {
                    if (item.Tag?.ToString() == tr.TriggerType) EditTaskTriggerType.SelectedItem = item;
                }
                
                DateTime.TryParse(tr.ScheduleInfo, out var dt);
                EditTaskStartDate.Date = dt == DateTime.MinValue ? DateTime.Today : dt;
                EditTaskStartTime.Time = dt == DateTime.MinValue ? DateTime.Now.TimeOfDay : dt.TimeOfDay;
                
                // Session State mapping
                foreach(var item in EditTaskSessionStateType.Items.Cast<ComboBoxItem>()) {
                    if (item.Tag?.ToString() == tr.SessionStateChangeType) EditTaskSessionStateType.SelectedItem = item;
                }

                // Repetition mapping
                foreach (var item in EditTaskRepetitionInterval.Items.Cast<ComboBoxItem>())
                    if (item.Tag?.ToString() == (tr.RepetitionInterval ?? "")) { EditTaskRepetitionInterval.SelectedItem = item; break; }
                foreach (var item in EditTaskRepetitionDuration.Items.Cast<ComboBoxItem>())
                    if (item.Tag?.ToString() == (tr.RepetitionDuration ?? "")) { EditTaskRepetitionDuration.SelectedItem = item; break; }

                UpdateTriggerPanelVisibility();
                _isPopulatingDetails = false;
            }
        }

        private void EditTaskSessionStateType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isPopulatingDetails) return;
            if (TriggerList.SelectedItem is TaskTriggerModel tr && EditTaskSessionStateType.SelectedItem is ComboBoxItem item)
            {
                if (item.Tag != null) tr.SessionStateChangeType = item.Tag.ToString()!;
            }
        }
        
        private void EditTaskTriggerType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isPopulatingDetails) return;
            UpdateTriggerPanelVisibility();
            if (TriggerList.SelectedItem is TaskTriggerModel tr && EditTaskTriggerType.SelectedItem is ComboBoxItem item)
            {
                if (item.Tag != null) tr.TriggerType = item.Tag.ToString()!;
            }
        }

        private void UpdateTriggerPanelVisibility()
        {
            if (TriggerDetailsPanel == null || PanelDaily == null || PanelWeekly == null || 
                PanelMonthly == null || PanelEvent == null || PanelIdle == null || 
                PanelSessionState == null || PanelStartTime == null || EditTaskTriggerType == null) return;

             TriggerDetailsPanel.Visibility = Visibility.Visible;
             PanelDaily.Visibility = Visibility.Collapsed;
             PanelWeekly.Visibility = Visibility.Collapsed;
             PanelMonthly.Visibility = Visibility.Collapsed;
             PanelEvent.Visibility = Visibility.Collapsed;
             PanelIdle.Visibility = Visibility.Collapsed;
             PanelSessionState.Visibility = Visibility.Collapsed;
             PanelStartTime.Visibility = Visibility.Visible;

             if (EditTaskTriggerType.SelectedItem is ComboBoxItem item)
             {
                 string type = item.Tag?.ToString() ?? "";
                 switch (type)
                 {
                     case "Daily": PanelDaily.Visibility = Visibility.Visible; break;
                     case "Weekly": PanelWeekly.Visibility = Visibility.Visible; break;
                     case "Monthly": PanelMonthly.Visibility = Visibility.Visible; break;
                     case "Event": PanelEvent.Visibility = Visibility.Visible; PanelStartTime.Visibility = Visibility.Collapsed; break;
                     case "OnIdle": PanelIdle.Visibility = Visibility.Visible; PanelStartTime.Visibility = Visibility.Collapsed; break;
                     case "SessionStateChange": PanelSessionState.Visibility = Visibility.Visible; PanelStartTime.Visibility = Visibility.Collapsed; break;
                 }
             }
        }

        private void ActionList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ActionList.SelectedItem is TaskActionModel act)
            {
                ActionDetailsPanel.Visibility = Visibility.Visible;
                EditTaskActionCommand.Text = act.Command ?? "";
                EditTaskArguments.Text = act.Arguments ?? "";
                EditTaskWorkingDirectory.Text = act.WorkingDirectory ?? "";
            }
            else
            {
                ActionDetailsPanel.Visibility = Visibility.Collapsed;
            }
        }
        
        private void EditTaskActionCommand_TextChanged(object sender, TextChangedEventArgs e) { if (ActionList.SelectedItem is TaskActionModel m) m.Command = EditTaskActionCommand.Text; }
        private void EditTaskArguments_TextChanged(object sender, TextChangedEventArgs e) { if (ActionList.SelectedItem is TaskActionModel m) m.Arguments = EditTaskArguments.Text; }
        private void EditTaskWorkingDirectory_TextChanged(object sender, TextChangedEventArgs e) { if (ActionList.SelectedItem is TaskActionModel m) m.WorkingDirectory = EditTaskWorkingDirectory.Text; }

        private async void BrowseAction_Click(object sender, RoutedEventArgs e)
        {
            string? filePath = null;

            if (Helpers.ElevationHelper.IsElevated())
            {
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.m_window);
                filePath = Helpers.Win32FilePicker.PickOpenFile(hwnd, "Select File", "All files (*.*)|*.*");
            }
            else
            {
                var picker = new Windows.Storage.Pickers.FileOpenPicker();
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.m_window);
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
                picker.FileTypeFilter.Add("*");
                var file = await picker.PickSingleFileAsync();
                if (file != null) filePath = file.Path;
            }

            if (!string.IsNullOrEmpty(filePath)) EditTaskActionCommand.Text = filePath;
        }

        private void PopulateNetworkList()
        {
            bool isAdmin = new System.Security.Principal.WindowsPrincipal(
                System.Security.Principal.WindowsIdentity.GetCurrent())
                .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);

            if (!isAdmin)
            {
                // Non-admin: disable dropdown and show explanation notice
                EditTaskNetworkSelection.IsEnabled = false;
                EditTaskNetworkSelection.Items.Clear();
                EditTaskNetworkSelection.Items.Add(new ComboBoxItem { Content = L("Main.Network.Any", "Any network"), Tag = "" });
                EditTaskNetworkSelection.SelectedIndex = 0;
                NetworkAdminNotice.IsOpen = true;
                return;
            }

            // Admin: populate from registry (exact NLM profile GUIDs)
            NetworkAdminNotice.IsOpen = false;
            EditTaskNetworkSelection.IsEnabled = true;
            EditTaskNetworkSelection.Items.Clear();
            EditTaskNetworkSelection.Items.Add(new ComboBoxItem { Content = L("Main.Network.Any", "Any network"), Tag = "" });

            try
            {
                using var profilesKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\NetworkList\Profiles");
                if (profilesKey != null)
                {
                    foreach (var subKeyName in profilesKey.GetSubKeyNames())
                    {
                        using var profileKey = profilesKey.OpenSubKey(subKeyName);
                        var name = profileKey?.GetValue("ProfileName") as string;
                        if (!string.IsNullOrWhiteSpace(name))
                            EditTaskNetworkSelection.Items.Add(new ComboBoxItem { Content = name, Tag = subKeyName });
                    }
                }
            }
            catch (Exception ex) { LogService.Warn($"Could not populate network list: {ex.Message}"); }
        }

        // List Buttons
        private void BtnAddTrigger_Click(object sender, RoutedEventArgs e) { _tempTriggers.Add(new TaskTriggerModel { TriggerType="Daily", ScheduleInfo=DateTime.Now.ToString("g") }); TriggerList.SelectedIndex = _tempTriggers.Count - 1; }
        private void BtnRemoveTrigger_Click(object sender, RoutedEventArgs e) { if (TriggerList.SelectedItem is TaskTriggerModel t) _tempTriggers.Remove(t); }
        private void BtnMoveTriggerUp_Click(object sender, RoutedEventArgs e) 
        { 
            int idx = TriggerList.SelectedIndex;
            if (idx > 0) {
                var item = _tempTriggers[idx];
                _tempTriggers.RemoveAt(idx);
                _tempTriggers.Insert(idx - 1, item);
                TriggerList.SelectedIndex = idx - 1;
            }
        }
        private void BtnMoveTriggerDown_Click(object sender, RoutedEventArgs e) 
        { 
            int idx = TriggerList.SelectedIndex;
            if (idx >= 0 && idx < _tempTriggers.Count - 1) {
                var item = _tempTriggers[idx];
                _tempTriggers.RemoveAt(idx);
                _tempTriggers.Insert(idx + 1, item);
                TriggerList.SelectedIndex = idx + 1;
            }
        }

        private void BtnAddAction_Click(object sender, RoutedEventArgs e) { _tempActions.Add(new TaskActionModel { Command="notepad.exe" }); ActionList.SelectedIndex = _tempActions.Count - 1; }
        
        private void AddAction_SendEmail_Click(object sender, RoutedEventArgs e) 
        {
            string ps = "powershell.exe";
            string args = "-ExecutionPolicy Bypass -Command \"Send-MailMessage -To 'recipient@example.com' -From 'scheduler@example.com' -Subject 'Task Started' -Body 'The task has started.' -SmtpServer 'smtp.example.com'\"";
            _tempActions.Add(new TaskActionModel { Command = ps, Arguments = args });
            ActionList.SelectedIndex = _tempActions.Count - 1;
        }

        private void AddAction_ShowNotification_Click(object sender, RoutedEventArgs e)
        {
            string ps = "powershell.exe";
            string args = "-WindowStyle Hidden -Command \"& {Add-Type -AssemblyName System.Windows.Forms; [System.Windows.Forms.MessageBox]::Show('Task Notification', 'Fluent Launcher')}\"";
            _tempActions.Add(new TaskActionModel { Command = ps, Arguments = args });
            ActionList.SelectedIndex = _tempActions.Count - 1;
        }

        private void BtnRemoveAction_Click(object sender, RoutedEventArgs e) { if (ActionList.SelectedItem is TaskActionModel t) _tempActions.Remove(t); }
        private void BtnMoveActionUp_Click(object sender, RoutedEventArgs e) 
        { 
            int idx = ActionList.SelectedIndex;
            if (idx > 0) {
                var item = _tempActions[idx];
                _tempActions.RemoveAt(idx);
                _tempActions.Insert(idx - 1, item);
                ActionList.SelectedIndex = idx - 1;
            }
        }
        private void BtnMoveActionDown_Click(object sender, RoutedEventArgs e) 
        { 
            int idx = ActionList.SelectedIndex;
            if (idx >= 0 && idx < _tempActions.Count - 1) {
                var item = _tempActions[idx];
                _tempActions.RemoveAt(idx);
                _tempActions.Insert(idx + 1, item);
                ActionList.SelectedIndex = idx + 1;
            }
        }

        // Handlers to satisfy XAML connection
        private void EditTaskExpires_Click(object sender, RoutedEventArgs e) 
        { 
            bool enabled = EditTaskExpires.IsChecked == true;
            EditTaskExpirationDate.IsEnabled = enabled;
            EditTaskExpirationTime.IsEnabled = enabled;
        }
        private void EditTaskRandomDelay_Click(object sender, RoutedEventArgs e) { if (EditTaskRandomDelayVal != null) EditTaskRandomDelayVal.IsEnabled = EditTaskRandomDelay.IsChecked == true; }
        private void EditTaskRepetitionInterval_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isPopulatingDetails) return;
            if (TriggerList.SelectedItem is TaskTriggerModel tr && EditTaskRepetitionInterval.SelectedItem is ComboBoxItem item)
                tr.RepetitionInterval = item.Tag?.ToString() ?? "";
        }
        private void EditTaskRepetitionDuration_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isPopulatingDetails) return;
            if (TriggerList.SelectedItem is TaskTriggerModel tr && EditTaskRepetitionDuration.SelectedItem is ComboBoxItem item)
                tr.RepetitionDuration = item.Tag?.ToString() ?? "";
        }
        private void EditTaskStopAfter_Click(object sender, RoutedEventArgs e) { if (EditTaskStopAfterVal != null) EditTaskStopAfterVal.IsEnabled = EditTaskStopAfter.IsChecked == true; }
        private void EditTaskDailyRecurrence_Checked(object sender, RoutedEventArgs e) { if (DailyInterval != null) DailyInterval.IsEnabled = EditTaskDailyRecurrence.IsChecked == true; }
        private void UserContextRadio_Checked(object sender, RoutedEventArgs e) 
        { 
             if (EditTaskRunAsUser != null) EditTaskRunAsUser.IsEnabled = RunAsSpecificUser.IsChecked == true; 
             if (SystemUserWarning != null) 
             {
                 bool isElevated = false;
                 using (var identity = System.Security.Principal.WindowsIdentity.GetCurrent())
                 {
                     var principal = new System.Security.Principal.WindowsPrincipal(identity);
                     isElevated = principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
                 }
                 SystemUserWarning.IsOpen = (!isElevated) && (RunAsSystem.IsChecked == true);
             }
        }
        private void RunAsSystem_Click(object sender, RoutedEventArgs e) => RunAsSystem.IsChecked = true;
        private void DialogScrollViewer_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            // When the user clicks on empty space (no interactive element), WinUI shifts focus to
            // the ScrollViewer and calls BringIntoView on it, which resets the scroll position to
            // the top. We prevent this by capturing the current vertical offset and restoring it
            // on the next dispatcher frame (after BringIntoView has already fired).
            if (sender is not ScrollViewer sv) return;
            double savedOffset = sv.VerticalOffset;
            DispatcherQueue.TryEnqueue(() => sv.ChangeView(null, savedOffset, null, true));
        }

        private void InfoIcon_Tapped(object sender, TappedRoutedEventArgs e)
        {
            e.Handled = true; // Don't bubble to ScrollViewer
            if (sender is not FrameworkElement icon) return;

            var text = ToolTipService.GetToolTip(icon) as string;
            if (string.IsNullOrEmpty(text)) return;

            // Un-escape XML character references that appear literally in the string
            text = text.Replace("&#x0a;", "\n").Replace("&#x2022;", "\u2022");

            var content = new TextBlock
            {
                Text = text,
                MaxWidth = 300,
                TextWrapping = TextWrapping.Wrap,
                Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"]
            };

            var flyout = new Flyout
            {
                Content = content,
                Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.Bottom
            };

            // Save the current scroll offset now. When the flyout light-dismisses, WinUI
            // processes the outside tap as a focus change on the ScrollViewer and calls
            // BringIntoView, jumping the scroll to the top. Restoring the saved offset on
            // the next dispatcher frame (after BringIntoView has already fired) undoes that.
            double savedOffset = EditScrollViewer.VerticalOffset;
            flyout.Closed += (_, _) =>
                DispatcherQueue.TryEnqueue(() => EditScrollViewer.ChangeView(null, savedOffset, null, true));

            flyout.ShowAt(icon);
        }

        // Batch
        private void UpdateBatchActionsState()
        {
            if (TaskListView.SelectedItems.Count <= 1) return;
            var tasks = TaskListView.SelectedItems.Cast<ScheduledTaskModel>();
            bool anyDisabled = tasks.Any(t => !t.IsEnabled);
            if (BatchRunBtn != null) BatchRunBtn.IsEnabled = !anyDisabled;
            if (BatchStopBtn != null) BatchStopBtn.IsEnabled = !anyDisabled;
        }
        private void BatchCancel_Click(object sender, RoutedEventArgs e) => TaskListView.SelectedItems.Clear();
        private void BatchRun_Click(object sender, RoutedEventArgs e)
        {
            // Snapshot selection before anything changes.
            // Set IsRunning=true BEFORE calling RunTask so the ring appears immediately,
            // independently of the volatile State string.
            var tasks = TaskListView.SelectedItems.Cast<ScheduledTaskModel>().ToList();
            foreach (var t in tasks)
            {
                t.State = "Running";
                t.IsRunning = true;           // show the ring immediately
                try
                {
                    ViewModel.TaskService.RunTask(t.Path);
                }
                catch { /* RunTask failed â€“ watcher will correct IsRunning */ }
                _ = WatchTaskUntilFinished(t);
            }
        }
        private void BatchStop_Click(object sender, RoutedEventArgs e) => PerformBatchAction(t => { ViewModel.TaskService.StopTask(t.Path); t.State = "Ready"; });
        private async void BatchEnable_Click(object sender, RoutedEventArgs e) { var denied = PerformBatchActionWithErrors(t => { if (!t.IsEnabled) { ViewModel.TaskService.SetTaskEnabled(t.Path, true); t.IsEnabled = true; } }); UpdateBatchActionsState(); if (denied.Count > 0) await ShowErrorDialog($"The user account under which you are performing this action does not have permission to enable the following task(s):\n\n{string.Join("\n", denied)}\n\nThese tasks are protected and cannot be modified, even with administrator privileges."); }
        private async void BatchDisable_Click(object sender, RoutedEventArgs e) { var denied = PerformBatchActionWithErrors(t => { if (t.IsEnabled) { ViewModel.TaskService.SetTaskEnabled(t.Path, false); t.IsEnabled = false; } }); UpdateBatchActionsState(); if (denied.Count > 0) await ShowErrorDialog($"The user account under which you are performing this action does not have permission to disable the following task(s):\n\n{string.Join("\n", denied)}\n\nThese tasks are protected and cannot be modified, even with administrator privileges."); }
        private async void BatchDelete_Click(object sender, RoutedEventArgs e)
        {
            var tasks = TaskListView.SelectedItems.Cast<ScheduledTaskModel>().ToList();
            var dialog = new ContentDialog
            {
                Title = L("Dialog.ConfirmDelete.Title", "Confirm Delete"),
                Content = string.Format(L("Dialog.BatchDelete.ContentFormat", "Delete {0} tasks?"), tasks.Count),
                PrimaryButtonText = L("Dialog.Common.Delete", "Delete"),
                CloseButtonText = L("Dialog.Common.Cancel", "Cancel"),
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.XamlRoot
            };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                foreach (var t in tasks) try { ViewModel.TaskService.DeleteTask(t.Path); } catch { }
                _ = ViewModel.LoadTasksAsync();
            }
        }
        private void PerformBatchAction(System.Action<ScheduledTaskModel> action) { foreach (var task in TaskListView.SelectedItems.Cast<ScheduledTaskModel>().ToList()) try { action(task); } catch { } }
        private List<string> PerformBatchActionWithErrors(System.Action<ScheduledTaskModel> action) { var denied = new List<string>(); foreach (var task in TaskListView.SelectedItems.Cast<ScheduledTaskModel>().ToList()) try { action(task); } catch (UnauthorizedAccessException) { denied.Add(task.Name); } catch { } return denied; }

        // Keyboard Accelerators
        protected override void OnKeyDown(Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.F5) { e.Handled = true; _ = ViewModel.LoadTasksAsync(); return; }
            base.OnKeyDown(e);
        }
        private void NewTaskAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args) { args.Handled = true; NewTaskButton_Click(sender, new RoutedEventArgs()); }
        private void EditTaskAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args) { args.Handled = true; if (ViewModel.SelectedTask != null) EditTask_Click(sender, new RoutedEventArgs()); }
        private void RunTaskAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args) { args.Handled = true; if (ViewModel.SelectedTask != null) RunTask_Click(sender, new RoutedEventArgs()); }
        private void DeleteTaskAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args) { args.Handled = true; if (FocusManager.GetFocusedElement() is not TextBox) DeleteTask_Click(sender, new RoutedEventArgs()); }
        private void EscapeAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args) { args.Handled = true; try { TaskDetailsDialog.Hide(); } catch { } try { TaskEditDialog.Hide(); } catch { } }
        private void ShortcutsAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args) { args.Handled = true; ShowShortcutsDialog(); }

        // Feature 1: Keyboard shortcuts dialog
        private void ShortcutsButton_Click(object sender, RoutedEventArgs e) => ShowShortcutsDialog();

        private async void ShowShortcutsDialog()
        {
            ShortcutsDialog.XamlRoot = this.XamlRoot;
            try { await ShortcutsDialog.ShowAsync(); } catch { }
        }

        // Feature 2: Sort button with flyout
        private void SortButton_Click(object sender, RoutedEventArgs e)
        {
            var flyout = new MenuFlyout();
            string arrow(string col) =>
                ViewModel.SortColumn == col ? (ViewModel.SortAscending ? " ▲" : " ▼") : "";

            void AddItem(string label, string col)
            {
                var item = new MenuFlyoutItem { Text = label + arrow(col) };
                item.Click += (s, _) => { ViewModel.SortBy(col); UpdateSortButtonText(); };
                flyout.Items.Add(item);
            }

            AddItem(L("Main.Sort.Name", "Name"), "Name");
            AddItem(L("Main.Sort.Status", "Status"), "Status");
            AddItem(L("Main.Sort.NextRun", "Next Run"), "NextRun");
            AddItem(L("Main.Sort.LastRun", "Last Run"), "LastRun");
            flyout.Items.Add(new MenuFlyoutSeparator());
            var clear = new MenuFlyoutItem { Text = L("Main.Sort.Clear", "Clear Sort") };
            clear.Click += (s, _) => { ViewModel.ClearSort(); UpdateSortButtonText(); };
            flyout.Items.Add(clear);

            flyout.ShowAt(SortButton);
        }

        private void UpdateSortButtonText()
        {
            string arrow = ViewModel.SortAscending ? "▲" : "▼";
            SortButton.Content = string.IsNullOrEmpty(ViewModel.SortColumn)
                ? L("Main.Toolbar.SortButton", "Sort ↕")
                : string.Format(L("Main.Toolbar.SortActiveFormat", "Sort {0} {1}"), arrow, ViewModel.SortColumn);
        }

        private async void ReloadFolders_Click(object sender, RoutedEventArgs e)
        {
            FolderRefreshIcon.Visibility = Visibility.Collapsed;
            FolderRefreshRing.Visibility = Visibility.Visible;
            FolderRefreshRing.IsActive = true;

            await Task.Run(() => 
            {
                DispatcherQueue.TryEnqueue(() => LoadFolderStructure());
            });

            await Task.Delay(300); // Give a little visual feedback

            FolderRefreshRing.IsActive = false;
            FolderRefreshRing.Visibility = Visibility.Collapsed;
            FolderRefreshIcon.Visibility = Visibility.Visible;
        }

        private void CreateRootFolder_Click(object sender, RoutedEventArgs e)
        {
            CreateFolder_Click("\\");
        }

        private void FolderTreeViewItem_RightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            var fe = e.OriginalSource as FrameworkElement;
            if (fe == null) return;
            
            var tvi = FindParent<TreeViewItem>(fe);
            if (tvi != null)
            {
                var node = FolderTreeView.NodeFromContainer(tvi);
                if (node != null && _treeNodeFolderMap.TryGetValue(node, out var folder))
                {
                    ShowFolderContextMenu(fe, e.GetPosition(fe), folder);
                }
            }
        }

        private void ShowFolderContextMenu(FrameworkElement targetElement, Windows.Foundation.Point position, TaskFolderModel folder)
        {
            var flyout = new MenuFlyout();

            var newFolderItem = new MenuFlyoutItem { Text = "New Subfolder", Icon = new SymbolIcon(Symbol.Add) };
            newFolderItem.Click += (s, args) => CreateFolder_Click(folder.Path);
            flyout.Items.Add(newFolderItem);

            if (folder.Path != "\\")
            {
                var renameItem = new MenuFlyoutItem { Text = "Rename", Icon = new SymbolIcon(Symbol.Rename) };
                renameItem.Click += (s, args) => RenameFolder_Click(folder.Path, folder.Name);
                flyout.Items.Add(renameItem);

                var deleteItem = new MenuFlyoutItem { Text = "Delete", Icon = new SymbolIcon(Symbol.Delete) };
                deleteItem.Click += (s, args) => DeleteFolder_Click(folder.Path);
                flyout.Items.Add(deleteItem);
            }

            flyout.ShowAt(targetElement, new Microsoft.UI.Xaml.Controls.Primitives.FlyoutShowOptions { Position = position });
        }

        private async void CreateFolder_Click(string parentPath)
        {
            var dialog = new ContentDialog
            {
                Title = L("Dialog.NewFolder.Title", "New Folder"),
                Content = new TextBox { PlaceholderText = L("Dialog.NewFolder.NamePlaceholder", "Name") },
                PrimaryButtonText = L("Dialog.Common.Create", "Create"),
                CloseButtonText = L("Dialog.Common.Cancel", "Cancel"),
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.XamlRoot,
                RequestedTheme = Services.SettingsService.Theme
            };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary && dialog.Content is TextBox tb && !string.IsNullOrWhiteSpace(tb.Text)) 
            { 
                try 
                { 
                    ViewModel.TaskService.CreateFolder(parentPath == "\\" ? "\\" + tb.Text : parentPath + "\\" + tb.Text); 
                    LoadFolderStructure(); 
                } 
                catch (Exception ex) 
                { 
                    await ShowErrorDialog(ex.Message); 
                } 
            }
        }

        private async void RenameFolder_Click(string path, string oldName)
        {
            var tb = new TextBox { Text = oldName, PlaceholderText = L("Dialog.RenameFolder.NewNamePlaceholder", "New Name") };
            tb.SelectAll();
            
            var dialog = new ContentDialog
            {
                Title = L("Dialog.RenameFolder.Title", "Rename Folder"),
                Content = tb,
                PrimaryButtonText = L("Dialog.Common.Rename", "Rename"),
                CloseButtonText = L("Dialog.Common.Cancel", "Cancel"),
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.XamlRoot,
                RequestedTheme = Services.SettingsService.Theme
            };
            
            if (await dialog.ShowAsync() == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(tb.Text) && tb.Text != oldName) 
            { 
                try 
                { 
                    ViewModel.TaskService.RenameFolder(path, tb.Text); 
                    LoadFolderStructure(); 
                    
                    if (_currentFolderPath.StartsWith(path, StringComparison.OrdinalIgnoreCase))
                    {
                        _currentFolderPath = "\\";
                        ViewModel.SetFilter("all");
                        NavView.SelectedItem = NavView.FooterMenuItems[0];
                    }
                } 
                catch (Exception ex) 
                { 
                    await ShowErrorDialog(ex.Message); 
                } 
            }
        }

        private async void DeleteFolder_Click(string path)
        {
            var dialog = new ContentDialog
            {
                Title = L("Dialog.DeleteFolder.Title", "Delete Folder"),
                Content = string.Format(L("Dialog.DeleteFolder.ContentFormat", "Delete '{0}' and ALL tasks in it?"), path),
                PrimaryButtonText = L("Dialog.Common.Delete", "Delete"),
                CloseButtonText = L("Dialog.Common.Cancel", "Cancel"),
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.XamlRoot,
                RequestedTheme = Services.SettingsService.Theme
            };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary) 
            { 
                try 
                { 
                    ViewModel.TaskService.DeleteFolder(path); 
                    LoadFolderStructure(); 
                    ViewModel.SetFilter("all"); 
                    NavView.SelectedItem = NavView.FooterMenuItems[0]; 
                } 
                catch (Exception ex) 
                { 
                    await ShowErrorDialog(ex.Message); 
                } 
            }
        }


        private bool _isDialogOpen = false;

        // ========================================================================================================
        // Drag and Drop
        // ========================================================================================================

        private const string DragTaskPrefix  = "FTS_TASKS:";
        private const string DragFolderPrefix = "FTS_FOLDER:";
        private Grid? _dragHighlightedGrid;

        // --- Task dragging from TaskListView ---

        private void TaskListView_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
        {
            try
            {
                if (Helpers.ElevationHelper.IsElevated())
                {
                    e.Cancel = true;
                    AdminDragWarning.Visibility = Visibility.Visible;
                    return;
                }

                var paths = e.Items
                    .OfType<ScheduledTaskModel>()
                    .Where(t => !t.IsReadOnlyFallback)
                    .Select(t => t.Path)
                    .ToList();

                if (paths.Count == 0) { e.Cancel = true; return; }

                e.Data.RequestedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Move;
                e.Data.SetText(DragTaskPrefix + string.Join("\n", paths));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Task DragItemsStarting failed: {ex.Message}");
                e.Cancel = true;
            }
        }

        // --- Per-folder-item DataTemplate Grid events ---

        private TaskFolderModel? FindFolderFromItemGrid(DependencyObject element)
        {
            var tvi = FindParent<TreeViewItem>(element);
            if (tvi == null) return null;
            var node = FolderTreeView.NodeFromContainer(tvi);
            if (node == null) return null;
            return _treeNodeFolderMap.TryGetValue(node, out var f) ? f : null;
        }

        private void SetDragHighlight(Grid? grid, bool on)
        {
            if (grid == null) return;
            grid.Background = on
                ? new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(50, 0, 103, 192))
                : new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
        }

        private void FolderItem_DragStarting(UIElement sender, DragStartingEventArgs e)
        {
            try
            {
                if (Helpers.ElevationHelper.IsElevated())
                {
                    e.Cancel = true;
                    AdminDragWarning.Visibility = Visibility.Visible;
                    return;
                }

                if (sender is not FrameworkElement fe) return;
                var folder = FindFolderFromItemGrid(fe);
                if (folder == null || folder.Path == "\\") { e.Cancel = true; return; }
                e.Data.RequestedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Move;
                e.Data.SetText(DragFolderPrefix + folder.Path);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Folder DragStarting failed: {ex.Message}");
                e.Cancel = true;
            }
        }

        private void FolderItem_DragOver(object sender, DragEventArgs e)
        {
            if (sender is not Grid grid) return;
            var folder = FindFolderFromItemGrid(grid);
            if (folder == null) { e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.None; return; }

            e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Move;
            e.DragUIOverride.Caption = $"Move to \"{folder.Name}\"";
            e.DragUIOverride.IsGlyphVisible = true;

            if (_dragHighlightedGrid != grid)
            {
                SetDragHighlight(_dragHighlightedGrid, false);
                _dragHighlightedGrid = grid;
                SetDragHighlight(grid, true);
            }
        }

        private void FolderItem_DragLeave(object sender, DragEventArgs e)
        {
            if (sender is Grid grid && grid == _dragHighlightedGrid)
            {
                SetDragHighlight(grid, false);
                _dragHighlightedGrid = null;
            }
        }

        private async void FolderItem_Drop(object sender, DragEventArgs e)
        {
            if (sender is not Grid grid) return;
            var folder = FindFolderFromItemGrid(grid);
            SetDragHighlight(grid, false);
            _dragHighlightedGrid = null;
            if (folder == null) return;
            if (!e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.Text)) return;

            string payload;
            try { payload = await e.DataView.GetTextAsync(); }
            catch { return; }

            if (payload.StartsWith(DragTaskPrefix))
                await MoveDraggedTasksAsync(payload.Substring(DragTaskPrefix.Length), folder.Path);
            else if (payload.StartsWith(DragFolderPrefix))
                await MoveDraggedFolderAsync(payload.Substring(DragFolderPrefix.Length), folder.Path);
        }

        // --- TreeView-level fallback handlers ---

        private void FolderTreeView_DragOver(object sender, DragEventArgs e)
        {
            e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Move;
        }

        private void FolderTreeView_DragLeave(object sender, DragEventArgs e)
        {
            SetDragHighlight(_dragHighlightedGrid, false);
            _dragHighlightedGrid = null;
        }

        // --- Custom Elevated Drag-and-Drop Implementation ---
        private bool _isCustomDragging = false;
        private Windows.Foundation.Point _customDragStartPos;
        private object? _customDragItem; // string (folder path) or List<string> (task paths)
        private Grid? _customDragHoveredFolderGrid;

        private void OnCustomDragPointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (!Helpers.ElevationHelper.IsElevated()) return;
            var pt = e.GetCurrentPoint(this).Position;
            var element = e.OriginalSource as DependencyObject;
            if (element == null) return;

            // Check if dragging a Task
            var taskListViewItem = FindParent<ListViewItem>(element);
            if (taskListViewItem != null && FindParent<ListView>(taskListViewItem) == TaskListView)
            {
                // Ignore clicks on ToggleSwitch or CheckBox or Button
                if (element is Microsoft.UI.Xaml.Controls.Primitives.ToggleButton || 
                    FindParent<Microsoft.UI.Xaml.Controls.Primitives.ToggleButton>(element) != null ||
                    element is Button || FindParent<Button>(element) != null) return;

                var model = taskListViewItem.Content as ScheduledTaskModel;
                if (model == null || model.IsReadOnlyFallback) return;

                bool isSelected = false;
                foreach (ScheduledTaskModel sel in TaskListView.SelectedItems) {
                    if (sel == model) { isSelected = true; break; }
                }

                _customDragItem = isSelected && TaskListView.SelectedItems.Count > 0 
                    ? TaskListView.SelectedItems.OfType<ScheduledTaskModel>().Where(t => !t.IsReadOnlyFallback).Select(t => t.Path).ToList()
                    : new List<string> { model.Path };
                
                _customDragStartPos = pt;
                return;
            }

            // Check if dragging a Folder
            var treeViewItem = FindParent<TreeViewItem>(element);
            if (treeViewItem != null && FindParent<TreeView>(treeViewItem) == FolderTreeView)
            {
                // Ignore button clicks
                if (element is Button || FindParent<Button>(element) != null) return;

                var folder = FindFolderFromItemGrid(element);
                if (folder != null && folder.Path != "\\")
                {
                    _customDragItem = folder.Path;
                    _customDragStartPos = pt;
                }
            }
        }

        private void OnCustomDragPointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (_customDragItem == null) return;
            
            var pt = e.GetCurrentPoint(this).Position;
            if (!_isCustomDragging)
            {
                double dx = pt.X - _customDragStartPos.X;
                double dy = pt.Y - _customDragStartPos.Y;
                if (dx * dx + dy * dy > 25) // 5 pixel threshold
                {
                    _isCustomDragging = true;
                    this.CapturePointer(e.Pointer);
                    CustomDragCanvas.Visibility = Visibility.Visible;
                    
                    if (_customDragItem is List<string> tasks)
                    {
                        CustomDragIcon.Glyph = "\uE8F1"; // Task icon
                        CustomDragText.Text = tasks.Count > 1 ? $"Move {tasks.Count} tasks" : "Move task";
                    }
                    else if (_customDragItem is string folderPath)
                    {
                        CustomDragIcon.Glyph = "\uE8B7"; // Folder icon
                        var folderName = folderPath.Split('\\', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? folderPath;
                        CustomDragText.Text = $"Move {folderName}";
                    }
                }
            }

            if (_isCustomDragging)
            {
                Canvas.SetLeft(CustomDragVisual, pt.X + 15);
                Canvas.SetTop(CustomDragVisual, pt.Y + 15);

                // Hit testing for drop target (FolderTreeView item)
                var elements = Microsoft.UI.Xaml.Media.VisualTreeHelper.FindElementsInHostCoordinates(e.GetCurrentPoint(null).Position, FolderTreeView);
                Grid? targetGrid = null;
                foreach (var el in elements)
                {
                    if (el is Grid g && FindFolderFromItemGrid(g) != null)
                    {
                        targetGrid = g;
                        break;
                    }
                }

                if (_customDragHoveredFolderGrid != targetGrid)
                {
                    SetDragHighlight(_customDragHoveredFolderGrid, false);
                    _customDragHoveredFolderGrid = targetGrid;
                    SetDragHighlight(_customDragHoveredFolderGrid, true);
                }
            }
        }

        private async void OnCustomDragPointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (_customDragItem == null) return;

            if (_isCustomDragging)
            {
                this.ReleasePointerCapture(e.Pointer);
                CustomDragCanvas.Visibility = Visibility.Collapsed;
                _isCustomDragging = false;
                SetDragHighlight(_customDragHoveredFolderGrid, false);

                if (_customDragHoveredFolderGrid != null)
                {
                    var targetFolder = FindFolderFromItemGrid(_customDragHoveredFolderGrid);
                    if (targetFolder != null)
                    {
                        if (_customDragItem is List<string> tasks)
                        {
                            await MoveDraggedTasksAsync(string.Join("\n", tasks), targetFolder.Path);
                        }
                        else if (_customDragItem is string folderPath)
                        {
                            await MoveDraggedFolderAsync(folderPath, targetFolder.Path);
                        }
                    }
                }
                _customDragHoveredFolderGrid = null;
            }
            _customDragItem = null;
        }

        private void FolderTreeView_Drop(object sender, DragEventArgs e)
        {
            SetDragHighlight(_dragHighlightedGrid, false);
            _dragHighlightedGrid = null;
        }

        // --- Move helpers ---

        private async System.Threading.Tasks.Task MoveDraggedTasksAsync(string rawPaths, string targetFolderPath)
        {
            var paths = rawPaths.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var errors = new List<string>();

            foreach (var path in paths)
            {
                try { await System.Threading.Tasks.Task.Run(() => ViewModel.TaskService.MoveTask(path, targetFolderPath)); }
                catch (Exception ex) { errors.Add($"{System.IO.Path.GetFileName(path)}: {ex.Message}"); }
            }

            await ViewModel.LoadTasksAsync();
            LoadFolderStructure();

            if (errors.Count > 0)
                await ShowErrorDialog("Some tasks could not be moved:\n\n" + string.Join("\n", errors));
        }

        private async System.Threading.Tasks.Task MoveDraggedFolderAsync(string sourceFolderPath, string targetFolderPath)
        {
            try
            {
                // Ensure target folder is expanded so user sees the change
                _folderExpandedState[targetFolderPath] = true;

                await System.Threading.Tasks.Task.Run(() => ViewModel.TaskService.MoveFolder(sourceFolderPath, targetFolderPath));
                LoadFolderStructure();
                await ViewModel.LoadTasksAsync();
            }
            catch (Exception ex) { await ShowErrorDialog($"Could not move folder: {ex.Message}"); }
        }


        private async Task ShowErrorDialog(string message) 
        {
            if (_isDialogOpen) return;
            _isDialogOpen = true;
            try 
            { 
                var dialog = new ContentDialog 
                { 
                    Title = L("Dialog.Error.Title", "Error"), 
                    Content = message, 
                    CloseButtonText = L("Dialog.Common.OK", "OK"), 
                    XamlRoot = this.XamlRoot, 
                    RequestedTheme = Services.SettingsService.Theme 
                };
                await dialog.ShowAsync(); 
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to show error dialog: {ex.Message}");
            }
            finally { _isDialogOpen = false; }
        }

        // --- AutoSuggest Interactivity ---

        private void EditTaskCategory_GotFocus(object sender, RoutedEventArgs e)
        {
            if (sender is AutoSuggestBox asb) asb.IsSuggestionListOpen = true;
        }

        private void EditTaskCategory_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
            {
                var query = sender.Text.Trim();
                var suggestions = new List<string>();

                if (string.IsNullOrEmpty(query))
                {
                    suggestions.AddRange(ViewModel.SavedCategories);
                }
                else
                {
                    suggestions.AddRange(ViewModel.SavedCategories
                        .Where(c => c.Contains(query, StringComparison.OrdinalIgnoreCase))
                        .ToList());

                    if (!ViewModel.SavedCategories.Any(c => c.Equals(query, StringComparison.OrdinalIgnoreCase)))
                    {
                        suggestions.Add($"Add \"{query}\"");
                    }
                }
                sender.ItemsSource = suggestions;
            }
        }

        private void EditTaskCategory_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
        {
            var selected = args.SelectedItem.ToString() ?? "";
            if (selected.StartsWith("Add \"") && selected.EndsWith("\""))
            {
                var newCat = selected.Substring(5, selected.Length - 6);
                if (!ViewModel.SavedCategories.Any(c => c.Equals(newCat, StringComparison.OrdinalIgnoreCase)))
                {
                    var cats = new List<string>(ViewModel.SavedCategories);
                    cats.Add(newCat);
                    Services.SettingsService.SavedCategories = cats;
                    ViewModel.RefreshSavedCategories();
                }
                sender.Text = newCat;
            }
            else
            {
                sender.Text = selected;
            }
        }

        private void EditTaskTags_GotFocus(object sender, RoutedEventArgs e)
        {
            if (sender is AutoSuggestBox asb)
            {
                RefreshTagSuggestions(asb);
                asb.IsSuggestionListOpen = true;
            }
        }

        private void EditTaskTags_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
            {
                RefreshTagSuggestions(sender);
            }
        }

        private void RefreshTagSuggestions(AutoSuggestBox sender)
        {
            var currentText = sender.Text ?? "";
            var parts = currentText.Split(',').Select(p => p.Trim()).ToList();
            var lastPart = parts.LastOrDefault() ?? "";
            var existingTags = (parts.Count > 1) ? parts.Take(parts.Count - 1).ToList() : new List<string>();

            var availableTags = ViewModel.SavedTags
                .Where(t => !existingTags.Any(et => et.Equals(t, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            var suggestions = new List<string>();
            bool isExactMatch = ViewModel.SavedTags.Any(t => t.Equals(lastPart, StringComparison.OrdinalIgnoreCase));

            if (string.IsNullOrEmpty(lastPart))
            {
                suggestions.AddRange(availableTags);
            }
            else if (isExactMatch)
            {
                // If the last part is a complete tag, show all other available tags
                suggestions.AddRange(availableTags.Where(t => !t.Equals(lastPart, StringComparison.OrdinalIgnoreCase)));
            }
            else
            {
                var filtered = availableTags
                    .Where(t => t.Contains(lastPart, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                suggestions.AddRange(filtered);

                if (!ViewModel.SavedTags.Any(t => t.Equals(lastPart, StringComparison.OrdinalIgnoreCase)))
                {
                    suggestions.Add($"Add \"{lastPart}\"");
                }
            }
            sender.ItemsSource = suggestions;
        }

        private void EditTaskTags_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
        {
            if (args.SelectedItem is not string selected) return;
            
            var currentText = sender.Text ?? "";
            var parts = currentText.Split(',').Select(p => p.Trim()).ToList();
            var lastPart = parts.LastOrDefault() ?? "";
            if (parts.Count > 0) parts.RemoveAt(parts.Count - 1);

            string finalTag = selected;
            if (selected.StartsWith("Add \"") && selected.EndsWith("\""))
            {
                finalTag = selected.Substring(5, selected.Length - 6);
                if (!ViewModel.SavedTags.Any(t => t.Equals(finalTag, StringComparison.OrdinalIgnoreCase)))
                {
                    var tags = new List<string>(ViewModel.SavedTags);
                    tags.Add(finalTag);
                    Services.SettingsService.SavedTags = tags;
                    ViewModel.RefreshSavedCategories();
                }
            }

            // If the last part was already a complete tag and we chose something else, restore it.
            if (ViewModel.SavedTags.Any(t => t.Equals(lastPart, StringComparison.OrdinalIgnoreCase)) && 
                !lastPart.Equals(finalTag, StringComparison.OrdinalIgnoreCase))
            {
                parts.Add(lastPart);
            }

            if (!parts.Any(p => p.Equals(finalTag, StringComparison.OrdinalIgnoreCase)))
            {
                parts.Add(finalTag);
            }

            sender.Text = string.Join(", ", parts.Where(p => !string.IsNullOrEmpty(p))) + ", ";
        }
        private void Settings_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            AnimatedIcon.SetState(this.SettingsAnimatedIcon, "PointerOver");
        }

        private void Settings_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            AnimatedIcon.SetState(this.SettingsAnimatedIcon, "Normal");
        }

        private void HistoryList_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is Models.TaskHistoryEntry entry)
            {
                var detailView = new Dialogs.HistoryEntryDetailDialog(entry);
                var flyout = new Flyout
                {
                    Content = detailView,
                    Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft,
                    FlyoutPresenterStyle = new Style(typeof(FlyoutPresenter))
                    {
                        Setters = { new Setter(FlyoutPresenter.MaxWidthProperty, 1000) }
                    }
                };
                
                if (sender is ListView lv)
                {
                    var container = lv.ContainerFromItem(e.ClickedItem) as FrameworkElement;
                    flyout.ShowAt(container ?? lv);
                }
            }
        }
    }
}
