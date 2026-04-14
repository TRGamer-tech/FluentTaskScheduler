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

        public HistoryEntryDetailDialog(TaskHistoryEntry entry)
        {
            this.InitializeComponent();
            this.Entry = entry;
            this.RequestedTheme = Services.SettingsService.Theme;
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
