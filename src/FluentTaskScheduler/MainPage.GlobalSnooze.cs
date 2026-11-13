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
    /// <summary>Global snooze banner, dialog and tray integration.</summary>
    public sealed partial class MainPage
    {
        // ========================================================================================================
        // Global Snooze (v1.9)
        // ========================================================================================================

        private void SnoozeService_SnoozeChanged(object? sender, EventArgs e)
        {
            DispatcherQueue?.TryEnqueue(() =>
            {
                UpdateSnoozeBanner();
                TrayIconService.RefreshSnoozeState();
            });
        }

        private void TrayIconService_CustomSnoozeRequested()
        {
            DispatcherQueue?.TryEnqueue(() => ShowSnoozeDialog());
        }

        private void SnoozeToolbarButton_Click(object sender, RoutedEventArgs e) => ShowSnoozeDialog();

        private void StatusFilterBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (StatusFilterBox.SelectedItem is ComboBoxItem item)
                ViewModel.StatusFilter = Enum.TryParse<StatusFilter>(item.Tag?.ToString(), ignoreCase: true, out var status)
                    ? status : StatusFilter.All;
        }

        private void UpdateSnoozeBanner()
        {
            bool active = SnoozeService.IsActive;
            SnoozeBanner.IsOpen = active;

            // Toolbar button doubles as the snooze state indicator.
            SnoozeToolbarText.Text = active
                ? L("Snooze.Toolbar.Active", "Snoozed")
                : L("Snooze.Toolbar.Idle", "Snooze All");
            // Pause glyph while running normally, Play glyph while snoozed (clicking it resumes).
            SnoozeToolbarIcon.Glyph = active ? "" : "";
            ToolTipService.SetToolTip(SnoozeToolbarButton, active
                ? SnoozeService.StatusText
                : L("Snooze.Menu.SnoozeAll", "Snooze All Tasks..."));

            // A snoozed-only view must refresh when the suspended set changes.
            if (ViewModel.StatusFilter == StatusFilter.Snoozed) ViewModel.ApplyFilters();

            if (!active) return;

            SnoozeBanner.Title = SnoozeService.StatusText;
            SnoozeBanner.Message = Settings.SnoozeSuspendsScheduledTasks
                ? L("Snooze.Banner.Suspended", "Scheduled triggers are suspended and manual runs are blocked. Tasks are re-enabled when the snooze ends.")
                : L("Snooze.Banner.ManualOnly", "Runs started from this app are blocked. Windows will still fire scheduled triggers — enable \"Suspend scheduled triggers\" when snoozing to stop those too.");
            SnoozeBannerResumeBtn.Content = L("Snooze.Menu.Resume", "Resume All Tasks");
        }

        private void SnoozeResume_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SnoozeService.Cancel();
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "{Message}", "Failed to resume from global snooze.");
                _ = ShowErrorDialog(L("Snooze.Error.Resume", "Could not resume all tasks: ") + ex.Message);
            }
            UpdateSnoozeBanner();
            TrayIconService.RefreshSnoozeState();
        }

        private async void ShowSnoozeDialog()
        {
            if (this.Content?.XamlRoot == null) return;

            if (SnoozeService.IsActive)
            {
                // Already snoozing — offer to resume instead of stacking another window.
                var confirm = new ContentDialog
                {
                    Title = L("Snooze.Dialog.ActiveTitle", "Global Snooze is active"),
                    Content = SnoozeService.StatusText,
                    PrimaryButtonText = L("Snooze.Menu.Resume", "Resume All Tasks"),
                    CloseButtonText = L("Dialog.Common.Close", "Close"),
                    XamlRoot = this.Content.XamlRoot,
                    RequestedTheme = Settings.Theme
                };
                if (await confirm.ShowAsync() == ContentDialogResult.Primary) SnoozeResume_Click(this, new RoutedEventArgs());
                return;
            }

            SnoozeDialog.Title = L("Snooze.Dialog.Title", "Snooze All Tasks");
            SnoozeDialog.PrimaryButtonText = L("Snooze.Dialog.Confirm", "Snooze");
            SnoozeDialog.CloseButtonText = L("Dialog.Common.Cancel", "Cancel");
            SnoozeDialogIntro.Text = L("Snooze.Dialog.Intro",
                "While snoozed, FluentTaskScheduler refuses to start any task — including chained pipeline runs.");
            Snooze30m.Content = L("Snooze.Duration.30m", "30 Minutes");
            Snooze1h.Content = L("Snooze.Duration.1h", "1 Hour");
            Snooze3h.Content = L("Snooze.Duration.3h", "3 Hours");
            SnoozeReboot.Content = L("Snooze.Duration.Reboot", "Until Next Reboot");
            SnoozeCustom.Content = L("Snooze.Duration.Custom", "Custom Time...");

            SnoozeSuspendTriggers.Content = L("Snooze.SuspendTriggers", "Also suspend scheduled triggers");
            SnoozeSuspendHint.Text = L("Snooze.SuspendTriggersHint",
                "Disables every enabled task through the Task Scheduler API and re-enables exactly those tasks when the snooze ends. Tasks under \\Microsoft\\ (Defender, Windows Update, maintenance) are never touched unless you enable the option below. Protected system tasks are skipped.");
            SnoozeIncludeMicrosoftTasks.Content = L("Snooze.IncludeMicrosoftTasks", "Also suspend Microsoft's own scheduled tasks (Defender, Windows Update, maintenance...)");
            SnoozeMicrosoftWarning.Message = L("Snooze.IncludeMicrosoftTasksWarning",
                "Not recommended: disabling these can break Windows security scans, updates, and maintenance until the snooze ends.");

            SnoozeSuspendTriggers.IsChecked = Settings.SnoozeSuspendsScheduledTasks;
            SnoozeIncludeMicrosoftTasks.IsChecked = Settings.SnoozeIncludeMicrosoftTasks;
            UpdateSnoozeSuspendOptionsVisibility();
            Snooze30m.IsChecked = true;
            SnoozeCustomDate.Date = DateTimeOffset.Now;
            SnoozeCustomTime.Time = DateTime.Now.AddHours(2).TimeOfDay;
            SnoozeDialogError.IsOpen = false;

            SnoozeDialog.XamlRoot = this.Content.XamlRoot;
            SnoozeDialog.RequestedTheme = Settings.Theme;
            await SnoozeDialog.ShowAsync();
        }

        private void SnoozeCustom_Changed(object sender, RoutedEventArgs e)
        {
            bool custom = SnoozeCustom.IsChecked == true;
            if (SnoozeCustomDate != null) SnoozeCustomDate.IsEnabled = custom;
            if (SnoozeCustomTime != null) SnoozeCustomTime.IsEnabled = custom;
        }

        private void SnoozeSuspendTriggers_Changed(object sender, RoutedEventArgs e) => UpdateSnoozeSuspendOptionsVisibility();

        private void UpdateSnoozeSuspendOptionsVisibility()
        {
            bool suspend = SnoozeSuspendTriggers.IsChecked == true;
            SnoozeIncludeMicrosoftTasks.Visibility = suspend ? Visibility.Visible : Visibility.Collapsed;
            SnoozeMicrosoftWarning.IsOpen = suspend && SnoozeIncludeMicrosoftTasks.IsChecked == true;
        }

        private void SnoozeDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            try
            {
                // Persist the suspension preferences first: SnoozeService reads them while activating.
                Settings.SnoozeSuspendsScheduledTasks = SnoozeSuspendTriggers.IsChecked == true;
                Settings.SnoozeIncludeMicrosoftTasks = SnoozeSuspendTriggers.IsChecked == true && SnoozeIncludeMicrosoftTasks.IsChecked == true;

                if (SnoozeReboot.IsChecked == true)
                {
                    SnoozeService.SnoozeUntilReboot();
                }
                else if (SnoozeCustom.IsChecked == true)
                {
                    var end = SnoozeCustomDate.Date.Date + SnoozeCustomTime.Time;
                    if (end <= DateTime.Now)
                    {
                        args.Cancel = true;
                        SnoozeDialogError.Message = L("Snooze.Error.PastTime", "Pick a time in the future.");
                        SnoozeDialogError.IsOpen = true;
                        return;
                    }
                    SnoozeService.SnoozeUntilLocalTime(end);
                }
                else
                {
                    int minutes = 30;
                    foreach (var rb in new[] { Snooze30m, Snooze1h, Snooze3h })
                        if (rb.IsChecked == true && int.TryParse(rb.Tag?.ToString(), out int m)) minutes = m;

                    SnoozeService.Snooze(TimeSpan.FromMinutes(minutes));
                }

                UpdateSnoozeBanner();
                TrayIconService.RefreshSnoozeState();
            }
            catch (Exception ex)
            {
                args.Cancel = true;
                Serilog.Log.Error(ex, "{Message}", "Failed to start global snooze from the dialog.");
                SnoozeDialogError.Message = ex.Message;
                SnoozeDialogError.IsOpen = true;
            }
        }
    }
}
