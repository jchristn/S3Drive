namespace S3Drive.Agent
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Reflection;
    using Avalonia;
    using Avalonia.Controls;
    using Avalonia.Controls.ApplicationLifetimes;
    using Avalonia.Themes.Fluent;
    using Avalonia.Threading;
    using S3Drive.Core;
    using S3Drive.Core.Configuration;
    using S3Drive.Core.Diagnostics;
    using S3Drive.Core.Ipc;

    /// <summary>
    /// The Avalonia application that hosts the system tray icon and its menu.
    /// </summary>
    internal sealed class App : Application
    {
        private AgentHost? _Host;
        private TrayIcon? _Tray;

        /// <inheritdoc />
        public override void Initialize()
        {
            Styles.Add(new FluentTheme());
        }

        /// <inheritdoc />
        public override void OnFrameworkInitializationCompleted()
        {
            S3DrivePaths paths = new S3DrivePaths();
            _Host = new AgentHost(paths);
            _Host.StatusChanged += OnStatusChanged;

            _Tray = new TrayIcon
            {
                ToolTipText = Constants.ProductName + " - " + Constants.Tagline,
                Icon = LoadIcon(),
                Menu = BuildMenu(_Host.CurrentStatus()),
                IsVisible = true
            };

            _Host.Start();
            base.OnFrameworkInitializationCompleted();
        }

        private void OnStatusChanged(AgentStatus status)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (_Tray != null) _Tray.Menu = BuildMenu(status);
            });
        }

        private NativeMenu BuildMenu(AgentStatus status)
        {
            NativeMenu menu = new NativeMenu();

            NativeMenuItem about = new NativeMenuItem("About");
            about.Click += OnAbout;
            menu.Items.Add(about);

            NativeMenuItem open = new NativeMenuItem("Open S3Drive");
            open.Click += OnOpen;
            menu.Items.Add(open);

            menu.Items.Add(BuildOpenInExplorerItem(status));

            menu.Items.Add(new NativeMenuItemSeparator());

            if (status.Drives.Count == 0)
            {
                menu.Items.Add(new NativeMenuItem("No drives configured") { IsEnabled = false });
            }
            else
            {
                foreach (DriveStatus drive in status.Drives)
                {
                    menu.Items.Add(BuildDriveItem(drive));
                }
            }

            menu.Items.Add(new NativeMenuItemSeparator());

            NativeMenuItem exit = new NativeMenuItem("Exit");
            exit.Click += OnExit;
            menu.Items.Add(exit);
            return menu;
        }

        private NativeMenuItem BuildOpenInExplorerItem(AgentStatus status)
        {
            List<DriveStatus> openable = new List<DriveStatus>();
            foreach (DriveStatus drive in status.Drives)
            {
                if (drive.MountState == DriveMountStateEnum.Mounted && FormatLetter(drive.DriveLetter).Length > 0)
                {
                    openable.Add(drive);
                }
            }

            NativeMenuItem item = new NativeMenuItem("Open in Explorer");

            if (openable.Count == 0)
            {
                item.IsEnabled = false;
                return item;
            }

            if (openable.Count == 1)
            {
                string path = ExplorerPath(openable[0].DriveLetter);
                item.Click += (sender, args) => OpenInExplorer(path);
                return item;
            }

            NativeMenu sub = new NativeMenu();
            foreach (DriveStatus drive in openable)
            {
                string letter = FormatLetter(drive.DriveLetter);
                string label = letter + "  " + drive.Name;
                string path = ExplorerPath(drive.DriveLetter);
                NativeMenuItem driveItem = new NativeMenuItem(label);
                driveItem.Click += (sender, args) => OpenInExplorer(path);
                sub.Items.Add(driveItem);
            }

            item.Menu = sub;
            return item;
        }

        private NativeMenuItem BuildDriveItem(DriveStatus drive)
        {
            string letter = FormatLetter(drive.DriveLetter);
            string label = (letter.Length > 0 ? letter + "  " : string.Empty) + drive.Name + "  [" + drive.MountState + "]";
            NativeMenuItem item = new NativeMenuItem(label);

            NativeMenu sub = new NativeMenu();
            string id = drive.DriveId;
            bool mounted = drive.MountState == DriveMountStateEnum.Mounted;

            NativeMenuItem mount = new NativeMenuItem(mounted ? "Unmount" : "Mount");
            mount.Click += (sender, args) =>
            {
                if (mounted) _ = _Host!.UnmountAsync(id);
                else _ = _Host!.MountAsync(id);
            };
            sub.Items.Add(mount);

            item.Menu = sub;
            return item;
        }

        private void OnAbout(object? sender, EventArgs e)
        {
            AboutWindow window = new AboutWindow();
            window.Show();
        }

        private void OnOpen(object? sender, EventArgs e)
        {
            _Host?.LaunchTui();
        }

        private void OnExit(object? sender, EventArgs e)
        {
            _Host?.Stop();
            if (_Tray != null) _Tray.IsVisible = false;
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) desktop.Shutdown();
        }

        private static string ExplorerPath(string? driveLetter)
        {
            string letter = FormatLetter(driveLetter);
            return letter + "\\";
        }

        private static void OpenInExplorer(string path)
        {
            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe", "\"" + path + "\"") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                S3DriveLog.Error("Failed to open " + path + " in Explorer: " + ex.Message);
            }
        }

        private static string FormatLetter(string? driveLetter)
        {
            if (string.IsNullOrEmpty(driveLetter)) return string.Empty;

            string trimmed = driveLetter.TrimEnd('\\', ' ');
            if (trimmed.Length == 0) return string.Empty;

            char letter = char.ToUpperInvariant(trimmed[0]);
            return letter + ":";
        }

        private static WindowIcon? LoadIcon()
        {
            try
            {
                Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("S3Drive.Agent.logo.ico");
                if (stream == null) return null;
                using (stream)
                {
                    return new WindowIcon(stream);
                }
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
