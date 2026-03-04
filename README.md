# FluentTaskScheduler

A modern, powerful, and intuitive Windows task scheduling application built with WinUI 3 and .NET 8.

![App Icon](Assets/Logo.png)

## Overview

FluentTaskScheduler is a professional-grade wrapper for the Windows Task Scheduler API, designed with Microsoft's modern Fluent Design System. It simplifies the creation and management of automation tasks, offering a sleek alternative to the legacy Windows Task Scheduler.

## Key Features

### 🕹️ Dashboard & Monitoring

- **Activity Stream**: A live feed of task activity. Click any entry to jump directly to the task details.
- **Task History**: Comprehensive history of all task executions, keeping you informed of every run.
- **Visual Analytics**: A dynamic task run history chart on the dashboard showing successes vs. failures over time.
- **Animated Status**: Clearly identify running tasks at a glance with animated status indicators.

### 🕒 Comprehensive Triggers

- **Time-Based**:
  - **One Time**: Run once at a specific date and time.
  - **Daily**: Recur every X days.
  - **Weekly**: Select specific days of the week (Mon-Sun).
  - **Monthly**: Schedule on specific dates (e.g., 1st, 15th) or relative patterns (e.g., First Monday of the month).
- **System Events**:
  - **At Logon**: Trigger when a user logs in.
  - **At Startup**: Trigger when the system boots.
  - **On Event**: Trigger based on specific Windows Event Log entries (Log, Source, Event ID).
  - **Session State Change**: Trigger on Lock, Unlock, Remote Connect, or Remote Disconnect.
- **Advanced Options**:
  - **Random Delay**: Add a random delay to execution times to prevent thundering herds.
  - **Expiration**: Set task expiration dates.
  - **Stop After**: Automatically stop tasks if they run longer than a specified duration.

### 🔄 Advanced Repetition

- Configure tasks to repeat every few minutes or hours.
- Set a duration for the repetition pattern (e.g., repeat every 15 minutes for 12 hours).

### 📜 Script Library

- **Centralized Management**: A dedicated space for pr-written PowerShell scripts, separating logic from task configuration.
- **Reusable Code**: Use scripts in multiple tasks.
- **Custom Templates**: Save and reuse your own task templates, going beyond just the pre-built scripts.

### 🛡️ Actions & Conditions

- **Actions**:
  - Run programs or scripts.
  - Specialized support for **PowerShell** scripts with execution policy bypass tips.
  - Custom working directories and arguments.
- **Conditions**:
  - **Idle**: Start only if the computer has been idle.
  - **Power**: Start only if on AC power; stop if switched to battery.
  - **Network**: Start only if a network connection is available.
  - **Wake to Run**: enhance reliability by waking the computer to execute the task.

### 🎨 Customization

- **Themes**: Standard Dark Mode and specialized **OLED Mode** (Pure Black) for display longevity.
- **Languages**: Native English (en-US) support.
- **Smooth Scrolling**: Optional smooth/inertia scrolling throughout the app, disabled by default for a snappier feel.
- **Window Size Memory**: The app remembers your last window size and restores it on next launch.

### 🧬 System Integration

- **System Tray**: Minimize the app to the tray to keep your taskbar clean while the scheduler hums in the background. Disabled by default.
- **Tray Badge**: See the number of currently running tasks at a glance directly on the tray icon.
- **Multi-Window Tray Management**: Open multiple windows and manage them independently from the tray right-click menu — restore or close individual windows, or open a new one, all from a single tray icon.
- **Tray Notification**: A toast notification appears the first time the app is minimized to tray, with a click action to restore the window instantly.
- **Single Instance**: Launching the app a second time brings the existing window to the front instead of opening a duplicate.
- **Run on Startup**: Option to launch automatically with Windows.
- **Notifications**: Get native toast notifications when tasks complete or fail.
- **Onboarding Walkthrough**: Step-by-step onboarding for first launch to guide new users through key features.
- **Changelog Popup**: A "What's New" popup highlighting application updates the first time you run a new version.

### ⚙️ Robust Settings

