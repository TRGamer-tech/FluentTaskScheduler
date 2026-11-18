namespace FluentTaskScheduler.Localization;

/// <summary>
/// Names for screen readers that are put together from texts the app shows anyway. In XAML:
/// <c>AutomationProperties.Name="{x:Bind l:ScreenReaderName.In('Editor.MoveUp', 'TriggerTitle.Text')}"</c>.
/// </summary>
public static class ScreenReaderName
{
    /// <summary>
    /// The text of <paramref name="key"/> with that of <paramref name="placeKey"/> in brackets, as in
    /// "Move up (Triggers)". For a control whose text leaves out what its place on screen says:
    /// a screen reader names the control alone, and two that read the same could not be told apart.
    /// </summary>
    public static string In(string key, string placeKey) => Loc.Format("Text.DetailFormat", Loc.Get(key), Loc.Get(placeKey));
}
