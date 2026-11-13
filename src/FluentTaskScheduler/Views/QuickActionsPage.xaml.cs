using FluentTaskScheduler.Localization;
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

    /// <summary>
    /// For x:Bind: what a screen reader calls a button of a card, with the title of its action,
    /// e.g. "Run Flush DNS". Every card has the same buttons, which this tells apart.
    /// </summary>
    public static string ButtonName(string format, string actionTitle) => Loc.Format(format, actionTitle);
}
