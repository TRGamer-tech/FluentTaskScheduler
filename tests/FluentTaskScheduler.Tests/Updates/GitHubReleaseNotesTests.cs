using System.Net;
using FluentTaskScheduler.Updates;

namespace FluentTaskScheduler.Tests.Updates;

public class GitHubReleaseNotesTests
{
    /// <summary>Stands in for GitHub: answers every request with what the test decided, and records what was asked.</summary>
    private sealed class StubServer(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(answer(request));
        }
    }

    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json) };

    [Fact]
    public async Task TheNotesOfARelease_AreAskedForByItsTag_AndComeBackWithoutTheTagInTheTitle()
    {
        var server = new StubServer(_ => Json(
            """{"tag_name":"V1.9.0","name":"V1.9.0 - The \"Chain Reaction\" Update","body":"## New\r\n- Task chaining","html_url":"https://github.com/TRGamer-tech/FluentTaskScheduler/releases/tag/V1.9.0"}"""));
        using var source = new GitHubReleaseNotes(server);

        ReleaseNotes? notes = await source.FindAsync("1.9.0");

        Assert.Equal(
            new ReleaseNotes("1.9.0", "The \"Chain Reaction\" Update", "## New\r\n- Task chaining", "https://github.com/TRGamer-tech/FluentTaskScheduler/releases/tag/V1.9.0"),
            notes);

        HttpRequestMessage request = Assert.Single(server.Requests);
        Assert.Equal("https://api.github.com/repos/TRGamer-tech/FluentTaskScheduler/releases/tags/V1.9.0", request.RequestUri!.ToString());
        Assert.NotEmpty(request.Headers.UserAgent);
    }

    [Theory]
    [InlineData("V2.0.0", "")]
    [InlineData("v2.0.0", "")]
    [InlineData("V2.0.0: Spring cleaning", "Spring cleaning")]
    [InlineData("Spring cleaning", "Spring cleaning")]
    public async Task TheTitle_IsTheNameOfTheReleaseWithoutItsTag(string name, string expectedTitle)
    {
        using var source = new GitHubReleaseNotes(new StubServer(_ => Json($$"""{"name":"{{name}}","body":"notes"}""")));

        Assert.Equal(expectedTitle, (await source.FindAsync("2.0.0"))!.Title);
    }

    [Theory]
    [InlineData("""{"name":"V2.0.0","body":null}""")]
    [InlineData("""{"name":"V2.0.0","body":"  "}""")]
    [InlineData("""{"name":"V2.0.0"}""")]
    [InlineData("""{"name":null,"body":42}""")]
    [InlineData("[]")]
    [InlineData("<html>a sign-in page of some hotspot</html>")]
    public async Task AnAnswerWithoutNotes_CountsAsNoNotes(string answer)
    {
        using var source = new GitHubReleaseNotes(new StubServer(_ => Json(answer)));

        Assert.Null(await source.FindAsync("2.0.0"));
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task ARefusal_CountsAsNoNotes(HttpStatusCode status)
    {
        using var source = new GitHubReleaseNotes(new StubServer(_ => new HttpResponseMessage(status)));

        Assert.Null(await source.FindAsync("2.0.0"));
    }

    [Fact]
    public async Task WithoutAConnection_ThereAreNoNotes_AndNoException()
    {
        using var offline = new GitHubReleaseNotes(new StubServer(_ => throw new HttpRequestException("No such host is known.")));
        using var stalled = new GitHubReleaseNotes(new StubServer(_ => throw new TaskCanceledException("The request timed out.")));

        Assert.Null(await offline.FindAsync("2.0.0"));
        Assert.Null(await stalled.FindAsync("2.0.0"));
    }
}
