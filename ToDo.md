# To Do:

1. **Weekly/Monthly triggers skip today.** If today is one of the selected days and the scheduled
   time hasn't passed yet, a Weekly or Monthly trigger should fire today and then continue on its
   normal cycle. Instead it always rolls to the next occurrence, skipping today even when it should
   run. Daily triggers do not have this problem. Pre-existing behavior, not caused by the enum
   refactor (confirmed: `GetDaysOfWeek`/`CreateMonthlyTrigger` in `Services/TaskServiceWrapper.cs`
   were untouched by that change).
2. **"Queue" / "Stop existing instance" policies don't apply to manual Run.** Manually clicking Run
   while a task is already running always starts a new instance in parallel for these two policies,
   even though "Do not start" and "Parallel" behave correctly. Likely a Windows Task Scheduler
   limitation: `RunEx` (manual run) only honors IgnoreNew/Parallel; Queue/StopExisting are only
   enforced when a trigger fires while an instance is running, not on manual invocation. Needs
   further research to confirm whether there's an app-side workaround.
3. **Tray icon running-task badge is unreliable / unreadable.** Two separate causes, both pre-existing
   (`TrayIconService.UpdateBadge` and its only caller, `MainViewModel.LoadTasksAsync`, are unchanged by
   the refactor): (a) the badge is only recomputed when the task list reloads (page load, Refresh, F5),
   not when a task is started/stopped/finishes, so it stays stale after clicking Run; (b) the badge is
   drawn on a 32px bitmap with a 14px circle and ~7.5pt number, which Windows downscales to 16px in the
   tray, making the digit an illegible blob. There is also no taskbar-button badge at all, only the tray icon.

# Changelog:

## [V1.8.2] - 2026-05-05

1. Fixed Issues #20 #21 #23
2. Addes #19
