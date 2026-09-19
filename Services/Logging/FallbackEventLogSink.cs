using System;
using System.Diagnostics;
using Serilog.Core;
using Serilog.Events;

namespace FluentTaskScheduler.Services.Logging
{
    /// <summary>
    /// Mirrors Error/Fatal events to the Windows Event Log. Tries to register a named
    /// event source (requires admin on first run); if that fails, falls back to writing
    /// under the built-in "Application" source with the source name prefixed in the text.
    /// </summary>
    internal sealed class FallbackEventLogSink : ILogEventSink
    {
        private readonly string _source;

        public FallbackEventLogSink(string source) => _source = source;

        public void Emit(LogEvent logEvent)
        {
            try
            {
                string message = logEvent.RenderMessage();
                if (logEvent.Exception != null)
                {
                    message += logEvent.Level == LogEventLevel.Fatal
                        ? $"{Environment.NewLine}Stack Trace: {logEvent.Exception.StackTrace ?? "No stack"}"
                        : $" | {logEvent.Exception.GetType().Name}: {logEvent.Exception.Message}";
                }

                var type = logEvent.Level == LogEventLevel.Fatal ? EventLogEntryType.Error : EventLogEntryType.Warning;

                try
                {
                    if (!EventLog.SourceExists(_source))
                        EventLog.CreateEventSource(_source, "Application");
                    EventLog.WriteEntry(_source, message, type);
                }
                catch
                {
                    // Source registration needs admin; fall back to the built-in Application source.
                    using var log = new EventLog("Application") { Source = "Application" };
                    log.WriteEntry($"[{_source}] {message}", type);
                }
            }
            catch
            {
                // Logging should never crash the app.
            }
        }
    }
}
