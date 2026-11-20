## Installing

> [!IMPORTANT]
> **These builds are not code-signed, so Windows blocks the first launch.** This is expected and does not mean the
> download is damaged. The steps to allow S3Drive are below. To confirm a file is the one published here, check it
> against `SHA256SUMS`.
>
> S3Drive is free, open-source software and is not code-signed (signing certificates are an ongoing cost we choose
> not to pass on).

### Windows: "Windows protected your PC"

When you run the installer, Windows shows *Windows protected your PC. Microsoft Defender SmartScreen prevented an
unrecognized app from starting.* with only a **Don't run** button. Click **More info**, then **Run anyway**.

If your browser warns that the file *isn't commonly downloaded*, choose **Keep**. In Edge, open the **...** menu on
the download, choose **Keep**, then **Show more > Keep anyway**.

For the `.zip`, unblock it before extracting, or every program inside it is blocked too: right-click the file >
**Properties** > **Unblock**, or in PowerShell:

```powershell
Unblock-File .\s3drive-{version}-win-x64.zip
```

### Before you install: Dokany

S3Drive mounts drives through the [Dokany](https://github.com/dokan-dev/dokany/releases) 2 file system driver, a
separate free install. Install it first, or Setup reminds you and offers the download page.

### Which file to download

| Platform | File |
| --- | --- |
| Windows (x64, most PCs) | `s3drive-{version}-win-x64-setup.exe` |
| Windows on ARM | `s3drive-{version}-win-arm64-setup.exe` |
| Portable, scripts, and automation | `s3drive-{version}-win-x64.zip` / `s3drive-{version}-win-arm64.zip` |

Not sure which you have? **Settings > System > About > System type** says "x64-based processor" or "ARM-based
processor".

Setup installs S3Drive to Program Files, adds the `s3drive` command to PATH, adds Start menu shortcuts, and starts
the S3Drive Agent in the system tray (and, if you leave the box checked, at every sign-in). Open a new terminal and
run `s3drive`, or choose **S3Drive** from the Start menu.

Full instructions: [INSTALL.md](https://github.com/jchristn/S3Drive/blob/main/INSTALL.md). To build from source, use
the **Source code** archives below and follow
[INSTALL.md: Building from source](https://github.com/jchristn/S3Drive/blob/main/INSTALL.md#building-from-source).
