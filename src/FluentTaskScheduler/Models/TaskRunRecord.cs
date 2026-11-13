using System;

namespace FluentTaskScheduler.Models
{
    /// <summary>
    /// A single lightweight entry read from the Task Scheduler operational log.
    /// Unlike <see cref="TaskHistoryEntry"/> this is parsed straight from the event XML
    /// (no <c>FormatDescription()</c> call), which makes bulk reads of thousands of
    /// records fast enough for the dashboard analytics.
    /// </summary>
    public class TaskRunRecord
    {
        public string TaskPath { get; set; } = "";
        public string TaskName { get; set; } = "";
        public DateTime Time { get; set; }
        public int EventId { get; set; }
        public string InstanceId { get; set; } = "";

        /// <summary>
        /// Action return code, present on EventID 201 / 103 / 203. Stored as a long because
        /// HRESULT-style codes (e.g. 2147942402) do not fit in a signed int.
        /// </summary>
        public long? ResultCode { get; set; }

        /// <summary>Exit code formatted the way Task Scheduler shows it: 0, or hex for anything else.</summary>
        public string ExitCodeText => ResultCode switch
        {
            null => "-",
            0 => "0",
            _ => "0x" + ((uint)ResultCode.Value).ToString("X8")
        };

        /// <summary>EventID 100 — an instance of the task was started.</summary>
        public bool IsStart => EventId == 100;

        /// <summary>EventID 102 — the task instance finished.</summary>
        public bool IsCompletion => EventId == 102;

        /// <summary>A completion carrying an explicit action exit code.</summary>
        public bool IsActionResult => EventId == 201;

        /// <summary>Task Scheduler could not start the task / launch the action at all.</summary>
        public bool IsLaunchFailure => EventId == 103 || EventId == 203;

        /// <summary>True for records that represent a definitive failed outcome.</summary>
        public bool IsFailure => IsLaunchFailure || (IsActionResult && ResultCode.GetValueOrDefault() != 0);

        /// <summary>True for records that represent a definitive successful outcome.</summary>
        public bool IsSuccess => IsActionResult && ResultCode.GetValueOrDefault() == 0;
    }
}
