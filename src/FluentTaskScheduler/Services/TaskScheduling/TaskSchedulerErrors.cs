using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.Linq;
using System.Security.Principal;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentTaskScheduler.Models;
using Microsoft.Win32.TaskScheduler;
using System.Collections.ObjectModel;
using AppTaskState = FluentTaskScheduler.Models.Enums.TaskState;
using AppTriggerType = FluentTaskScheduler.Models.Enums.TriggerType;

namespace FluentTaskScheduler.Services
{
    internal static class TaskSchedulerErrors
    {
        /// <summary>Win32 E_ACCESSDENIED (0x80070005), which is what Task Scheduler's COM layer
        /// raises for a protected/system task regardless of the OS display language.</summary>
        internal const int E_ACCESSDENIED = unchecked((int)0x80070005);

        /// <summary>
        /// Checks the HRESULT (walking inner exceptions too) instead of matching the exception
        /// message text — the old code only recognized English and German "access denied" strings,
        /// so the check silently failed on any other OS display language (see 3.9).
        /// </summary>
        internal static bool IsAccessDenied(Exception? ex)
        {
            for (var e = ex; e != null; e = e.InnerException)
            {
                if (e.HResult == E_ACCESSDENIED) return true;
                if (e is System.Runtime.InteropServices.COMException com && com.ErrorCode == E_ACCESSDENIED) return true;
            }
            return false;
        }
    }
}
