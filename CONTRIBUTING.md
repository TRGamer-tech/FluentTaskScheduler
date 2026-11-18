# Contributing to FluentTaskScheduler

First off, thank you for considering contributing to FluentTaskScheduler! It is people like you that make this tool actually useful.

Before you submit a pull request or open an issue asking why the project will not compile, please review the following guidelines.

## What you need

- **The .NET 10 SDK.** That is all it takes to build the app and run the tests from the command line. `global.json` keeps the build on it when a newer SDK is installed as well, since the libraries treat warnings as errors and every SDK brings new ones.
- **Windows 10 1809 or later** to run the app and the tests that need it. The larger part of the tests runs on any system the SDK runs on, a Mac included.
- **Visual Studio 2026** with the "WinUI application development" workload, if you want a debugger and a XAML editor. Open `FluentTaskScheduler.sln`. Visual Studio 2022 cannot build for .NET 10.

## Where things are

| Folder | What lives there |
|--------|------------------|
| `src/FluentTaskScheduler.Core` | Everything that does not need a window: reading and writing tasks, history, snoozing, settings, the command line and the view models. No WinUI, so it builds and is tested anywhere. |
| `src/FluentTaskScheduler` | The app: window, pages, dialogs, controls, and what talks to Windows on behalf of the UI (notification area icon, notifications, file dialogs). |
| `tests/FluentTaskScheduler.Tests` | Tests of Core. Most are plain unit tests. Those that register tasks in the real Task Scheduler or read the event log are skipped where that is not possible. |
| `tests/FluentTaskScheduler.UITests` | Tests that start the built app and drive it through UI Automation. |
| `packaging/` | Manifests for winget, Scoop and Chocolatey. |
| `Assets/` | Images for the README and the installer. The app's own assets are in `src/FluentTaskScheduler/Assets`. |

## Building and testing

```powershell
# The tests of Core. On Windows, run them from an elevated terminal to include the ones
# that register tasks for other accounts; everything they create is in folders named FtsTests_*.
dotnet test tests/FluentTaskScheduler.Tests

# The app, the way a release builds it.
dotnet publish src/FluentTaskScheduler -c Release -p:Platform=x64 -o publish

# The UI tests, against that build. They need an elevated terminal and take over mouse and
# keyboard while they run. Whatever they create in Task Scheduler is in a folder, or under
# a name, that begins with FtsUiTests_, with the one exception described below.
$env:FTS_APP_EXE = "$PWD\publish\FluentTaskScheduler.exe"
dotnet test tests/FluentTaskScheduler.UITests
```

Every push to `main` and every pull request runs all of this on Windows for x64 and ARM64 (`.github/workflows/build.yml`). The UI tests leave screenshots of what they saw as an artifact of the run, which is the quickest way to find out why one of them failed.

The screenshots in the README are taken by the UI tests as well, but only on request: when `FTS_README_SCREENSHOTS` is `1`, as it is for the x64 build of the workflow. They need a display at a scale of 100 % with room for a window of 1280 by 800 pixels. For these the tests fill a task folder `\Housekeeping` with tasks that look like a user's own, which is the exception to the rule about names, and remove the folder again afterwards. On a machine that already has a task folder of that name they take no pictures. The pictures are in the folder `readme` of the artifact `ui-artifacts-win-x64`, under the names they have in `Assets/`: look them over, then copy them there.

## How to contribute

1. **Fork the repository** to your own GitHub account.
2. **Clone the project** to your local machine.
3. **Create a new branch** for your feature or bug fix (`git checkout -b feature/my-brilliant-idea`).
4. **Write your code** and make sure it actually compiles.
5. **Test your changes.** If you add a feature that runs tasks as SYSTEM, please make sure it does not destroy your own OS first.
6. **Commit your changes** with messages that say what changes for the user and why. "Fixed stuff" is not a descriptive message.
7. **Push to your fork** and submit a Pull Request against the `main` branch.

## How the code is put together