- **Privileges**: Run tasks with highest privileges (Admin) or as System/Specific User.
- **Priority**: Configurable task priority (Realtime to Idle).
- **Concurrency**: Define behavior for multiple instances (Parallel, Queue, Ignore New, Stop Existing).
- **Fail-Safe**:
  - **Restart on Failure**: Automatically attempt to restart failed tasks up to a configured limit.
  - **Run if Missed**: Execute the task as soon as possible if a scheduled start was missed (e.g., computer was off).
- **Settings Backup**: Backup and restore your application settings to ensure your configuration is safe.

### 📊 Management & History

- **Task History**: View recent task execution history within the app (Today, Yesterday, This Week, All Time).
- **Folder Management**: Organize your tasks logically by creating, renaming, and deleting custom folders.
- **Smart Navigation**: The application remembers your last-used folder on restart.
- **Sortable Lists**: Sort tasks easily by clicking column headers directly in the task list (by name, status, next run, etc.).
- **Search & Filter**: Instantly find tasks by name, status, or path.
- **Import/Export**: Easily backup or migrate task definitions, including CSV export for history.
- **Batch Operations**: Select and manage multiple tasks simultaneously.
- **CLI Support**: Full command-line interface for automation and headless management.

### ⌨️ Keyboard Shortcuts

| Shortcut   | Action                          |
| :--------- | :------------------------------ |
| `Ctrl + N` | New Task                        |
| `Ctrl + E` | Edit Selected Task              |
| `Ctrl + R` | Run Selected Task               |
| `Delete`   | Delete Selected Task            |
| `F5`       | Refresh Task List               |
| `Esc`      | Close Dialogs / Clear Selection |
| `?` / `F1` | Keyboard Shortcut Cheat Sheet   |

## 💻 CLI Reference

FluentTaskScheduler supports command-line arguments for integration with scripts and external tools.

```powershell
# List all tasks as JSON
FluentTaskScheduler.exe --list

# Run a specific task
FluentTaskScheduler.exe --run "MyTaskName"

# Enable or Disable a task
FluentTaskScheduler.exe --enable "MyTaskName"
FluentTaskScheduler.exe --disable "MyTaskName"

# Export task history to CSV
FluentTaskScheduler.exe --export-history "MyTaskName" --output "C:\logs\history.csv"
```

## Technology Stack

- **Framework**: [.NET 8](https://dotnet.microsoft.com/download/dotnet/8.0)
- **UI Architecture**: [WinUI 3](https://learn.microsoft.com/en-us/windows/apps/winui/winui3/) (Windows App SDK)
- **Core Logic**: [TaskScheduler Managed Wrapper](https://github.com/dahall/TaskScheduler)
- **Language**: C#

## Building from Source

1. **Prerequisites**:
   - Visual Studio 2022 (17.8 or later) with "Windows application development" workload.
   - .NET 8 SDK.

2. **Clone & Build**:

   ```bash
   git clone https://github.com/TRGamer-tech/FluentTaskScheduler.git
   cd FluentTaskScheduler
   dotnet build -c Release
   ```

## 🛠️ Troubleshooting

- **Crash Logs**: If the application encounters a critical error, a `crash_log.txt` file is generated in the application directory.
- **Admin Rights**: Some features (like "Run as SYSTEM") require the application to be run as Administrator.
- **Preferences storage**: The application stores its preferences as well as the log and custom script files in a JSON file in "%localappdata%\FluentTaskScheduler".

3. **Single File Deployment**:
   The project supports publishing as a single, self-contained executable for easy distribution.

## Star History

<a href="https://www.star-history.com/#TRGamer-tech/FluentTaskScheduler&type=date&legend=top-left">
 <picture>
   <source media="(prefers-color-scheme: dark)" srcset="https://api.star-history.com/svg?repos=TRGamer-tech/FluentTaskScheduler&type=date&theme=dark&legend=top-left" />
   <source media="(prefers-color-scheme: light)" srcset="https://api.star-history.com/svg?repos=TRGamer-tech/FluentTaskScheduler&type=date&legend=top-left" />
   <img alt="Star History Chart" src="https://api.star-history.com/svg?repos=TRGamer-tech/FluentTaskScheduler&type=date&legend=top-left" />
 </picture>
</a>

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.
