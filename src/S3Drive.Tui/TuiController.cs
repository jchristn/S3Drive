namespace S3Drive.Tui
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Reflection;
    using System.Text;
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

    /// <summary>
    /// Builds and drives the TUI: a Drives pane and an Activity pane, each with its own keyboard
    /// shortcut bar. Tab moves focus between the two panes; the focused pane's shortcuts are active
    /// and its bar is highlighted. Activity tails the shared log file so operations happening in the
    /// agent and the TUI are visible, and can be copied to the clipboard.
    /// </summary>
    internal sealed class TuiController
    {
        private const int ActivityHeight = 14;
        private const int HintHeight = 1;
        private const int FillMax = 1_000_000;
        private const int MaxActivityLines = 2000;

        private readonly S3DrivePaths _Paths;
        private readonly SettingsManager _SettingsManager;
        private readonly CredentialProtector _Protector;
        private readonly bool _ShowSplash;
        private readonly object _ActivitySync = new object();
        private readonly List<string> _ActivityLines = new List<string>();

        private readonly Pane _Content = new Pane("content");
        private readonly Pane _Log = new Pane("log");
        private readonly HintBar _DrivesHints;
        private readonly HintBar _ActivityHints;

        private TuiApplication? _App;
        private S3DriveSettings _Settings = new S3DriveSettings();
        private AgentStatus? _Status;
        private List<string> _LastContentLines = new List<string>();
        private bool _ActivityFocused;

        private string? _LogPath;
        private long _LogPosition;
        private string _LogPartial = string.Empty;

        /// <summary>
        /// Initializes a new controller.
        /// </summary>
        /// <param name="paths">The path resolver.</param>
        /// <param name="showSplash">Whether to show the startup splash screen. Defaults to true.</param>
        public TuiController(S3DrivePaths paths, bool showSplash = true)
        {
            _Paths = paths;
            _SettingsManager = new SettingsManager(paths);
            _Protector = new CredentialProtector(paths.MachineKeyFile);
            _ShowSplash = showSplash;

            _DrivesHints = new HintBar("Drives", new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("Tab", "Activity"),
                new KeyValuePair<string, string>("c", "Add"),
                new KeyValuePair<string, string>("e", "Edit"),
                new KeyValuePair<string, string>("d", "Delete"),
                new KeyValuePair<string, string>("m", "Mount"),
                new KeyValuePair<string, string>("u", "Unmount"),
                new KeyValuePair<string, string>("r", "Refresh"),
                new KeyValuePair<string, string>("F1", "Help"),
                new KeyValuePair<string, string>("^Q", "Quit")
            });

            _ActivityHints = new HintBar("Activity", new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("Tab", "Drives"),
                new KeyValuePair<string, string>("c", "Copy"),
                new KeyValuePair<string, string>("r", "Refresh"),
                new KeyValuePair<string, string>("F1", "Help"),
                new KeyValuePair<string, string>("^Q", "Quit")
            });

            _DrivesHints.Focused = true;
            _ActivityHints.Focused = false;
        }

        /// <summary>
        /// Configures the TUI application: theme, layout, panes, and key bindings.
        /// </summary>
        /// <param name="app">The application.</param>
        public void Configure(TuiApplication app)
        {
            _App = app;
            app.Theme = Theme.Dark;

            // The header shows the "s3drive" ASCII-art wordmark on the left with the tagline and link
            // to its right, then a blank separator row before the Drives pane beneath it.
            string[] logoRows = S3DriveBanner.WordmarkLines();
            HeaderBanner header = new HeaderBanner(logoRows, Constants.Tagline, Constants.RepositoryUrl);
            int headerHeight = logoRows.Length + 1;

            app.Layout = Layout.Create()
                .Add("header", region => region
                    .Horizontal(AxisConstraint.Stretch(0, 0, 1, FillMax))
                    .Vertical(AxisConstraint.Fixed(0, headerHeight))
                    .WithPadding(0))
                .Add("content", region => region
                    .Horizontal(AxisConstraint.Stretch(0, 0, 1, FillMax))
                    .Vertical(AxisConstraint.Stretch(headerHeight, ActivityHeight + 2 * HintHeight, 1, FillMax))
                    .WithBorder(BorderStyle.Line, "Drives")
                    .WithPadding(0)
                    .WithHorizontalPadding(1, 1))
                .Add("driveshints", region => region
                    .Horizontal(AxisConstraint.Stretch(0, 0, 1, FillMax))
                    .Vertical(AxisConstraint.FromEnd(ActivityHeight + HintHeight, HintHeight))
                    .WithPadding(0)
                    .WithHorizontalPadding(1, 1))
                .Add("log", region => region
                    .Horizontal(AxisConstraint.Stretch(0, 0, 1, FillMax))
                    .Vertical(AxisConstraint.FromEnd(HintHeight, ActivityHeight))
                    .WithBorder(BorderStyle.Line, "Activity")
                    .WithPadding(0)
                    .WithHorizontalPadding(1, 1))
                .Add("activityhints", region => region
                    .Horizontal(AxisConstraint.Stretch(0, 0, 1, FillMax))
                    .Vertical(AxisConstraint.FromEnd(0, HintHeight))
                    .WithPadding(0)
                    .WithHorizontalPadding(1, 1))
                .Build();

            app.Bind("header", header);
            app.BindPane("content", _Content);
            app.Bind("driveshints", _DrivesHints);
            app.BindPane("log", _Log);
            app.Bind("activityhints", _ActivityHints);

            app.Bind("ctrl+q", app.Quit);
            app.Bind("tab", ToggleFocus);
            app.Bind("r", () => Launch(RefreshAsync));
            app.Bind("f1", () => Launch(HelpAsync));
            app.Bind("c", OnC);
            app.Bind("e", () => { if (!_ActivityFocused) Launch(EditDriveAsync); });
            app.Bind("d", () => { if (!_ActivityFocused) Launch(DeleteDriveAsync); });
            app.Bind("m", () => { if (!_ActivityFocused) Launch(MountDriveAsync); });
            app.Bind("u", () => { if (!_ActivityFocused) Launch(UnmountDriveAsync); });

            Launch(StartAsync);
            StartStatusPolling(app);
            StartLogTail(app);
        }

        private async Task StartAsync()
        {
            if (_ShowSplash)
            {
                Version? assemblyVersion = Assembly.GetExecutingAssembly().GetName().Version;
                string version = assemblyVersion != null
                    ? assemblyVersion.Major + "." + assemblyVersion.Minor + "." + assemblyVersion.Build
                    : "0.1.0";

                SplashModal splash = new SplashModal(Constants.ProductName, S3DriveBanner.SplashLines(version));
                await RequireApp().ShowAsync(splash).ConfigureAwait(false);
            }

            await RefreshAsync().ConfigureAwait(false);
        }

        private void ToggleFocus()
        {
            _ActivityFocused = !_ActivityFocused;
            _DrivesHints.Focused = !_ActivityFocused;
            _ActivityHints.Focused = _ActivityFocused;
            _App?.Post(() => { });
        }

        private void OnC()
        {
            if (_ActivityFocused) Launch(CopyActivityAsync);
            else Launch(AddDriveAsync);
        }

        private void StartStatusPolling(TuiApplication app)
        {
            _ = Task.Run(async () =>
            {
                while (true)
                {
                    try
                    {
                        _Status = await StatusStore.ReadAsync(_Paths, CancellationToken.None).ConfigureAwait(false);
                        app.Post(RenderContent);
                    }
                    catch (Exception)
                    {
                    }

                    await Task.Delay(1000).ConfigureAwait(false);
                }
            });
        }

        private void StartLogTail(TuiApplication app)
        {
            _ = Task.Run(async () =>
            {
                while (true)
                {
                    try
                    {
                        TailOnce();
                    }
                    catch (Exception)
                    {
                    }

                    await Task.Delay(600).ConfigureAwait(false);
                }
            });
        }

        private void TailOnce()
        {
            string directory = _Paths.LogDirectory;
            if (!Directory.Exists(directory)) return;

            string? newest = null;
            DateTime newestTime = DateTime.MinValue;
            // SyslogLogging's FileWithDate mode appends the date after the extension
            // (for example "s3drive.log.20260829"), so match "*.log*" rather than "*.log".
            foreach (string file in Directory.GetFiles(directory, "*.log*"))
            {
                DateTime written = File.GetLastWriteTimeUtc(file);
                if (written >= newestTime)
                {
                    newestTime = written;
                    newest = file;
                }
            }

            if (newest == null) return;

            if (!string.Equals(newest, _LogPath, StringComparison.OrdinalIgnoreCase))
            {
                _LogPath = newest;
                _LogPosition = 0;
                _LogPartial = string.Empty;
            }

            using (FileStream stream = new FileStream(newest, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                if (stream.Length < _LogPosition)
                {
                    _LogPosition = 0;
                    _LogPartial = string.Empty;
                }

                if (stream.Length == _LogPosition) return;

                stream.Seek(_LogPosition, SeekOrigin.Begin);
                int length = (int)(stream.Length - _LogPosition);
                byte[] buffer = new byte[length];
                int read = stream.Read(buffer, 0, length);
                _LogPosition += read;

                string chunk = _LogPartial + Encoding.UTF8.GetString(buffer, 0, read);
                int lastNewline = chunk.LastIndexOf('\n');
                if (lastNewline < 0)
                {
                    _LogPartial = chunk;
                    return;
                }

                _LogPartial = chunk.Substring(lastNewline + 1);
                string[] lines = chunk.Substring(0, lastNewline).Split('\n');
                foreach (string raw in lines)
                {
                    string line = raw.TrimEnd('\r');
                    if (line.Length > 0) AppendActivity(line);
                }
            }
        }

        private void AppendActivity(string line)
        {
            lock (_ActivitySync)
            {
                _ActivityLines.Add(line);
                if (_ActivityLines.Count > MaxActivityLines) _ActivityLines.RemoveAt(0);
            }

            _App?.Post(() => _Log.WriteLine(line));
        }

        private Task CopyActivityAsync()
        {
            string text;
            int count;
            lock (_ActivitySync)
            {
                text = string.Join(Environment.NewLine, _ActivityLines);
                count = _ActivityLines.Count;
            }

            bool copied = Clipboard.TryCopy(text);
            AppendActivity(copied
                ? "[copied " + count + " activity line(s) to the clipboard]"
                : "[clipboard copy failed]");
            return Task.CompletedTask;
        }

        private async Task RefreshAsync()
        {
            _Settings = await _SettingsManager.LoadAsync().ConfigureAwait(false);
            _Status = await StatusStore.ReadAsync(_Paths, CancellationToken.None).ConfigureAwait(false);
            _App?.Post(RenderContent);
        }

        private void RenderContent()
        {
            List<string> lines = BuildContentLines();
            if (SameLines(lines, _LastContentLines)) return;
            _LastContentLines = lines;

            _Content.Clear();
            foreach (string line in lines)
            {
                _Content.WriteLine(line);
            }
        }

        private List<string> BuildContentLines()
        {
            List<string> lines = new List<string>();
            if (_Settings.Drives.Count == 0)
            {
                lines.Add("No drives configured. Press 'c' to add one.");
                return lines;
            }

            lines.Add(Row("Name", "Provider", "Bucket", "Letter", "Mount"));
            foreach (DriveProfile profile in _Settings.Drives)
            {
                DriveStatus? status = FindStatus(profile.Id);
                string mount = status?.MountState.ToString() ?? "Unmounted";
                lines.Add(Row(profile.Name, profile.Provider.ToString(), profile.Bucket, profile.DriveLetter, mount));
            }

            return lines;
        }

        private static bool SameLines(List<string> a, List<string> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
            {
                if (!string.Equals(a[i], b[i], StringComparison.Ordinal)) return false;
            }

            return true;
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

        private async Task HelpAsync()
        {
            TuiApplication app = RequireApp();
            string help = "S3Drive TUI\n\n"
                + "The tray agent owns all mounts and keeps running when this window is closed.\n\n"
                + "Tab                 switch focus between the Drives and Activity panes\n"
                + "Drives:  c add   e edit   d delete   m mount   u unmount\n"
                + "Activity: c copy the activity log to the clipboard\n"
                + "r refresh    F1 help    Ctrl+Q quit\n\n"
                + "One drive maps to one bucket; configuring a drive mounts it automatically.\n"
                + "Works with AWS S3 and S3-compatible endpoints (Less3, Ceph, MinIO, and others).\n"
                + "To share a mounted drive on the network, use Windows Explorer.";
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
            profile.AutoMount = true;

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

        private static string Row(string name, string provider, string bucket, string letter, string mount)
        {
            return string.Format(
                "{0,-20} {1,-14} {2,-22} {3,-8} {4}",
                Trim(name, 20),
                Trim(provider, 14),
                Trim(bucket, 22),
                Trim(letter, 8),
                mount);
        }

        private static string Trim(string value, int max)
        {
            if (value.Length <= max) return value;
            return value.Substring(0, max - 1) + "…";
        }
    }
}
