# Contributing to FluentTaskScheduler

First off, thank you for considering contributing to FluentTaskScheduler! It is people like you that make this tool actually useful.

Before you submit a pull request or open an issue asking why the project will not compile, please review the following guidelines.

## Prerequisites

If you want to build this project from source, you cannot just use Notepad. You will need:

- **Visual Studio 2022** (17.8 or later)
- **.NET 8 SDK**
- **The "Windows application development" workload** installed in Visual Studio. (If you skip this, WinUI 3 will not work, and the build will fail. You have been warned.)

## How to Contribute

1. **Fork the repository** to your own GitHub account.
2. **Clone the project** to your local machine.
3. **Create a new branch** for your feature or bug fix (`git checkout -b feature/my-brilliant-idea`).
4. **Write your code** and make sure it actually compiles.
5. **Test your changes** locally. If you add a feature that runs tasks as SYSTEM, please make sure it does not destroy your own OS first.
6. **Commit your changes** with clear, descriptive commit messages. "Fixed stuff" is not a descriptive message.
7. **Push to your fork** and submit a Pull Request against the `main` branch.

## Code Style

Please try to match the existing code style. We use C# and WinUI 3. Keep things clean, use meaningful variable names, and leave comments if you are doing something overly complicated.

## Publishing a Release with VeloPack

This project uses [VeloPack](https://velopack.io/) for auto-updates. If you are a maintainer creating a release, here is the workflow:

1. Install the VeloPack CLI (one-time):

   ```bash
   dotnet tool install -g vpk
   ```

2. Publish and package:

   ```bash
   # x64
   dotnet publish -c Release -r win-x64 --self-contained
   vpk pack -u FluentTaskScheduler -v <VERSION> -p bin/x64/Release/net8.0-windows10.0.19041.0/win-x64/publish -e FluentTaskScheduler.exe

   # ARM64
   dotnet publish -c Release -r win-arm64 --self-contained
   vpk pack -u FluentTaskScheduler -v <VERSION> -p bin/ARM64/Release/net8.0-windows10.0.19041.0/win-arm64/publish -e FluentTaskScheduler.exe
   ```

3. Upload the generated `Setup.exe`, full `.nupkg`, and delta `.nupkg` from the `Releases` folder to a GitHub Release. The app will pick these up automatically for in-app updates.

## Bug Reports and Feature Requests

Please use the provided GitHub Issue templates. Fill them out completely. We cannot fix a bug if your entire report is "it crashed".
