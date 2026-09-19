# To Do:

1. ***More TBD***
2. **Weekly/Monthly triggers skip today.** If today is one of the selected days and the scheduled
   time hasn't passed yet, a Weekly or Monthly trigger should fire today and then continue on its
   normal cycle. Instead it always rolls to the next occurrence, skipping today even when it should
   run. Daily triggers do not have this problem. Pre-existing behavior, not caused by the refactor.
3. **"Queue" / "Stop existing instance" policies don't apply to manual Run.** Manually clicking Run
   while a task is already running always starts a new instance in parallel for these two policies,
   even though "Do not start" and "Parallel" behave correctly. Likely a Windows Task Scheduler
   limitation: `RunEx` (manual run) only honors IgnoreNew/Parallel; Queue/StopExisting are only
   enforced when a trigger fires while an instance is running. Needs research for an app-side workaround.
4. **Tray icon running-task badge is unreliable / unreadable.** Pre-existing: (a) the badge is only
   recomputed when the task list reloads (page load, Refresh, F5), not when a task starts/stops/finishes;
   (b) it is drawn on a 32px bitmap with a 14px circle and ~7.5pt number, which Windows downscales to 16px
   in the tray, making the digit an illegible blob. There is also no taskbar-button badge, only the tray icon.
5. ~~Translate the v1.9.0 additions (Snooze, Pipelines, Dashboard analytics, Library/Templates, onboarding slides) into `de-DE`, `ja-JP`, `zh-CN`.~~ (Done: all 217 v1.9.0 keys translated into all three locales; every `.resw` now has identical key counts.)
6. Submit the updated Scoop / Chocolatey / WinGet manifests once the v1.9.0 GitHub Release is published.
7. ~~**Fix the in-app auto-updater's arch collision before ever uploading `releases.win.json`/`.nupkg` to a GitHub Release.**~~ (Done: Fixed in V1.9.0. Added -c win-x64/win-arm64 at pack time, and pass ExplicitChannel to UpdateOptions).

# Changelog:

## [Unreleased]

## [V1.9.0] - 2026-07-25

### Task Chaining & Execution Pipelines
- New "Completion Actions" section in the task editor: chain downstream tasks to run when a task
  succeeds (exit code 0) or fails (any other code), configured through a dedicated Fluent-styled
  dialog (`TaskPipelineDialog`).
- An event-driven `TaskPipelineService` watches the Task Scheduler operational log (no polling) and
  starts the configured downstream tasks, with cycle detection and a rate limit to prevent runaway
  chains.
- Tasks with a configured pipeline now show a "Chained" badge in the task list.

### Global Snooze / Pause
- "Snooze All Tasks" pauses task execution app-wide for 30m / 1h / 3h / until next reboot / a custom
  time, with an optional "suspend scheduled triggers" mode that disables tasks at the OS level and
  restores them automatically when the snooze ends.
- Snooze status is surfaced via a banner on the task list, a toolbar button next to Sort, and the
  system tray icon/tooltip.

### Dashboard Analytics
- Added a 24-hour execution heatmap, a health-score ring, average/longest run duration, 24h/7d
  throughput, and a filterable live execution log (Success / Failed / Snoozed).
- Replaced the old per-task history polling with a single bulk read of the Task Scheduler event log,
  which is both faster and the basis for the new analytics.
