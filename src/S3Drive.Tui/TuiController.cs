namespace S3Drive.Tui
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using S3Drive.Core;
    using S3Drive.Core.Configuration;
    using S3Drive.Core.Diagnostics;
    using S3Drive.Core.Helpers;
    using S3Drive.Core.Ipc;
    using S3Drive.Core.Security;
    using TUIKit;
    using TUIKit.Content;
    using TUIKit.Hosting;
    using TUIKit.Layout;
    using TUIKit.Modals;
    using TUIKit.Theming;
    using TUIKit.Widgets;

    /// <summary>
    /// Builds and drives the TUI: a drive list, an activity log, and keyboard actions that send
    /// commands to the agent and edit the configuration.
    /// </summary>
    internal sealed class TuiController
    {
        private const int HeaderHeight = 2;
        private const int LogHeight = 8;
        private const int HintsHeight = 1;
        private const int FillMax = 1_000_000;

        private readonly S3DrivePaths _Paths;
        private readonly SettingsManager _SettingsManager;
        private readonly CredentialProtector _Protector;

        private readonly Pane _Header = new Pane("header");
        private readonly Pane _Content = new Pane("content");
        private readonly Pane _Log = new Pane("log");
        private readonly Pane _Hints = new Pane("hints");

        private TuiApplication? _App;
        private S3DriveSettings _Settings = new S3DriveSettings();
        private AgentStatus? _Status;

        /// <summary>
        /// Initializes a new controller.
        /// </summary>
        /// <param name="paths">The path resolver.</param>
        public TuiController(S3DrivePaths paths)
        {
            _Paths = paths;
            _SettingsManager = new SettingsManager(paths);
            _Protector = new CredentialProtector(paths.MachineKeyFile);
        }

        /// <summary>
        /// Configures the TUI application: theme, layout, panes, and key bindings.
        /// </summary>
        /// <param name="app">The application.</param>
        public void Configure(TuiApplication app)
        {
            _App = app;
            app.Theme = Theme.Dark;

            app.Layout = Layout.Create()
                .Add("header", region => region
                    .Horizontal(AxisConstraint.Stretch(0, 0, 1, FillMax))
                    .Vertical(AxisConstraint.Fixed(0, HeaderHeight))
                    .WithPadding(0))
                .Add("content", region => region
                    .Horizontal(AxisConstraint.Stretch(0, 0, 1, FillMax))
                    .Vertical(AxisConstraint.Stretch(HeaderHeight, LogHeight + HintsHeight, 1, FillMax))
                    .WithBorder(BorderStyle.Line, "Drives")
                    .WithPadding(0)
                    .WithHorizontalPadding(1, 1))
                .Add("log", region => region
                    .Horizontal(AxisConstraint.Stretch(0, 0, 1, FillMax))
                    .Vertical(AxisConstraint.FromEnd(HintsHeight, LogHeight))
                    .WithBorder(BorderStyle.Line, "Activity")
                    .WithPadding(0)
                    .WithHorizontalPadding(1, 1))
                .Add("hints", region => region
                    .Horizontal(AxisConstraint.Stretch(0, 0, 1, FillMax))
                    .Vertical(AxisConstraint.FromEnd(0, HintsHeight))
                    .WithPadding(0))
                .Build();

            app.BindPane("header", _Header);
            app.BindPane("content", _Content);
            app.BindPane("log", _Log);
            app.BindPane("hints", _Hints);

            RenderHeader();
            RenderHints();

            S3DriveLog.MessageLogged += OnLogMessage;

            app.Bind("ctrl+q", app.Quit);
            app.Bind("r", () => Launch(RefreshAsync));
            app.Bind("c", () => Launch(AddDriveAsync));
            app.Bind("e", () => Launch(EditDriveAsync));
            app.Bind("d", () => Launch(DeleteDriveAsync));
            app.Bind("m", () => Launch(MountDriveAsync));
            app.Bind("u", () => Launch(UnmountDriveAsync));
            app.Bind("s", () => Launch(ShareDriveAsync));
            app.Bind("x", () => Launch(UnshareDriveAsync));
            app.Bind("f1", () => Launch(HelpAsync));

            Launch(RefreshAsync);
        }

        private void RenderHeader()
        {
            _Header.Clear();
            _Header.WriteLine(Constants.ProductName + " - " + Constants.Tagline);
            _Header.WriteLine("v0.1.0 " + Constants.ReleaseLabel + "   " + Constants.RepositoryUrl);
        }

        private void RenderHints()
        {
            _Hints.Clear();
            _Hints.WriteLine("[c]add [e]edit [d]delete  [m]mount [u]unmount  [s]share [x]unshare  [r]refresh  [F1]help  [Ctrl+Q]quit");
        }

        private void OnLogMessage(string severity, string message)
        {
            TuiApplication? app = _App;
            if (app == null) return;
            app.Post(() => _Log.WriteLine(severity + "  " + message));
        }

        private async Task RefreshAsync()
        {
            _Settings = await _SettingsManager.LoadAsync().ConfigureAwait(false);
            _Status = await StatusStore.ReadAsync(_Paths, CancellationToken.None).ConfigureAwait(false);
            _App?.Post(RenderContent);
        }

        private void RenderContent()
        {
            _Content.Clear();

            if (_Settings.Drives.Count == 0)
            {
                _Content.WriteLine("No drives configured. Press 'c' to add one.");
                return;
            }

            _Content.WriteLine(Row("Name", "Provider", "Bucket", "Letter", "Mount", "Share"));
            foreach (DriveProfile profile in _Settings.Drives)
            {
                DriveStatus? status = FindStatus(profile.Id);
                string mount = status?.MountState.ToString() ?? "Unmounted";
                string share;
                if (status != null && status.Shared)
                {
                    share = "shared:" + (status.ShareName ?? profile.Share.ShareName ?? string.Empty);
                }
                else
                {
                    share = profile.Share.Enabled ? "configured" : "-";
                }

                _Content.WriteLine(Row(profile.Name, profile.Provider.ToString(), profile.Bucket, profile.DriveLetter, mount, share));
            }
        }

        private async Task AddDriveAsync()
        {
            TuiApplication app = RequireApp();
            DriveFormModal modal = new DriveFormModal(null);
            DriveFormResult? result = await app.ShowAsync<DriveFormResult>(modal).ConfigureAwait(false);
            if (result == null) return;

            DriveProfile profile = new DriveProfile { Id = IdGenerator.GenerateDriveId() };
            await ApplyAsync(profile, result, null).ConfigureAwait(false);
            _Settings.Drives.Add(profile);
            await SaveAndReloadAsync().ConfigureAwait(false);
            await RefreshAsync().ConfigureAwait(false);
        }

        private async Task EditDriveAsync()
        {
            DriveProfile? profile = await SelectDriveAsync("Edit which drive?").ConfigureAwait(false);
            if (profile == null) return;

            TuiApplication app = RequireApp();
            DriveFormModal modal = new DriveFormModal(profile);
            DriveFormResult? result = await app.ShowAsync<DriveFormResult>(modal).ConfigureAwait(false);
            if (result == null) return;

            await ApplyAsync(profile, result, profile.SecretKeyEncrypted).ConfigureAwait(false);
            await SaveAndReloadAsync().ConfigureAwait(false);
            await RefreshAsync().ConfigureAwait(false);
        }

        private async Task DeleteDriveAsync()
        {
            DriveProfile? profile = await SelectDriveAsync("Delete which drive?").ConfigureAwait(false);
            if (profile == null) return;

            TuiApplication app = RequireApp();
            bool confirmed = await app.ConfirmAsync("Delete drive '" + profile.Name + "'?", "Delete", "Cancel").ConfigureAwait(false);
            if (!confirmed) return;

            await SendAsync(AgentCommandTypeEnum.Unmount, profile.Id).ConfigureAwait(false);
            _Settings.Drives.Remove(profile);
            await SaveAndReloadAsync().ConfigureAwait(false);
            await RefreshAsync().ConfigureAwait(false);
        }

        private async Task MountDriveAsync()
        {
            await CommandOnSelectedAsync("Mount which drive?", AgentCommandTypeEnum.Mount).ConfigureAwait(false);
        }

        private async Task UnmountDriveAsync()
        {
            await CommandOnSelectedAsync("Unmount which drive?", AgentCommandTypeEnum.Unmount).ConfigureAwait(false);
        }

        private async Task ShareDriveAsync()
        {
            await CommandOnSelectedAsync("Share which drive?", AgentCommandTypeEnum.Share).ConfigureAwait(false);
        }

        private async Task UnshareDriveAsync()
        {
            await CommandOnSelectedAsync("Unshare which drive?", AgentCommandTypeEnum.Unshare).ConfigureAwait(false);
        }

        private async Task HelpAsync()
        {
            TuiApplication app = RequireApp();
            string help = "S3Drive TUI\n\n"
                + "The tray agent owns all mounts and shares and keeps running when this window is closed.\n\n"
                + "c  add a drive        e  edit        d  delete\n"
                + "m  mount              u  unmount\n"
                + "s  share (SMB)        x  unshare\n"
                + "r  refresh            F1 help        Ctrl+Q quit\n\n"
                + "One drive maps to one bucket. Works with AWS S3 and S3-compatible endpoints\n"
                + "(Less3, Ceph, MinIO, and others). Network sharing requires administrator rights.";
            await app.ShowAsync(new MessageModal("Help", help, new List<string> { "OK" })).ConfigureAwait(false);
        }

        private async Task CommandOnSelectedAsync(string prompt, AgentCommandTypeEnum commandType)
        {
            DriveProfile? profile = await SelectDriveAsync(prompt).ConfigureAwait(false);
            if (profile == null) return;

            await SendAsync(commandType, profile.Id).ConfigureAwait(false);
            await Task.Delay(900).ConfigureAwait(false);
            await RefreshAsync().ConfigureAwait(false);
        }

        private async Task<DriveProfile?> SelectDriveAsync(string title)
        {
            if (_Settings.Drives.Count == 0) return null;

            TuiApplication app = RequireApp();
            string[] names = new string[_Settings.Drives.Count];
            for (int i = 0; i < _Settings.Drives.Count; i++)
            {
                names[i] = _Settings.Drives[i].Name + " (" + _Settings.Drives[i].Bucket + ")";
            }

            int index = await app.SelectAsync(title, names).ConfigureAwait(false);
            if (index < 0 || index >= _Settings.Drives.Count) return null;
            return _Settings.Drives[index];
        }

        private async Task ApplyAsync(DriveProfile profile, DriveFormResult result, string? existingSecret)
        {
            profile.Name = result.Name;
            profile.Provider = result.Provider;
            profile.ServiceUrl = result.ServiceUrl;
            profile.Region = result.Region;
            profile.Bucket = result.Bucket;
            profile.AccessKey = result.AccessKey;
            profile.UseSsl = result.UseSsl;
            profile.UsePathStyle = result.UsePathStyle;
            profile.DriveLetter = result.DriveLetter;
            profile.AutoMount = result.AutoMount;
            profile.Share.Enabled = result.ShareEnabled;
            profile.Share.ShareName = result.ShareName;
            profile.Share.Access = result.ShareAccess;
            profile.Share.AllowedPrincipals = result.AllowedPrincipals;

            if (result.SecretPlain.Length > 0)
            {
                profile.SecretKeyEncrypted = await _Protector.ProtectAsync(result.SecretPlain).ConfigureAwait(false);
            }
            else if (existingSecret != null)
            {
                profile.SecretKeyEncrypted = existingSecret;
            }
        }

        private async Task SaveAndReloadAsync()
        {
            await _SettingsManager.SaveAsync(_Settings).ConfigureAwait(false);
            await SendAsync(AgentCommandTypeEnum.Reload, null).ConfigureAwait(false);
        }

        private async Task SendAsync(AgentCommandTypeEnum commandType, string? driveId)
        {
            AgentCommand command = new AgentCommand { CommandType = commandType, DriveId = driveId };
            await CommandChannel.SendAsync(_Paths, command, CancellationToken.None).ConfigureAwait(false);
        }

        private DriveStatus? FindStatus(string driveId)
        {
            if (_Status == null) return null;
            foreach (DriveStatus status in _Status.Drives)
            {
                if (string.Equals(status.DriveId, driveId, StringComparison.Ordinal)) return status;
            }

            return null;
        }

        private void Launch(Func<Task> action)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await action().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    S3DriveLog.Error("Action failed: " + ex.Message);
                }
            });
        }

        private TuiApplication RequireApp()
        {
            if (_App == null) throw new InvalidOperationException("The TUI application is not configured.");
            return _App;
        }

        private static string Row(string name, string provider, string bucket, string letter, string mount, string share)
        {
            return string.Format(
                "{0,-18} {1,-13} {2,-18} {3,-7} {4,-11} {5}",
                Trim(name, 18),
                Trim(provider, 13),
                Trim(bucket, 18),
                Trim(letter, 7),
                Trim(mount, 11),
                share);
        }

        private static string Trim(string value, int max)
        {
            if (value.Length <= max) return value;
            return value.Substring(0, max - 1) + "…";
        }
    }
}
