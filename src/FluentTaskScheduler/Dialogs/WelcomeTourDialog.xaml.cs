using FluentTaskScheduler.ViewModels;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;

namespace FluentTaskScheduler.Dialogs;

/// <summary>The welcome tour, shown at the very first start and again on request.</summary>
public sealed partial class WelcomeTourDialog : ContentDialog
{
    public WelcomeTourDialog(WelcomeTourViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(WelcomeTourViewModel.Index)) ReadOut(viewModel.Slides[viewModel.Index]);
        };
    }

    public WelcomeTourViewModel ViewModel { get; }

    /// <summary>For x:Bind: a slide that is not showing keeps its place in the layout, unseen.</summary>
    public static double OpacityOf(bool isCurrent) => isCurrent ? 1 : 0;

    /// <summary>For x:Bind: a slide that is not showing is not read out by a screen reader either.</summary>
    public static AccessibilityView ViewOf(bool isCurrent) => isCurrent ? AccessibilityView.Content : AccessibilityView.Raw;

    // Back and Next are the dialog's own buttons, which would close it.
    private void Back_Click(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        args.Cancel = true;
        ViewModel.GoBack();
    }

    private void Next_Click(ContentDialog sender, ContentDialogButtonClickEventArgs args) => args.Cancel = ViewModel.GoNext();

    // The focus stays on the button or the dot that changed the slide, and a screen reader reads
    // out what has the focus: of the new slide it would say nothing. So the pager tells it.
    private void ReadOut(TourSlide slide) =>
        FrameworkElementAutomationPeer.CreatePeerForElement(TourPager)?.RaiseNotificationEvent(
            AutomationNotificationKind.Other, AutomationNotificationProcessing.ImportantMostRecent,
            slide.Title + Environment.NewLine + slide.Body, activityId: "TourSlide");
}