- The Dashboard now fills the full available width instead of being centred inside a fixed 1400px
  column, which left large empty gutters on either side with the sidebar open (#20).
- All figures stay hidden until a load finishes, so partially-populated or zeroed statistics no
  longer flash on screen while the Dashboard is refreshing.

### Library: Task Templates + Scripts, merged
- Merged the separate Script Library and a new Task Template Library into one "Library" page with a
  segmented switcher and a shared search box.
- 17 built-in templates across System Maintenance, Developer Tools, and Power & Utility (temp
  cleanup, restore points, DISM/SFC scan, Git fetch, Docker prune, project backup, idle shutdown,
  and more). Deploying one opens the task editor pre-filled — nothing is registered until you save.

### Localization fixes
- Found and fixed the root cause of the app showing OS-language text regardless of the in-app
  language picker: many dialogs used `x:Uid`, which resolves against the *Windows display language*
  instead of the selected app language. Removed all remaining `x:Uid` usages and routed everything
  through `LocalizationService`.
- `.NET` culture (`CultureInfo`) is now synced to the language picker, so dates, numbers, and
  framework-generated text follow the app language too, not just `.resw` lookups.
- Task History entries are now built from the event's own data fields instead of Windows'
  pre-rendered (OS-language) description, for every event type the app understands.
- The About page version number is read from the assembly at runtime — it can no longer drift out of
  sync with the actual build, and all four locales now carry a translated version format string.

### Settings & UI polish
- Moved Export/Import Settings ("Data") into the Advanced panel.
- Categories & Tags now list vertically at full width instead of a cramped horizontal wrap.
- OLED Mode now follows the *effective* theme (including "System Default" resolving to a dark OS)
  instead of only the stored theme preference, which previously left it greyed out incorrectly.
- Replaced the sidebar's Running/Enabled/Disabled entries with a single status dropdown (now
  including "Snoozed") next to the search box in the task list toolbar.
- CSV history exports now write a UTF-8 BOM so non-ASCII task/user names survive the round trip.

### Onboarding
- Added two new walkthrough slides covering task chaining/snooze and the dashboard/library, and
  refreshed several step icons.

## [Full-app correctness audit]
Ran a systematic audit of every feature in the app after finding the trigger start-time/recurrence
bugs. Found and fixed 26 issues, from silent data loss to dead code:

**Data-loss / silently corrupted existing data:**
1. Script Editor "Save" wiped all saved script templates on every save.
2. Editing an existing task silently cleared `OnlyIfAC` / `OnlyIfNetwork` / `WakeToRun`.
3. Folder rename/move could silently drop a task if copying it failed.
4. "Restart on failure" could be silently dropped on save if the interval failed to parse.

**Features that didn't do what they claimed:**
5. Task expiration date/time was completely unwired.
6. "Stop task if runs longer than" was completely unwired.
7. Idle-trigger duration, Event-trigger fields, and idle Conditions settings were completely unwired.
8. History date filter (Today/Yesterday/This Week/All Time) was a no-op.
9. Dashboard "Run History" chart over-counted failures.
10. Dashboard chart/recent-activity/failed-list silently capped at the 20 most-recently-run tasks.
11. Import Settings didn't actually re-localize the running app.
12. Tray icon was never removed on Exit, leaving a ghost icon.
13. "Task Started"/"Task Failed" toast notifications didn't route back into the app on click.
14. Quick Actions status could be clobbered mid-run by a stale reset timer from a previous run.
15. `sfc`/`DISM` Quick Actions had a stdout pipe-buffer deadlock risk.
16. Update-apply failures were silently swallowed with zero user-facing feedback.
17. Stats-panel "Failed" count and the "Failed" filter disagreed on what counted as a failure.
18. `WhatsNewDialog` could throw if GitHub returned `null` for a release's fields.

**Lower severity (leaks, dead code, minor UX):**
19. `EventLogReader` was never disposed — native handle leak.
20. `Error_Log.txt`/`Crash_Log.txt` had no size cap.
21. Batch Stop didn't clear the spinning-ring state the way single-task Stop does.
22. History detail dialog omitted `ExitCode`.
23. Ctrl+N could throw if pressed while another dialog was already open.
24. Dashboard language-switch edge case could zero out the dashboard for languages missing from a hardcoded string list (e.g. ja-JP) — replaced with a language-independent flag.
25. Removed ~150 lines of dead COM interop code (`NlmInterop.cs`).
26. `SettingsService` load/save failures are now logged instead of silently swallowed.
