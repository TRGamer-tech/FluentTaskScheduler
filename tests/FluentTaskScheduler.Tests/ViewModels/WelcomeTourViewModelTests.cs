using FluentTaskScheduler.ViewModels;

namespace FluentTaskScheduler.Tests.ViewModels;

public class WelcomeTourViewModelTests
{
    private readonly WelcomeTourViewModel _tour = new();

    private int[] ShowingSlides => _tour.Slides.Select((slide, position) => slide.IsCurrent ? position : -1).Where(position => position >= 0).ToArray();

    [Fact]
    public void TheTour_StartsOnItsFirstSlide_WithNoWayBack()
    {
        Assert.Equal(0, _tour.Index);
        Assert.Equal([0], ShowingSlides);
        Assert.False(_tour.CanGoBack);
        Assert.Equal("Dialog.Next", _tour.NextText);
        Assert.Equal(_tour.Slides.Count, _tour.SlideCount);
    }

    [Fact]
    public void EverySlide_HasAPictureATitleAndAText_OfItsOwn()
    {
        Assert.All(_tour.Slides, slide => Assert.Equal(1, slide.Glyph.Length));
        Assert.Equal(_tour.SlideCount, _tour.Slides.Select(slide => slide.Title).Distinct().Count());
        Assert.Equal(_tour.SlideCount, _tour.Slides.Select(slide => slide.Body).Distinct().Count());
        Assert.All(_tour.Slides, slide => Assert.StartsWith("Tour.", slide.Title));
    }

    [Fact]
    public void Next_MovesOnOneSlideAtATime_UntilTheLast_WhereItEndsTheTour()
    {
        for (int position = 1; position < _tour.SlideCount; position++)
        {
            Assert.Equal("Dialog.Next", _tour.NextText);
            Assert.True(_tour.GoNext());
            Assert.Equal(position, _tour.Index);
            Assert.Equal([position], ShowingSlides);
        }

        Assert.Equal("Dialog.GetStarted", _tour.NextText);
        Assert.False(_tour.GoNext());
        Assert.Equal(_tour.SlideCount - 1, _tour.Index);
    }

    [Fact]
    public void Back_ReturnsToTheSlideBefore_AndStopsAtTheFirst()
    {
        _tour.GoNext();
        _tour.GoNext();
        Assert.True(_tour.CanGoBack);

        _tour.GoBack();
        _tour.GoBack();
        _tour.GoBack();

        Assert.Equal(0, _tour.Index);
        Assert.Equal([0], ShowingSlides);
        Assert.False(_tour.CanGoBack);
    }

    [Fact]
    public void JumpingToASlide_AsThePagerDoes_ShowsThatSlideOnly()
    {
        var changed = new List<string?>();
        _tour.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        _tour.Index = _tour.SlideCount - 1;

        Assert.Equal([_tour.SlideCount - 1], ShowingSlides);
        Assert.Equal("Dialog.GetStarted", _tour.NextText);
        Assert.Contains(nameof(WelcomeTourViewModel.NextText), changed);
        Assert.Contains(nameof(WelcomeTourViewModel.CanGoBack), changed);
    }
}
