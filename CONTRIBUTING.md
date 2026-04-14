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

## Bug Reports and Feature Requests

Please use the provided GitHub Issue templates. Fill them out completely. We cannot fix a bug if your entire report is "it crashed".

## Publishing a Release with VeloPack

This project uses [VeloPack](https://velopack.io/) for auto-updates. If you are a maintainer creating a release, here is the workflow:

1. Install the VeloPack CLI (one-time):

   ```bash
   dotnet tool install -g vpk
   ```

2. Publish and package — **one MSI per architecture**, scope chosen by the end user at install time.
   Requires vpk **0.0.1444-gc245055** or later (`dotnet tool update -g vpk` or `dotnet tool update -g vpk --prerelease` to upgrade).
   Change 1.X.X to the desired version number.

   ```bash
   # x64
   dotnet publish -c Release -r win-x64 --self-contained -p:Platform=x64
   vpk pack -u FluentTaskScheduler -v 1.X.X -o Releases/x64 -p bin/x64/Release/net8.0-windows10.0.19041.0/win-x64/publish -e FluentTaskScheduler.exe --msi --instLocation Either --msiBanner Assets/MSI-Banner.bmp --msiLogo Assets/MSI-Logo.bmp

   # ARM64
   dotnet publish -c Release -r win-arm64 --self-contained -p:Platform=ARM64
   vpk pack -u FluentTaskScheduler -v 1.X.X -o Releases/arm64 -p bin/arm64/Release/net8.0-windows10.0.19041.0/win-arm64/publish -e FluentTaskScheduler.exe --msi --instLocation Either --msiBanner Assets/MSI-Banner.bmp --msiLogo Assets/MSI-Logo.bmp

   # Fix zero-dated entries in Portable ZIPs (vpk sets all timestamps to 1980-01-01)
   Add-Type -Assembly System.IO.Compression.FileSystem
   $now = Get-Date
   foreach ($zip in @("Releases/x64/FluentTaskScheduler-win-Portable.zip", "Releases/arm64/FluentTaskScheduler-win-Portable.zip")) {
       $archive = [System.IO.Compression.ZipFile]::Open($zip, 'Update')
       foreach ($entry in $archive.Entries) { $entry.LastWriteTime = $now }
       $archive.Dispose()
   }

   # Copy to dist folder -> Making it ready for release
   Copy-Item -Path "Releases/x64/FluentTaskScheduler-win-Portable.zip" -Destination "Dist/Portable-x64.zip" -Force;
   Copy-Item -Path "Releases/x64/FluentTaskScheduler-win.msi" -Destination "Dist/Setup-x64.msi" -Force;
   Copy-Item -Path "Releases/arm64/FluentTaskScheduler-win-Portable.zip" -Destination "Dist/Portable-arm64.zip" -Force;
   Copy-Item -Path "Releases/arm64/FluentTaskScheduler-win.msi" -Destination "Dist/Setup-arm64.msi" -Force;
   ```

   The resulting `Setup-x64.msi` and `Setup-arm64.msi` presents a standard MSI UI where the user selects _Per User_ or _Machine-Wide_ installation.

3. Upload `Setup-x64.msi`, `Setup-arm64.msi`, `Portable-x64.zip`, `Portable-arm64.zip` from the Dist folder to a GitHub Release. The app will pick these up automatically for in-app updates.

---

## Publishing to Package Managers

Ready-made manifests live in the `packaging/` folder. After each GitHub Release you need to update the **version number** and **SHA-256 hashes** in these files, then follow the submission steps below.

### Compute SHA-256 hashes (PowerShell)

```powershell
(Get-FileHash "Dist\Setup-x64.msi"        -Algorithm SHA256).Hash
(Get-FileHash "Dist\Setup-arm64.msi"      -Algorithm SHA256).Hash
(Get-FileHash "Dist\Portable-x64.zip"     -Algorithm SHA256).Hash
(Get-FileHash "Dist\Portable-arm64.zip"   -Algorithm SHA256).Hash
```

---

### winget (Windows Package Manager)

**Manifests location:** `packaging/winget/`

The three required files follow the [winget multi-file manifest schema v1.6](https://github.com/microsoft/winget-pkgs):

| File | Purpose |
|------|---------|
| `TRGamer-tech.FluentTaskScheduler.yaml` | Version manifest |
| `TRGamer-tech.FluentTaskScheduler.locale.en-US.yaml` | Localised metadata |
| `TRGamer-tech.FluentTaskScheduler.installer.yaml` | Installer URLs + hashes |

**Submission steps:**

1. Update `PackageVersion` in all three files to the new version (e.g. `1.8.0`).
2. In the **installer manifest**, replace both `InstallerSha256` placeholders with the real hashes.
3. Update both `InstallerUrl` paths to point to the new GitHub Release tag.
4. Validate locally (**requires winget CLI 1.6+**):

   ```powershell
   winget validate packaging\winget\
   ```

5. Fork [microsoft/winget-pkgs](https://github.com/microsoft/winget-pkgs) and copy the three files to:

   ```
   manifests/t/TRGamer-tech/FluentTaskScheduler/<version>/
   ```

6. Open a Pull Request. Automated validation runs; once it passes a maintainer will merge it.

> **Tip:** `wingetcreate update` can automate steps 1–3:
> ```powershell
> winget install wingetcreate
> wingetcreate update TRGamer-tech.FluentTaskScheduler --version 1.8.0 `
>   --urls "https://github.com/TRGamer-tech/FluentTaskScheduler/releases/download/V1.8.0/Setup-x64.msi|x64|msi" `
>          "https://github.com/TRGamer-tech/FluentTaskScheduler/releases/download/V1.8.0/Setup-arm64.msi|arm64|msi" `
>   --submit
> ```

---

### Scoop

**Manifest location:** `packaging/scoop/fluenttaskscheduler.json`

Scoop uses the **Portable ZIPs** (no installer required).

**Submission steps:**

1. In `fluenttaskscheduler.json`, update `"version"` to the new version string.
2. Replace both `"hash"` placeholders (`<SHA256_OF_Portable-*.zip>`) with the real hashes.
3. Update both `"url"` values in the `architecture` block to the new release tag.
4. Test locally:

   ```powershell
   scoop install packaging\scoop\fluenttaskscheduler.json
   ```

5. **Option A — Submit to** [ScoopInstaller/Extras](https://github.com/ScoopInstaller/Extras) (preferred for non-mainstream apps):
   - Fork the bucket, copy `fluenttaskscheduler.json` to the `bucket/` folder, open a PR.

6. **Option B — Maintain your own bucket** (e.g. `TRGamer-tech/scoop-bucket`):
   - Create a repo, put the JSON in its root.
   - Users add it with:

     ```powershell
     scoop bucket add trgamertech https://github.com/TRGamer-tech/scoop-bucket
     scoop install fluenttaskscheduler
     ```

> **`autoupdate` support:** The manifest already includes `checkver` and `autoupdate` sections. Bucket maintainers can run `scoop checkver fluenttaskscheduler` to detect new releases and auto-generate updated hashes.

---

### Chocolatey

**Package location:** `packaging/chocolatey/`

| File | Purpose |
|------|---------|
| `fluenttaskscheduler.nuspec` | Package metadata |
| `tools/chocolateyInstall.ps1` | Silent install script |

**Submission steps:**

1. Update `<version>` in `fluenttaskscheduler.nuspec` to the new version.
2. Update `$version` and `$checksum64` (with the x64 MSI hash) in `chocolateyInstall.ps1`.
3. Update the `$url64` download URL to point to the new release tag.
4. Build and test the package locally:

   ```powershell
   cd packaging\chocolatey
   choco pack                           # creates fluenttaskscheduler.<version>.nupkg
   choco install fluenttaskscheduler --source . -y   # local install test
   choco uninstall fluenttaskscheduler -y            # cleanup
   ```

5. Submit to the [Chocolatey Community Repository](https://community.chocolatey.org/packages/upload):

   ```powershell
   choco push fluenttaskscheduler.<version>.nupkg --source https://push.chocolatey.org/ --api-key <YOUR_API_KEY>
   ```

   The package enters a moderation queue. Automated virus scanning and a human review must pass before it goes live (usually 1–3 business days).

> **Note:** Chocolatey currently only supports x64 via `Install-ChocolateyPackage`. ARM64 users should install from the MSI directly until native ARM64 package support is mainstream.

