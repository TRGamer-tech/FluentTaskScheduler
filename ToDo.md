# To Do:

1. Add Setup so you don't have to make a Shortcut yourself
2. Add update functionality to installed versions (not standalone)
4. Fix [Issue #4](https://github.com/TRGamer-tech/FluentTaskScheduler/issues/4): Hidden Tasks are not displayed
5. Fix [Issue #6](https://github.com/TRGamer-tech/FluentTaskScheduler/issues/6): Unhandled exception on 'Add or edit task' add tag, save
   > [2026-03-16 07:45:44] [CRASH] [Xaml.UnhandledException] COMException: An async operation was not properly started.
   > Only a single ContentDialog can be open at any time.
   > Stack Trace:    at WinRT.ExceptionHelpers.<ThrowExceptionForHR>g__Throw|38_0(Int32 hr)
   > at ABI.Microsoft.UI.Xaml.Controls.IContentDialogMethods.ShowAsync(IObjectReference _obj)
   > at Microsoft.UI.Xaml.Controls.ContentDialog.ShowAsync()
   > at FluentTaskScheduler.App.<>c__DisplayClass24_0.<<LogCrash>b__0>d.MoveNext()
   > --- End of stack trace from previous location ---
   > at System.Threading.Tasks.Task.<>c.<ThrowAsync>b__128_0(Object state)
   > at Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext.<>c__DisplayClass2_0.<Post>b__0()
   >
   > [2026-03-16 08:21:22] [CRASH] [Xaml.UnhandledException] COMException: An async operation was not properly started.
   > {"An async operation was not properly started.\r\n\r\nOnly a single ContentDialog can be open at any time."}
   > // Keep process alive briefly?
   > System.Threading.Thread.Sleep(5000); 
   > [2026-03-16 08:21:27] [CRASH] [Xaml.UnhandledException] COMException: An async operation was not properly started.
   > [2026-03-16 08:21:32] [CRASH] [Xaml.UnhandledException] COMException: An async operation was not properly started.
   > [2026-03-16 08:21:37] [CRASH] [Xaml.UnhandledException] COMException: An async operation was not properly started.
6. **_More TBD_**

# Done for next release:

1. Fix "Access Denied" errors on specific tasks
2. Add Error Logging on Access Denied or similar (not the same as crash log)
3. Fix All Tasks not being displayed on App Start -> Disable "Remembering last selected folder" for now.
4. Fix [Issue #5](https://github.com/TRGamer-tech/FluentTaskScheduler/issues/5): v1.60 ARM 64 single exe crashes on startup
5. Let's see what we can do to make it even better.
