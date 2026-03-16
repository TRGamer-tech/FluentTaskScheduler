# To Do:

1. Add Setup so you don't have to make a Shortcut yourself
2. Add update functionality to installed versions (not standalone)
3. Fix [Issue #5](https://github.com/TRGamer-tech/FluentTaskScheduler/issues/5): v1.60 ARM 64 single exe crashes on startup
   > info: Application: FluentTaskScheduler-arm64.exe
   > CoreCLR Version: 8.0.2526.11203
   > .NET Version: 8.0.25
   > Description: The process was terminated due to an unhandled exception.
   > Exception Info: System.DllNotFoundException: Dll was not found.
   > at FluentTaskScheduler.Program.XamlCheckProcessRequirements()
   > at FluentTaskScheduler.Program.Main(String[] args)
   > The folder version doesn't crash so there is no "crash_log.txt". But here's some extra information If I place "FluentTaskScheduler-arm64.exe" inside the unzipped folder version it works, so whatever .dll it's missing it finds in the folder version.
4. Fix [Issue #4](https://github.com/TRGamer-tech/FluentTaskScheduler/issues/4): Hidden Tasks are not displayed
5. **_More TBD_**

# Done for next release:

1. Fix "Access Denied" errors on specific tasks
2. Add Error Logging on Access Denied or similar (not the same as crash log)
3. Fix All Tasks not being displayed on App Start -> Disable "Remembering last selected folder" for now.
4. Let's see what we can do to make it even better.