- **Logic goes into Core, with a test.** A page or dialog binds to a view model and does little else. If you find yourself writing an `if` in a code-behind file, it probably belongs in the view model.
- **Nothing is looked up, everything is handed in.** Classes get what they need through their constructor. `App.xaml.cs` is the one place that decides which services exist.
- **Every text the user sees is in `src/FluentTaskScheduler/Strings/<language>/Resources.resw`, in all four languages.** Code reads it with `Loc.Get("Some.Key")`, XAML with `{x:Bind l:Loc.Get('Some.Key')}`. The dots in a key are a path: next to `Some.Key` there can be no `Some.Key.More`, or the app stops building on Windows. The tests check the four files against each other, so run them after adding texts.
- **A switch, check box or list in a list row is bound both ways to its row**, never handled through an event that looks up "the current row". Lists reuse their rows, and an event raised while a row changes hands ends up changing the wrong item.
- **Formatting and naming** are described in `.editorconfig`; your editor applies most of it by itself.

## Bug Reports and Feature Requests

Please use the provided GitHub Issue templates. Fill them out completely. We cannot fix a bug if your entire report is "it crashed".

## Publishing a release

This project uses [Velopack](https://velopack.io/) for installers and auto-updates.

**To release, push a tag.** Pushing `V1.X.X` runs `.github/workflows/release.yml`. It builds both architectures with the version taken from the tag and packs them. Then it installs the x64 MSI silently, the way winget and Chocolatey do, checks that the installed app answers `--list`, and uninstalls it again; only if that works does it attach everything to a **draft** GitHub Release. Review the draft, write the notes and publish it.

The draft contains, and has to keep, exactly these files:

| File | What it is for |
|------|----------------|
| `Setup-x64.msi`, `Setup-arm64.msi` | The installers. The user chooses between "per user" and "machine-wide" when installing. |
| `Portable-x64.zip`, `Portable-arm64.zip` | The app without an installer. |
| `releases.win-x64.json`, `releases.win-arm64.json` | Tell an installed app that there is an update. |
| `FluentTaskScheduler-1.X.X-win-x64-full.nupkg`, `...-win-arm64-full.nupkg` | What an installed app downloads to update itself. |

Without the last two kinds, auto-update silently does nothing. The names matter too: the package managers and the README link to the installers and zips by name, and the updater looks for the other files under the names Velopack gave them.

To try the packaging out without releasing anything, start the workflow by hand ("Run workflow") and type a version. It then keeps the same files as artifacts of the run and publishes nothing.

Two things the workflow settles that are easy to get wrong by hand:

- It pins the version of Velopack's command line tool (`VPK_VERSION` in the workflow). Change it together with the `Velopack` package the app references.
- It builds both architectures on an x64 machine. The tools Velopack uses to build the MSI are x64 programs and do not start on ARM64.

> **Do not paste a SHA-256 table into the release notes.** GitHub already shows the SHA-256 of every asset next to it, and that value always describes the file that is actually attached. A hand-copied table goes stale the moment an asset is rebuilt and re-uploaded, which is exactly how the V1.9.0 notes ended up advertising hashes that matched none of the downloads.

---

## Publishing to package managers

Ready-made manifests live in the `packaging/` folder. After each GitHub Release, update the **version number** and the **SHA-256 hashes** in these files, then follow the steps below.

### Get the SHA-256 hashes

Take the hashes from the **published release**, not from a local build. A local build is not necessarily the build that was uploaded, and package managers verify what users actually download:

```powershell
gh release view V1.X.X --repo TRGamer-tech/FluentTaskScheduler --json assets --jq '.assets[] | "\(.digest)  \(.name)"'
```

Each line reads `sha256:<hash>  <file name>`; the manifests want the hash without the `sha256:` prefix. The same values are shown next to each asset on the release page.

---

### winget (Windows Package Manager)

**Manifests location:** `packaging/winget/`

The three files follow the [winget multi-file manifest schema 1.12](https://github.com/microsoft/winget-pkgs):

| File | Purpose |
|------|---------|
| `TRGamer-tech.FluentTaskScheduler.yaml` | Version manifest |
| `TRGamer-tech.FluentTaskScheduler.locale.en-US.yaml` | Localised metadata |
| `TRGamer-tech.FluentTaskScheduler.installer.yaml` | Installer URLs, hashes and product codes |

**Submission steps:**

1. Update `PackageVersion` in all three files to the new version.
2. In the **installer manifest**, update both `InstallerUrl` values to the new release tag and both `InstallerSha256` values to the new hashes.
3. Update both `ProductCode` values. Every build of an MSI gets a new one, and x64 and ARM64 differ. To read it from a downloaded installer:

   ```powershell
   $installer = New-Object -ComObject WindowsInstaller.Installer
   $database = $installer.OpenDatabase("$PWD\Setup-x64.msi", 0)
   $view = $database.OpenView("SELECT Value FROM Property WHERE Property = 'ProductCode'")
   $view.Execute(); $view.Fetch().StringData(1)
   ```

4. Validate locally:

   ```powershell
   winget validate packaging\winget\
   ```

5. Fork [microsoft/winget-pkgs](https://github.com/microsoft/winget-pkgs) and copy the three files to:

   ```
   manifests/t/TRGamer-tech/FluentTaskScheduler/<version>/
   ```

6. Open a Pull Request. Automated validation runs; once it passes a maintainer will merge it.

> **Tip:** `wingetcreate update` can do steps 1 to 3:
> ```powershell
> winget install wingetcreate
> wingetcreate update TRGamer-tech.FluentTaskScheduler --version 1.X.X `
>   --urls "https://github.com/TRGamer-tech/FluentTaskScheduler/releases/download/V1.X.X/Setup-x64.msi|x64" `
>          "https://github.com/TRGamer-tech/FluentTaskScheduler/releases/download/V1.X.X/Setup-arm64.msi|arm64" `
>   --submit
> ```

---

### Scoop

**Manifest location:** `packaging/scoop/fluenttaskscheduler.json`

Scoop uses the **portable zips** (no installer required).

**Submission steps:**

1. In `fluenttaskscheduler.json`, update `"version"` to the new version.
2. Update both `"url"` values in the `architecture` block to the new release tag, and both `"hash"` values to the new hashes.
3. Test locally:

   ```powershell
   scoop install packaging\scoop\fluenttaskscheduler.json
   ```

4. The published manifest lives in [ScoopInstaller/Extras](https://github.com/ScoopInstaller/Extras) as `bucket/fluenttaskscheduler.json`. Its `checkver` and `autoupdate` sections let the bucket pick up a new release by itself, with new URLs and hashes. Anything else that changes in the manifest here needs a pull request to that bucket.

> **`bin` names the app in `current`, not the `FluentTaskScheduler.exe` at the top of the zip.** That one is Velopack's launcher: it starts the app and returns at once with its own exit code, so a command such as `FluentTaskScheduler --list` through the Scoop shim would not wait for the answer and would always report success. The shortcut keeps pointing at the launcher, which is the right thing to start the window with.

---

### Chocolatey

**Package location:** `packaging/chocolatey/`

| File | Purpose |
|------|---------|
| `fluenttaskscheduler.nuspec` | Package metadata |
| `tools/chocolateyInstall.ps1` | Silent install script |

The install script picks the x64 or the ARM64 installer itself, so both hashes go into it.

**Submission steps:**

1. Update `<version>` and the `<releaseNotes>` link in `fluenttaskscheduler.nuspec`.
2. In `tools/chocolateyInstall.ps1`, update `$version` and both `$pkgHash` values: the first is the hash of `Setup-x64.msi`, the one inside `if ($isArm64)` that of `Setup-arm64.msi`. The download URLs follow from `$version`.
3. Build and test the package locally:

   ```powershell
   cd packaging\chocolatey
   choco pack                           # creates fluenttaskscheduler.<version>.nupkg
   choco install fluenttaskscheduler --source . -y   # local install test
   choco uninstall fluenttaskscheduler -y            # cleanup
   ```

4. Submit to the [Chocolatey Community Repository](https://community.chocolatey.org/packages/upload):

   ```powershell
   choco push fluenttaskscheduler.<version>.nupkg --source https://push.chocolatey.org/ --api-key <YOUR_API_KEY>
   ```

   The package enters a moderation queue. Automated virus scanning and a human review must pass before it goes live (usually 1 to 3 business days).
