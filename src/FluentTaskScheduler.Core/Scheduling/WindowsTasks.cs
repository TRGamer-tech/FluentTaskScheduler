namespace FluentTaskScheduler.Scheduling;

/// <summary>The tasks Windows registers for itself: Defender, Windows Update, maintenance and several hundred more.</summary>
internal static class WindowsTasks
{
    private const string Folder = @"\Microsoft\";

    public static bool Contains(string taskPath) => taskPath.StartsWith(Folder, StringComparison.OrdinalIgnoreCase);
}
