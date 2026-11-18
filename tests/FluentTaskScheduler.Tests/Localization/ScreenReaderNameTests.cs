using FluentTaskScheduler.Localization;
using FluentTaskScheduler.Tests.Snoozing;

namespace FluentTaskScheduler.Tests.Localization;

[Collection(LocalizedTextCollection.Name)]
public sealed class ScreenReaderNameTests : IDisposable
{
    public ScreenReaderNameTests() => Loc.Use(key => key switch
    {
        "Text.DetailFormat" => "{0} ({1})",
        "Editor.MoveUp" => "Move up",
        "TriggerTitle.Text" => "Triggers",
        "ActionTitle.Text" => "Actions",
        _ => null
    });

    public void Dispose() => Loc.Use(static _ => null);

    [Fact]
    public void AControlThatReadsTheSameAsAnother_IsToldApartByThePlaceItIsIn()
    {
        Assert.Equal("Move up (Triggers)", ScreenReaderName.In("Editor.MoveUp", "TriggerTitle.Text"));
        Assert.Equal("Move up (Actions)", ScreenReaderName.In("Editor.MoveUp", "ActionTitle.Text"));
    }
}
