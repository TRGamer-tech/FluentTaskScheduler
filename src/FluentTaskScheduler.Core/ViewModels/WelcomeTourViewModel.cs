using CommunityToolkit.Mvvm.ComponentModel;
using FluentTaskScheduler.Localization;

namespace FluentTaskScheduler.ViewModels;

/// <summary>One slide of the welcome tour.</summary>
public sealed partial class TourSlide(string glyph, string title, string body) : ObservableObject
{
    /// <summary>The slide's picture: a character of the Windows icon font.</summary>
    public string Glyph { get; } = glyph;
    public string Title { get; } = title;
    public string Body { get; } = body;

    /// <summary>True for the slide that is showing.</summary>
    [ObservableProperty] private bool _isCurrent;
}

/// <summary>The welcome tour: a handful of slides that say what the app is for, one showing at a time.</summary>
public sealed partial class WelcomeTourViewModel : ObservableObject
{
    public WelcomeTourViewModel()
    {
        Slides =
        [
            new TourSlide("\uE787", Loc.Get("Tour.Welcome.Title"), Loc.Get("Tour.Welcome.Body")),
            new TourSlide("\uE710", Loc.Get("Tour.Create.Title"), Loc.Get("Tour.Create.Body")),
            new TourSlide("\uE81C", Loc.Get("Tour.History.Title"), Loc.Get("Tour.History.Body")),
            new TourSlide("\uE71B", Loc.Get("Tour.Automation.Title"), Loc.Get("Tour.Automation.Body")),
            new TourSlide("\uE8F1", Loc.Get("Tour.Library.Title"), Loc.Get("Tour.Library.Body")),
            new TourSlide("\uE7EF", Loc.Get("Tour.Admin.Title"), Loc.Get("Tour.Admin.Body")),
            new TourSlide("\uE713", Loc.Get("Tour.Settings.Title"), Loc.Get("Tour.Settings.Body"))
        ];
        Slides[0].IsCurrent = true;
    }

    public IReadOnlyList<TourSlide> Slides { get; }
    public int SlideCount => Slides.Count;

    /// <summary>Position of the slide that is showing.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoBack), nameof(NextText))]
    private int _index;

    public bool CanGoBack => Index > 0;

    /// <summary>What the button that moves on says; on the last slide it ends the tour.</summary>
    public string NextText => Loc.Get(IsLastSlide ? "Dialog.GetStarted" : "Dialog.Next");

    public void GoBack()
    {
        if (CanGoBack) Index--;
    }

    /// <returns>False on the last slide: there is nothing to move on to, and the tour is over.</returns>
    public bool GoNext()
    {
        if (IsLastSlide) return false;
        Index++;
        return true;
    }

    private bool IsLastSlide => Index == Slides.Count - 1;

    partial void OnIndexChanged(int oldValue, int newValue)
    {
        Slides[oldValue].IsCurrent = false;
        Slides[newValue].IsCurrent = true;
    }
}
