using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace FluentTaskScheduler.Helpers
{
    internal static class VisualTreeUtil
    {
        /// <summary>Walks up the visual tree and returns the nearest ancestor of type <typeparamref name="T"/>, or null.</summary>
        public static T? FindParent<T>(DependencyObject child) where T : DependencyObject
        {
            var parent = VisualTreeHelper.GetParent(child);
            if (parent == null) return null;
            if (parent is T match) return match;
            return FindParent<T>(parent);
        }
    }
}
