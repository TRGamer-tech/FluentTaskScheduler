using FluentTaskScheduler.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace FluentTaskScheduler.Dialogs;

/// <summary>Asks where an exported task goes and who it runs as, and imports it.</summary>
public sealed partial class ImportTaskDialog : ContentDialog
{
    public ImportTaskDialog(ImportTaskViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
    }

    public ImportTaskViewModel ViewModel { get; }

    private async void Import_Click(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        // The dialog stays open until the task is really registered.
        ContentDialogButtonClickDeferral deferral = args.GetDeferral();
        try
        {
            args.Cancel = !await ViewModel.ImportAsync();
        }
        finally
        {
            deferral.Complete();
        }
    }
}
