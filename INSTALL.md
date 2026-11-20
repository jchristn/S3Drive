# Installing S3Drive

S3Drive runs on **Windows 10 and 11**, on x64 and ARM64 PCs. Every release on the
[Releases page](https://github.com/jchristn/S3Drive/releases) has a setup program for each architecture, a portable
`.zip`, and a `SHA256SUMS` file. The builds are self-contained: you do not need to install .NET.

S3Drive releases are currently alpha (`0.x`) and are marked **Pre-release** on GitHub.

> [!IMPORTANT]
> **These builds are not code-signed, so Windows blocks the first launch.** This is expected and does not mean the
> download is damaged. The steps to allow S3Drive are below. To confirm a file is the one published here, check it
> against `SHA256SUMS`.
>
> S3Drive is free, open-source software and is not code-signed (signing certificates are an ongoing cost we choose
> not to pass on).

## 1. Install Dokany

S3Drive mounts drives through the [Dokany](https://github.com/dokan-dev/dokany/releases) 2 file system driver, a
separate, independently licensed install (LGPL-2.1 / MIT). Download the latest 2.x installer from its releases page
and run it. S3Drive's setup checks for Dokany and, if it is missing, reminds you and offers the download page; the
install still completes, but drives cannot mount until Dokany is installed.

## 2. Download the right file

| Platform | File |
| --- | --- |
| Windows (x64, most PCs) | `s3drive-<version>-win-x64-setup.exe` |
| Windows on ARM | `s3drive-<version>-win-arm64-setup.exe` |
| Portable, scripts, and automation | `s3drive-<version>-win-x64.zip` / `s3drive-<version>-win-arm64.zip` |

Not sure which you have? **Settings > System > About > System type** says "x64-based processor" or "ARM-based
processor". Each setup program installs only on its own architecture.

## 3. Get past the security prompts

### Windows: "Windows protected your PC"

When you run the installer, Windows shows *Windows protected your PC. Microsoft Defender SmartScreen prevented an
unrecognized app from starting.* with only a **Don't run** button. Click **More info**, then **Run anyway**.

If your browser warns that the file *isn't commonly downloaded*, choose **Keep**. In Edge, open the **...** menu on
the download, choose **Keep**, then **Show more > Keep anyway**.

For the `.zip`, unblock it before extracting, or every program inside it is blocked too: right-click the file >
**Properties** > **Unblock**, or in PowerShell:

```powershell
Unblock-File .\s3drive-<version>-win-x64.zip
```

If Microsoft Defender (not SmartScreen) quarantines a file as malware, that is a false positive that self-extracting
single-file programs sometimes trigger. Please [open an issue](https://github.com/jchristn/S3Drive/issues); do not
turn Defender off.

## 4. Run the installer

The setup program asks you to accept the MIT license, then:

- installs S3Drive to `C:\Program Files\S3Drive` (it needs administrator rights),
- adds the `s3drive` command to the system PATH (a checkbox, on by default),
- adds **S3Drive** and **S3Drive Agent** to the Start menu,
- registers the **S3Drive Agent** to start when anyone signs in to Windows (a checkbox, on by default), and
- offers to start the agent when it finishes. The agent lives in the system tray and owns your mounted drives.

Open a **new** terminal (existing ones do not see the PATH change) and run:

```powershell
s3drive --version
s3drive
```

`s3drive` opens the terminal app, which starts the agent if it is not already running.

For an unattended install, run the setup program from an elevated prompt with
`/VERYSILENT /SUPPRESSMSGBOXES`. It installs with the default choices and does not start the agent until the next
sign-in.

### Upgrading

Run the newer setup program. It stops the running agent and terminal app (mounted drives unmount), replaces the
program files, and offers to start the agent again. Your configuration is kept.

## Portable .zip

The `.zip` holds the same files the installer puts in Program Files: `S3Drive.Tui.exe` (the terminal app),
`S3Drive.Agent.exe` (the tray agent), the `s3drive.cmd` command shim, the icon, and the license. Keep them together
in one folder, since each program finds the other beside it.

```powershell
Unblock-File .\s3drive-<version>-win-x64.zip
Expand-Archive .\s3drive-<version>-win-x64.zip -DestinationPath "$env:LOCALAPPDATA\S3Drive"
& "$env:LOCALAPPDATA\S3Drive\s3drive.cmd"
```

The portable copy does not change PATH or register anything at sign-in. To start the agent at sign-in, put a shortcut
to `S3Drive.Agent.exe` in your Startup folder (`Win+R`, then `shell:startup`).

## Verifying a download

Each release has a `SHA256SUMS` file. In PowerShell, compare a file's hash with its line in `SHA256SUMS`:

```powershell
(Get-FileHash .\s3drive-<version>-win-x64-setup.exe -Algorithm SHA256).Hash.ToLower()
Select-String "s3drive-<version>-win-x64-setup.exe" .\SHA256SUMS
```

With Git Bash or WSL, `sha256sum -c SHA256SUMS --ignore-missing` checks every file you downloaded.

## Uninstalling

Open **Settings > Apps > Installed apps**, find **S3Drive**, and choose **Uninstall**. This stops the agent and the
terminal app, removes the program files, the Start menu shortcuts, the PATH entry, and the sign-in entry.

Your configuration, encrypted keys, logs, and staging cache stay in `%USERPROFILE%\.s3drive` so a reinstall picks up
where you left off. Delete that folder to remove them too. Dokany is a separate program; uninstall it from the same
Settings page if nothing else uses it.

## Package managers

`winget`, Scoop, and Chocolatey packages are **planned and not yet available**. Until they are, install from the
Releases page.

## Building from source

You need Windows 10 or 11, the [.NET 10 SDK](https://dot.net/download) (it also builds the `net8.0` projects), and
Dokany. From the repository root:

```bat
go.bat
```

builds the solution and launches the terminal app, which starts the agent. See the README's
[Getting started](README.md#getting-started) section.

### Building the installers

The installers are built by `S3Drive.Publisher` (`src/S3Drive.Publisher`), which reads `publisher.json` at the
repository root and runs one channel at a time: `publish`, `zip`, `inno`, `checksums`. The scripts run them in order:

```bat
REM Windows: setup .exe, .zip, and SHA256SUMS. Needs Inno Setup 6.3 or later:
REM   choco install innosetup --no-progress -y
build-installers.bat [version]
```

```bash
# macOS or Linux: stages the Windows binaries and builds the .zip archives (the setup .exe needs Windows)
./build-installers.sh [version]
```

Output lands in `installers/<version>/` (git-ignored). The version defaults to `<Version>` in
`Directory.Build.props`. Run a single channel with
`dotnet run --project src/S3Drive.Publisher -c Release -- --channel <name> [--rid win-x64]`, or list them with
`--list-channels`.

### Releasing (maintainers)

Releases are built and published by GitHub Actions, never by hand:

1. Set the version in `Directory.Build.props` (a person decides the version), move the `## [Unreleased]` entries in
   `CHANGELOG.md` into a `## [<version>] - <date>` section, and update the README status line.
2. Commit and push to `main`. Optionally dry-run the release workflow:
   `gh workflow run release.yml --ref main`, then download and install the artifacts.
3. Tag and push: `git tag -a v<version> -m "S3Drive <version>" && git push origin v<version>`.

The tag push runs `.github/workflows/release.yml`: it builds the x64 and ARM64 installers on `windows-latest`,
checks that the tag matches `Directory.Build.props` and that `CHANGELOG.md` has a section for the version, writes
`SHA256SUMS`, and publishes the release titled `S3Drive <version>` with the notes from
`.github/release-install-notes.md` followed by the changelog section. If any build fails, nothing is published.
`0.x` versions are marked Pre-release. `.github/workflows/release-check.yml` runs the same builds, without
publishing, on every push that touches packaging.
