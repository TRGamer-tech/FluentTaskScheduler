using System.Globalization;
using FluentTaskScheduler.Localization;

namespace FluentTaskScheduler.Tests.Localization;

public class AppLanguagesTests
{
    [Theory]
    [InlineData("de-DE", "en-US", "de-DE")]   // a choice wins over Windows
    [InlineData("ja-jp", "en-US", "ja-JP")]   // however it is spelled
    [InlineData("", "de-DE", "de-DE")]        // no choice: follow Windows
    [InlineData(null, "de-AT", "de-DE")]      // same language, other region
    [InlineData("", "zh-Hans-CN", "zh-CN")]
    [InlineData("", "fr-FR", "en-US")]        // not translated: English
    [InlineData("fr-FR", "de-DE", "de-DE")]   // a choice the app no longer has falls back to Windows
    [InlineData("xx", "", "en-US")]
    public void Resolve_PicksTheChosenLanguageThenWindowsThenEnglish(string? chosen, string windows, string expected)
    {
        Assert.Equal(expected, AppLanguages.Resolve(chosen, CultureInfo.GetCultureInfo(windows)));
    }
}
