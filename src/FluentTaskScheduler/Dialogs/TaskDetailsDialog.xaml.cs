using System.Globalization;
using FluentTaskScheduler.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FluentTaskScheduler.Dialogs;

/// <summary>What the user asked for in the details of a task. The dialog closes; the task list carries it out.</summary>
public enum TaskDetailsRequest
{
    None,
    Run,
    Stop,
    Edit,
    Export,
    Delete,

    /// <summary>Snooze the task until a moment the user picks, which takes a dialog of its own.</summary>
    SnoozeUntil
}

/// <summary>One task at a glance: what starts it, what it runs, and what became of its runs.</summary>
public sealed partial class TaskDetailsDialog : ContentDialog
{
    public TaskDetailsDialog(TaskDetailsViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        Opened += async (_, _) => await ViewModel.LoadAsync();
    }

    public TaskDetailsViewModel ViewModel { get; }

    public TaskDetailsRequest Request { get; private set; }

    public static Visibility VisibleIfText(string text) => text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    public static Visibility VisibleIfAny(IReadOnlyList<string> lines) => lines.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    public static Visibility VisibleUnless(bool value) => value ? Visibility.Collapsed : Visibility.Visible;
    public static string DateText(DateTime? time) => time?.ToString("g", CultureInfo.CurrentCulture) ?? "-";

    private void Request_Click(object sender, RoutedEventArgs e)
    {
        Request = Enum.Parse<TaskDetailsRequest>((string)((FrameworkElement)sender).Tag);
        Hide();
    }

    private void History_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        ViewModel.Selected = DetailsHistory.SelectedItem as HistoryEntry;
}
