using FluentTaskScheduler.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace FluentTaskScheduler.Dialogs;

/// <summary>Snoozes all tasks, or one, until a moment the user picks; for all tasks, with the options of their snooze.</summary>
public sealed partial class SnoozeDialog : ContentDialog
{
    public SnoozeDialog(SnoozeUntilViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
    }

    public SnoozeUntilViewModel ViewModel { get; }

    private async void Snooze_Click(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        // The dialog stays open until the snooze has started; what is in the way shows at the top.
        ContentDialogButtonClickDeferral deferral = args.GetDeferral();
        try
        {
            args.Cancel = !await ViewModel.SnoozeAsync();
        }
        finally
        {
            deferral.Complete();
        }
    }

    // A snooze that has been asked for cannot be called back. Until it has started or failed to,
    // Cancel would only hide whether it did.
    private void Dialog_Closing(ContentDialog sender, ContentDialogClosingEventArgs args)
    {
        if (ViewModel.IsSnoozing) args.Cancel = true;
    }
}
