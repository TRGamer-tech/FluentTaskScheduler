using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using FluentTaskScheduler.Models;
using Windows.ApplicationModel.DataTransfer;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace FluentTaskScheduler.Dialogs
{
    public sealed partial class HistoryEntryDetailDialog : UserControl
    {
        public TaskHistoryEntry Entry { get; }
        public List<SystemEventEntry> NearbySystemEvents { get; }
        public bool HasNearbySystemEvents => NearbySystemEvents.Count > 0;

        public HistoryEntryDetailDialog(TaskHistoryEntry entry)
        {
            this.InitializeComponent();
            this.Entry = entry;
            this.NearbySystemEvents = LoadNearbySystemEvents(entry);
            this.RequestedTheme = Services.SettingsService.Theme;
        }

        /// <summary>Correlates this run with logon/logoff/shutdown/reboot/sleep events from the
        /// Windows System log within +/- 10 minutes, so an unexpected result can be explained
        /// (e.g. "the machine restarted right after this run started").</summary>
        private static List<SystemEventEntry> LoadNearbySystemEvents(TaskHistoryEntry entry)
        {
            try
            {
                if (DateTime.TryParse(entry.Time, out var time))
                {
                    return new Services.TaskServiceWrapper().GetSystemEventsNear(time, TimeSpan.FromMinutes(10));
                }
            }
            catch { }
            return new List<SystemEventEntry>();
        }

        private void CopyButton_Click(object sender, RoutedEventArgs e)
        {
            var dataPackage = new DataPackage();
            var details = $"Time: {Entry.Time}\n" +
                          $"Result: {Entry.Result}\n" +
                          $"Event ID: {Entry.EventId}\n" +
                          $"Level: {Entry.Level}\n" +
                          $"User: {Entry.User}\n" +
                          $"Computer: {Entry.Computer}\n" +
                          $"Message: {Entry.Message}";
            dataPackage.SetText(details);
            Clipboard.SetContent(dataPackage);
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            // Find the flyout that contains this control and close it
            if (this.Parent is FlyoutPresenter presenter && presenter.Parent is Popup popup)
            {
                popup.IsOpen = false;
            }
            else
            {
                // Fallback for different hosting scenarios
                var parent = this.Parent;
                while (parent != null)
                {
                    if (parent is FlyoutPresenter fp)
                    {
                        if (VisualTreeHelper.GetParent(fp) is Popup p) { p.IsOpen = false; break; }
                    }
                    parent = VisualTreeHelper.GetParent(parent);
                }
            }
        }
    }
}
