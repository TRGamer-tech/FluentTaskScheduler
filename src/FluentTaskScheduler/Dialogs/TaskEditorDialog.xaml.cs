using FluentTaskScheduler.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FluentTaskScheduler.Dialogs;

/// <summary>The form for creating and changing a task.</summary>
public sealed partial class TaskEditorDialog : ContentDialog
{
    public TaskEditorDialog(TaskEditorViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
    }

    public TaskEditorViewModel ViewModel { get; }

    public static Visibility VisibleIfNone(int count) => count == 0 ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility VisibleIfUnsupported(ActionViewModel? action) => action is { IsEditable: false } ? Visibility.Visible : Visibility.Collapsed;

    private async void Save_Click(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        // The dialog stays open until the task is really saved; what is in the way shows at the top.
        // It is kept open from the start, so that a failure nobody foresaw does not close it and
        // throw away what was entered.
        ContentDialogButtonClickDeferral deferral = args.GetDeferral();
        args.Cancel = true;
        try
        {
            args.Cancel = !await ViewModel.SaveAsync();
            if (args.Cancel) EditorScroller.ChangeView(null, 0, null);
        }
        finally
        {
            deferral.Complete();
        }
    }

    private void Category_GotFocus(object sender, RoutedEventArgs e) => EditorCategory.ItemsSource = ViewModel.SuggestCategories();

    private void Category_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput) sender.ItemsSource = ViewModel.SuggestCategories();
    }

    private void RemoveOnSuccess_Click(object sender, RoutedEventArgs e) =>
        ViewModel.CompletionActions.RemoveOnSuccessCommand.Execute((string)((FrameworkElement)sender).Tag);

    private void RemoveOnFailure_Click(object sender, RoutedEventArgs e) =>
        ViewModel.CompletionActions.RemoveOnFailureCommand.Execute((string)((FrameworkElement)sender).Tag);
}
