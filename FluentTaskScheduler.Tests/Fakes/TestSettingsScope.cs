using System;
using System.IO;
using FluentTaskScheduler.Services;
using Microsoft.Extensions.DependencyInjection;

namespace FluentTaskScheduler.Tests.Fakes
{
    /// <summary>
    /// Gives a test its own <see cref="SettingsService"/> (backed by a throw-away folder) and
    /// installs it as the app's <see cref="ISettingsService"/>, so the static snooze/pipeline
    /// services that resolve settings from <c>App.Container</c> never touch the real user's file.
    /// </summary>
    public sealed class TestSettingsScope : IDisposable
    {
        private readonly string _dir;
        private readonly IServiceProvider? _previousContainer;

        public SettingsService Settings { get; }

        public TestSettingsScope()
        {
            _dir = Path.Combine(Path.GetTempPath(), "FTS_Tests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            Settings = new SettingsService(_dir);

            _previousContainer = App.Container;
            App.Container = new ServiceCollection()
                .AddSingleton<ISettingsService>(Settings)
                .BuildServiceProvider();
        }

        public void Dispose()
        {
            App.Container = _previousContainer!;
            Settings.Dispose();
            try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch { }
        }
    }
}
