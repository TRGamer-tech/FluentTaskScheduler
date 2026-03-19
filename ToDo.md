# To Do:

1. Add Feature #11:
    > Is your feature request related to a problem? Please describe.
    > Yes. A major reason I use task scheduling software is to automatically run scripts, bots, and other background tasks. Windows Task Scheduler is functional, but it is not very good for quickly understanding the current state of those tasks at a glance.
    > FluentTaskScheduler already feels like a cleaner and more usable interface, but it seems closer to a UI refresh for Task Scheduler. What I am missing most is being able to quickly open the app and see which tasks are active, whether they are running, whether they failed, and how they have behaved recently, overall helping with monitoring and managing user-created automation.
    > For example, when I select a task, I cannot easily tell whether the script or process it spawned is still running. I can start or stop the task, but to check its current status I still have to resort to Task Manager.
    > Describe the solution you'd like
    > It would be great if FluentTaskScheduler could gradually offer stronger dashboard or monitoring features for some user-flagged tasks, especially for people using it to manage scripts, bots, and lightweight automation.
    > Some examples of what would be useful:
    > Clear at-a-glance status for tasks, such as running, failed, last run state
    > A clearer distinction between tasks that are simply scheduled and tasks that appear to be working as expected
    > Describe alternatives you've considered
    > So far I have mainly tried organizing tasks with tags and relying on the existing enabled list. Those help with finding tasks, but they do not really solve the problem of quickly seeing all relevant tasks and understanding task health or activity.
2. **_More TBD_**

# Done for next release:

1. Fix Issue #9 by Relocating WinRT ComWrapper initialization
2. Fix Issue #10 by adding a Reload button to the Recent History panel
3. Fix Issue #12 by adding "TaskRunFlags.IgnoreConstraints, 0, null" to the Run Task action.
4. Fix CSV Export feature.
5. Let's see what we can do to make it even better.
