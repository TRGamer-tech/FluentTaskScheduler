using FluentTaskScheduler.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace FluentTaskScheduler.Views;

public sealed partial class QuickActionsPage : Page
{
    public QuickActionsPage(QuickActionsViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
    }

    public QuickActionsViewModel ViewModel { get; }

    /// <summary>The UI automation id of one part of an action's card, e.g. "QuickAction.FlushDNS.Run".</summary>
    public static string IdOf(string actionId, string part) => $"QuickAction.{actionId}.{part}";
}
