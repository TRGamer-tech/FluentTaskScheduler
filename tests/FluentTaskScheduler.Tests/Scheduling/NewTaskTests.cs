using FluentTaskScheduler.Localization;
using FluentTaskScheduler.Scheduling;
using FluentTaskScheduler.Tests.Snoozing;

namespace FluentTaskScheduler.Tests.Scheduling;

[Collection(LocalizedTextCollection.Name)]
public sealed class NewTaskTests : IDisposable
{
    public NewTaskTests() => Loc.Use(key => key switch
    {
        "NewTask.Email.Subject" => "Gestartet",
        "NewTask.Email.Body" => "Die Aufgabe läuft.",
        "NewTask.Message.Text" => "It's \"done\"",
        _ => null
    });

    public void Dispose() => Loc.Use(static _ => null);

    [Fact]
    public void TheSampleActions_AreWrittenInTheUsersLanguage_AndQuotedForPowerShell()
    {
        Assert.Contains("-Subject 'Gestartet' -Body 'Die Aufgabe läuft.'", NewTask.SendEmail.Arguments);
        Assert.Contains("::Show('It''s ''done''', 'FluentTaskScheduler')", NewTask.ShowMessage.Arguments);
    }
}
