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
| `packaging/` | Manifests for winget, Scoop and Chocolatey, and the script that writes a release into them. |
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

This project uses [Velopack](https://velopack.io/) for installers and auto-updates. `.github/workflows/release.yml` does everything up to a **draft** GitHub Release, which you review and publish.

**To release:**

1. Set `<Version>` in `src/FluentTaskScheduler/FluentTaskScheduler.csproj`. The workflow builds with the version of the tag whatever that file says, but a build on your own machine calls itself what is written there.
2. Write the release notes as `.github/release-notes/V2.X.X.md`, named exactly like the tag. Without that file the draft gets GitHub's list of the merged pull requests instead.
3. Push the tag `V2.X.X`. Or start the workflow by hand ("Run workflow") on the branch you release from, type the version and tick **draft**: publishing that draft creates the tag on the commit the workflow ran on.

The workflow builds both architectures with that version and packs them. Before it makes the draft, it checks on fresh machines that:

- **the installer works the way winget and Chocolatey run it.** The x64 MSI installs silently, the installed app answers `--list`, and uninstalling removes the app. The user's files in `%LOCALAPPDATA%\TRGamer-tech\FluentTaskScheduler` stay. An app that started with Windows no longer does after the uninstall, and does again after a reinstallation: that is how a newer installer replaces this one, and it shows that the app does its part when the installer starts it for a moment.
- **an installed 1.9.0 gets to the new version.** Both update feeds name exactly the package that is attached, with its size and hashes. Then 1.9.0 is installed per user, with settings and a script where it keeps them, and the new package is applied the way its updater does it. The new app has to have the new version and answer `--list`, the files have to be in the new place and nothing of the installer with them, the script has to be in the old place as well (tasks created with 1.9.0 run it from there), and uninstalling has to leave the files in the new place.

Only if all of that works does it create the draft and attach the files. It names the draft like the tag; give it its title there.

The notes of the draft end with a table, "Artifacts and Hashes", of the SHA-256 of the four downloads. The workflow writes it from the very files it attaches, so leave it out of your notes file.

> **Never edit that table by hand, and never replace a file of a release by hand.** That is how the V1.9.0 notes came to advertise hashes that matched none of the downloads. Change what has to change in the repository and run the workflow again for the same version: as long as the release is a draft, it replaces every file and rewrites the notes and the table, and keeps the title. A published release it refuses to touch. Its files are what people have downloaded and what the package managers have hashed, so a fix for it is a new version.

The draft contains, and has to keep, exactly these files:

| File | What it is for |
|------|----------------|
| `Setup-x64.msi`, `Setup-arm64.msi` | The installers. The user chooses between "per user" and "machine-wide" when installing. |
| `Portable-x64.zip`, `Portable-arm64.zip` | The app without an installer. |
| `releases.win-x64.json`, `releases.win-arm64.json` | Tell an installed app that there is an update. |
| `FluentTaskScheduler-2.X.X-win-x64-full.nupkg`, `...-win-arm64-full.nupkg` | What an installed app downloads to update itself. |

Without the last two kinds, auto-update silently does nothing. The names matter too: the package managers and the README link to the installers and zips by name, and the updater looks for the other files under the names Velopack gave them.

**One thing is left to try by hand after publishing.** An installed app only finds releases that are published and not marked as a pre-release, so no check before that can make 1.9.0 download anything. Install 1.9.0, let it find the update, and see it arrive. For the same reason, publish as a normal release: the updater passes over a pre-release.

What else the checks leave open: they install and update the x64 files only, and of the ARM64 files they compare the feed with its package. And they take the way of the app's own updater. Installing the new MSI over an older version, which is what winget and Chocolatey do for an upgrade, is not gone through.

To try the packaging out without releasing anything, start the workflow by hand and leave **draft** unticked. It runs the same checks and keeps the files as artifacts of the run.

Two things the workflow settles that are easy to get wrong by hand:

- It pins the version of Velopack's command line tool (`VPK_VERSION` in the workflow). Change it together with the `Velopack` package the app references.
- It builds both architectures on an x64 machine. The tools Velopack uses to build the MSI are x64 programs and do not start on ARM64.

---

## Publishing to package managers

The manifests for winget, Scoop and Chocolatey live in the `packaging/` folder. Each names the version, links to the release and carries the SHA-256 of what it downloads. winget also wants the product code of each installer, and every build of an MSI gets a new one.

**None of that is typed in by hand.** `packaging/Update-Manifests.ps1` puts all of it into the manifests and leaves the rest of every file as it is. The release workflow runs it on the files it has built, makes no draft if that fails, and keeps the result as the artifact `package-manifests`. So, after publishing a release:

1. Download `package-manifests` from the workflow run that last wrote the draft you published. Every run builds anew, and with that the hashes and product codes change.
2. Put its content over `packaging/`, commit it, and follow the steps below.

The script also runs by hand, on the files of the published release. It needs Windows, because that is where the product codes can be read:

```powershell
gh release download V2.X.X --dir downloads --pattern "Setup-*.msi" --pattern "Portable-*.zip"
.\packaging\Update-Manifests.ps1 -Version 2.X.X -Downloads downloads
```

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

1. Validate locally:

   ```powershell
   winget validate packaging\winget\
   ```

2. Fork [microsoft/winget-pkgs](https://github.com/microsoft/winget-pkgs) and copy the three files to:

   ```
   manifests/t/TRGamer-tech/FluentTaskScheduler/<version>/
   ```

3. Open a Pull Request. Automated validation runs; once it passes a maintainer will merge it.

---

### Scoop

**Manifest location:** `packaging/scoop/fluenttaskscheduler.json`

Scoop uses the **portable zips** (no installer required).

**Submission steps:**

1. Test locally:

   ```powershell
   scoop install packaging\scoop\fluenttaskscheduler.json
   ```

2. The published manifest lives in [ScoopInstaller/Extras](https://github.com/ScoopInstaller/Extras) as `bucket/fluenttaskscheduler.json`. Its `checkver` and `autoupdate` sections let the bucket pick up a new release by itself, with new URLs and hashes. Anything else that changes in the manifest here needs a pull request to that bucket.

> **`bin` names the app in `current`, not the `FluentTaskScheduler.exe` at the top of the zip.** That one is Velopack's launcher: it starts the app and returns at once with its own exit code, so a command such as `FluentTaskScheduler --list` through the Scoop shim would not wait for the answer and would always report success. The shortcut keeps pointing at the launcher, which is the right thing to start the window with.

---

### Chocolatey

**Package location:** `packaging/chocolatey/`

| File | Purpose |
|------|---------|
| `fluenttaskscheduler.nuspec` | Package metadata |
| `tools/chocolateyInstall.ps1` | Silent install script |

The install script picks the x64 or the ARM64 installer itself, so it carries the hashes of both.

**Submission steps:**

1. Build and test the package locally:

   ```powershell
   cd packaging\chocolatey
   choco pack                           # creates fluenttaskscheduler.<version>.nupkg
   choco install fluenttaskscheduler --source . -y   # local install test
   choco uninstall fluenttaskscheduler -y            # cleanup
   ```

2. Submit to the [Chocolatey Community Repository](https://community.chocolatey.org/packages/upload):

   ```powershell
   choco push fluenttaskscheduler.<version>.nupkg --source https://push.chocolatey.org/ --api-key <YOUR_API_KEY>
   ```

   The package enters a moderation queue. Automated virus scanning and a human review must pass before it goes live (usually 1 to 3 business days).
