using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace FluentTaskScheduler.Helpers
{
    /// <summary>Shows the small "info" flyout for the (i) icons in the task dialog.</summary>
    internal static class InfoFlyoutHelper
    {
        /// <summary>
        /// Shows the icon's tooltip text in a flyout. When the flyout is light-dismissed, WinUI treats
        /// the outside tap as a focus change on the dialog's ScrollViewer and calls BringIntoView, which
        /// jumps the scroll to the top; the saved offset is restored on the next dispatcher frame.
        /// </summary>
        public static void Show(FrameworkElement icon, ScrollViewer? scrollViewer)
        {
            var text = ToolTipService.GetToolTip(icon) as string;
            if (string.IsNullOrEmpty(text)) return;

            // Un-escape XML character references that appear literally in the string
            text = text.Replace("&#x0a;", "\n").Replace("&#x2022;", "•");

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
                Placement = FlyoutPlacementMode.Bottom
            };

            if (scrollViewer != null)
            {
                double savedOffset = scrollViewer.VerticalOffset;
                flyout.Closed += (_, _) =>
                    icon.DispatcherQueue.TryEnqueue(() => scrollViewer.ChangeView(null, savedOffset, null, true));
            }

            flyout.ShowAt(icon);
        }
    }
}
