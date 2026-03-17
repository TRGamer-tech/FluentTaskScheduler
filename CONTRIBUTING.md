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

2. Publish and package — **one MSI per architecture**, scope chosen by the end user at install time.
   Requires vpk **0.0.1444-gc245055** or later (`dotnet tool update -g vpk` or `dotnet tool update -g vpk --prerelease` to upgrade).

   ```bash
   # x64
   dotnet publish -c Release -r win-x64 --self-contained -p:Platform=x64
   vpk pack -u FluentTaskScheduler -v 1.X.X -o Releases/x64 -p bin/x64/Release/net8.0-windows10.0.19041.0/win-x64/publish -e FluentTaskScheduler.exe --msi --instLocation Either --msiBanner Assets/MSI-Banner.bmp --msiLogo Assets/MSI-Logo.bmp

   # ARM64
   dotnet publish -c Release -r win-arm64 --self-contained -p:Platform=ARM64
   vpk pack -u FluentTaskScheduler -v 1.X.X -o Releases/arm64 -p bin/arm64/Release/net8.0-windows10.0.19041.0/win-arm64/publish -e FluentTaskScheduler.exe --msi --instLocation Either --msiBanner Assets/MSI-Banner.bmp --msiLogo Assets/MSI-Logo.bmp

   # Copy to dist folder -> Making it ready for release
   Copy-Item -Path "Releases/x64/FluentTaskScheduler-win-Portable.zip" -Destination "Dist/Portable-x64.zip" -Force;
   Copy-Item -Path "Releases/x64/FluentTaskScheduler-win.msi" -Destination "Dist/Setup-x64.msi" -Force;
   Copy-Item -Path "Releases/arm64/FluentTaskScheduler-win-Portable.zip" -Destination "Dist/Portable-arm64.zip" -Force;
   Copy-Item -Path "Releases/arm64/FluentTaskScheduler-win.msi" -Destination "Dist/Setup-arm64.msi" -Force;
   ```

   The resulting `Setup-x64.msi` and `Setup-arm64.msi` presents a standard MSI UI where the user selects _Per User_ or _Machine-Wide_ installation.

3. Upload `Setup-x64.msi`, `Setup-arm64.msi`, `Portable-x64.zip`, `Portable-arm64.zip` from the Dist folder to a GitHub Release. The app will pick these up automatically for in-app updates.

## Bug Reports and Feature Requests

Please use the provided GitHub Issue templates. Fill them out completely. We cannot fix a bug if your entire report is "it crashed".
