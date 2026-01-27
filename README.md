# FluentTaskScheduler

A modern, powerful, and intuitive Windows task scheduling application built with WinUI 3 and .NET 8.

![App Icon](Assets/Logo.png)

## Overview

FluentTaskScheduler is a professional-grade wrapper for the Windows Task Scheduler API, designed with Microsoft's modern Fluent Design System. It simplifies the creation and management of automation tasks, offering a sleek alternative to the legacy Windows Task Scheduler.

## Key Features

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
- **Advanced Options**:
  - **Random Delay**: Add a random delay to execution times to prevent thundering herds.
  - **Expiration**: Set task expiration dates.
  - **Stop After**: Automatically stop tasks if they run longer than a specified duration.

### 🔄 Advanced Repetition

- Configure tasks to repeat every few minutes or hours.
- Set a duration for the repetition pattern (e.g., repeat every 15 minutes for 12 hours).

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

### ⚙️ Robust Settings

- **Privileges**: Run tasks with highest privileges (Admin).
- **Fail-Safe**:
  - **Restart on Failure**: Automatically attempt to restart failed tasks up to a configured limit.
  - **Run if Missed**: Execute the task as soon as possible if a scheduled start was missed (e.g., computer was off).

### 📊 Management & History

- **Task History**: View recent task execution history within the app (Today, Yesterday, This Week, All Time).
- **Search & Filter**: Instantly find tasks by name, status, or path.
- **XML Editing**: Direct access to the underlying Task XML for advanced configuration.
- **Import/Export**: Easily backup or migrate task definitions.

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

3. **Single File Deployment**:
   The project supports publishing as a single, self-contained executable for easy distribution.

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.
