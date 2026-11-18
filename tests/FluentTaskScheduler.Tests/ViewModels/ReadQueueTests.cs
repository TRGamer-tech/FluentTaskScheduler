using FluentTaskScheduler.ViewModels;

namespace FluentTaskScheduler.Tests.ViewModels;

public sealed class ReadQueueTests
{
    [Fact]
    public async Task AReadBegins_OnlyOnceTheReadsAskedForBeforeItAreDone()
    {
        var queue = new ReadQueue();
        var slow = new TaskCompletionSource();
        var done = new List<string>();

        Task first = queue.Enqueue(async () =>
        {
            await slow.Task;
            done.Add("first");
        });
        Task second = queue.Enqueue(() =>
        {
            done.Add("second");
            return Task.CompletedTask;
        });

        Assert.Empty(done);
        Assert.False(queue.Pending.IsCompleted);

        slow.SetResult();
        await queue.Pending;

        Assert.Equal(["first", "second"], done);
        Assert.True(first.IsCompletedSuccessfully && second.IsCompletedSuccessfully);
    }
}
