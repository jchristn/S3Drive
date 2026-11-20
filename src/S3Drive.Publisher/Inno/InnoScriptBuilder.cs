namespace S3Drive.Publisher.Inno
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using S3Drive.Publisher.Manifest;

    /// <summary>
    /// Generates an Inno Setup 6 script from the manifest. The installer shows the logo, product
    /// name, tagline, and a license page that must be accepted; adds the install directory to the
    /// system PATH; registers tray agents to start at sign-in; stops running copies before
    /// upgrading or uninstalling; and checks for prerequisites. Thread-safe: holds no state.
    /// </summary>
    public static class InnoScriptBuilder
    {
        /// <summary>
        /// The registry key holding the machine-wide PATH.
        /// </summary>
        public const string EnvironmentKey = "SYSTEM\\CurrentControlSet\\Control\\Session Manager\\Environment";

        /// <summary>
        /// The registry key holding sign-in Run entries.
        /// </summary>
        public const string RunKey = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";

        /// <summary>
        /// The Inno Setup task name for adding the install directory to PATH.
        /// </summary>
        public const string PathTask = "addtopath";

        /// <summary>
        /// Maps a runtime identifier to the Inno Setup architecture identifier used for both
        /// ArchitecturesAllowed and ArchitecturesInstallIn64BitMode. Each build installs only on its
        /// own architecture, so an ARM64 machine gets the native ARM64 build.
        /// </summary>
        /// <param name="runtime">The runtime identifier.</param>
        /// <returns>"x64os" for win-x64, "arm64" for win-arm64.</returns>
        /// <exception cref="PublisherException">Thrown for a runtime that has no Windows installer.</exception>
        public static string ArchitectureFor(string runtime)
        {
            switch (runtime)
            {
                case "win-x64": return "x64os";
                case "win-arm64": return "arm64";
                default: throw new PublisherException("Runtime '" + runtime + "' has no Inno Setup installer; only win-x64 and win-arm64 are supported.");
            }
        }

        /// <summary>
        /// The installer's stable AppId: a version 5 UUID of the manifest identifier.
        /// </summary>
        /// <param name="identifier">The reverse-DNS identifier.</param>
        /// <returns>The AppId GUID.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="identifier"/> is null.</exception>
        public static Guid AppIdFor(string identifier)
        {
            return DeterministicGuid.Create(DeterministicGuid.DnsNamespace, identifier);
        }

        /// <summary>
        /// The welcome page text: tagline, what gets installed, and prerequisites.
        /// </summary>
        /// <param name="manifest">The manifest.</param>
        /// <param name="version">The release version.</param>
        /// <returns>Plain text with \n line breaks.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="manifest"/> or <paramref name="version"/> is null.</exception>
        public static string WelcomeText(PublisherManifest manifest, string version)
        {
            if (manifest == null) throw new ArgumentNullException(nameof(manifest));
            if (version == null) throw new ArgumentNullException(nameof(version));

            StringBuilder text = new StringBuilder();
            text.Append(manifest.Project.Tagline).Append("\n\n");
            text.Append("This will install ").Append(manifest.Project.Name).Append(' ').Append(version).Append(" on your computer:\n");
            foreach (ArtifactInfo artifact in manifest.Artifacts.Where(a => !string.IsNullOrWhiteSpace(a.Summary)))
            {
                text.Append("\n- ").Append(artifact.Name).Append(", ").Append(artifact.Summary);
                if (artifact.Command != null) text.Append(". Run it with the ").Append(artifact.Command).Append(" command, which Setup adds to PATH");
                if (artifact.Startup != null) text.Append(". It can start automatically when you sign in");
                text.Append('.');
            }

            foreach (PrerequisiteInfo prerequisite in manifest.Windows.Prerequisites)
            {
                text.Append("\n\n").Append(prerequisite.Message);
            }

            return text.ToString();
        }

        /// <summary>
        /// Generates the script.
        /// </summary>
        /// <param name="input">The script input.</param>
        /// <returns>The script text with CRLF line endings.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="input"/> is null.</exception>
        /// <exception cref="PublisherException">Thrown when a value cannot be represented safely in the script, or the runtime has no installer.</exception>
        public static string Build(InnoScriptInput input)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));

            PublisherManifest manifest = input.Manifest;
            ProjectInfo project = manifest.Project;
            string architecture = ArchitectureFor(input.Runtime);
            List<ArtifactInfo> commands = manifest.Artifacts.Where(a => a.Command != null).ToList();
            List<ArtifactInfo> startups = manifest.Artifacts.Where(a => a.Startup != null).ToList();
            bool signing = !string.IsNullOrEmpty(input.SignToolName);

            Safe(project.Name, "project.name");
            Safe(project.Publisher, "project.publisher");
            Safe(project.PublisherUrl, "project.publisherUrl");
            Safe(project.RepositoryUrl, "project.repositoryUrl");
            Safe(project.Copyright, "project.copyright");
            Safe(project.Tagline, "project.tagline");
            Safe(input.Version, "version");
            SafePath(input.StageDirectory, "stage directory");
            SafePath(input.OutputDirectory, "output directory");
            SafePath(input.LicenseFile, "license file");
            SafePath(input.SetupIconFile, "setup icon");
            foreach (string image in input.Images.LargeImages.Concat(input.Images.SmallImages)) SafePath(image, "wizard image");
            foreach (ArtifactInfo artifact in manifest.Artifacts)
            {
                Safe(artifact.Name, "artifacts.name");
                Safe(artifact.Executable, "artifacts.executable");
                Safe(artifact.Shortcut, "artifacts.shortcut");
                Safe(artifact.Startup, "artifacts.startup");
            }

            foreach (string legacy in manifest.Windows.LegacyStartupValues) Safe(legacy, "windows.legacyStartupValues");

            StringBuilder s = new StringBuilder();
            Line(s, "; Generated by S3Drive.Publisher from publisher.json for " + input.Runtime + ". Do not edit; regenerate it.");
            Line(s, string.Empty);

            Line(s, "[Setup]");
            Line(s, "AppId={{" + AppIdFor(project.Identifier).ToString("D").ToUpperInvariant() + "}");
            Line(s, "AppName=" + project.Name);
            Line(s, "AppVersion=" + input.Version);
            Line(s, "AppVerName=" + project.Name + " " + input.Version);
            Line(s, "AppPublisher=" + project.Publisher);
            if (!string.IsNullOrEmpty(project.PublisherUrl)) Line(s, "AppPublisherURL=" + project.PublisherUrl);
            if (!string.IsNullOrEmpty(project.RepositoryUrl))
            {
                Line(s, "AppSupportURL=" + project.RepositoryUrl + "/issues");
                Line(s, "AppUpdatesURL=" + project.RepositoryUrl + "/releases");
            }

            if (!string.IsNullOrEmpty(project.Copyright)) Line(s, "AppCopyright=" + project.Copyright);
            Line(s, "VersionInfoVersion=" + VersionResolver.ToNumericVersion(input.Version));
            Line(s, "VersionInfoProductName=" + project.Name);
            Line(s, "VersionInfoProductTextVersion=" + input.Version);
            Line(s, "VersionInfoDescription=" + project.Name + " Setup");
            Line(s, "VersionInfoCompany=" + project.Publisher);
            Line(s, "DefaultDirName={autopf}\\" + project.Name);
            Line(s, "DefaultGroupName=" + project.Name);
            Line(s, "DisableProgramGroupPage=yes");
            Line(s, "DisableWelcomePage=no");
            Line(s, "LicenseFile=" + input.LicenseFile);
            Line(s, "SourceDir=" + input.StageDirectory);
            Line(s, "OutputDir=" + input.OutputDirectory);
            Line(s, "OutputBaseFilename=" + input.OutputBaseFilename);
            Line(s, "SetupIconFile=" + input.SetupIconFile);
            Line(s, "UninstallDisplayIcon={app}\\" + input.InstalledIconFileName);
            Line(s, "UninstallDisplayName=" + project.Name);
            Line(s, "WizardStyle=modern");
            if (input.Images.LargeImages.Any()) Line(s, "WizardImageFile=" + string.Join(",", input.Images.LargeImages));
            if (input.Images.SmallImages.Any()) Line(s, "WizardSmallImageFile=" + string.Join(",", input.Images.SmallImages));
            Line(s, "Compression=lzma2/max");
            Line(s, "SolidCompression=yes");
            Line(s, "PrivilegesRequired=admin");
            Line(s, "ArchitecturesAllowed=" + architecture);
            Line(s, "ArchitecturesInstallIn64BitMode=" + architecture);
            Line(s, "ChangesEnvironment=" + (commands.Any() ? "yes" : "no"));
            Line(s, "MinVersion=10.0");
            Line(s, "UsedUserAreasWarning=no");
            if (signing)
            {
                Line(s, "SignTool=" + input.SignToolName);
                Line(s, "SignedUninstaller=yes");
            }

            Line(s, string.Empty);
            Line(s, "[Languages]");
            Line(s, "Name: \"english\"; MessagesFile: \"compiler:Default.isl\"");
            Line(s, string.Empty);

            Line(s, "[Messages]");
            Line(s, "WelcomeLabel2=" + MessageText(WelcomeText(manifest, input.Version)));
            Line(s, string.Empty);

            if (commands.Any() || startups.Any())
            {
                Line(s, "[Tasks]");
                if (commands.Any())
                {
                    string names = string.Join(" and ", commands.Select(a => a.Command));
                    Line(s, "Name: \"" + PathTask + "\"; Description: " + Quote("Add the " + names + " command to the system PATH") + "; GroupDescription: \"Command line:\"");
                }

                for (int i = 0; i < startups.Count; i++)
                {
                    Line(s, "Name: \"" + StartupTask(i) + "\"; Description: " + Quote("Start " + startups[i].Name + " when anyone signs in to Windows") + "; GroupDescription: \"Startup:\"");
                }

                Line(s, string.Empty);
            }

            Line(s, "[Files]");
            foreach (ArtifactInfo artifact in manifest.Artifacts)
            {
                Line(s, "Source: " + Quote(artifact.Executable) + "; DestDir: \"{app}\"; Flags: ignoreversion" + (signing ? " signonce" : string.Empty));
            }

            Line(s, "Source: \"*\"; Excludes: " + Quote(string.Join(",", manifest.Artifacts.Select(a => "\\" + a.Executable))) + "; DestDir: \"{app}\"; Flags: ignoreversion recursesubdirs createallsubdirs");
            Line(s, string.Empty);

            List<ArtifactInfo> shortcuts = manifest.Artifacts.Where(a => !string.IsNullOrWhiteSpace(a.Shortcut)).ToList();
            if (shortcuts.Any())
            {
                Line(s, "[Icons]");
                foreach (ArtifactInfo artifact in shortcuts)
                {
                    Line(s, "Name: " + Quote("{autoprograms}\\" + artifact.Shortcut) + "; Filename: " + Quote("{app}\\" + artifact.Executable)
                        + "; IconFilename: " + Quote("{app}\\" + input.InstalledIconFileName) + "; Comment: " + Quote(Braces(project.Tagline)));
                }

                Line(s, string.Empty);
            }

            Line(s, "[Registry]");
            if (commands.Any())
            {
                Line(s, "Root: HKLM; Subkey: " + Quote(EnvironmentKey) + "; ValueType: expandsz; ValueName: \"Path\"; ValueData: \"{olddata};{app}\"; Check: NeedsAddPath('{app}'); Tasks: " + PathTask);
            }

            for (int i = 0; i < startups.Count; i++)
            {
                Line(s, "Root: HKLM; Subkey: " + Quote(RunKey) + "; ValueType: string; ValueName: " + Quote(startups[i].Startup!)
                    + "; ValueData: " + Quote("\"{app}\\" + startups[i].Executable + "\"") + "; Flags: uninsdeletevalue; Tasks: " + StartupTask(i));
            }

            foreach (string legacy in manifest.Windows.LegacyStartupValues)
            {
                Line(s, "Root: HKCU; Subkey: " + Quote(RunKey) + "; ValueType: none; ValueName: " + Quote(legacy) + "; Flags: deletevalue");
            }

            Line(s, string.Empty);

            Line(s, "[Run]");
            foreach (ArtifactInfo artifact in manifest.Artifacts.Where(a => a.Kind == ArtifactKindEnum.TrayAgent))
            {
                Line(s, "Filename: " + Quote("{app}\\" + artifact.Executable) + "; Description: " + Quote("Start " + artifact.Name + " now") + "; Flags: nowait postinstall skipifsilent runasoriginaluser");
            }

            foreach (ArtifactInfo artifact in manifest.Artifacts.Where(a => a.Kind == ArtifactKindEnum.Tui && !string.IsNullOrWhiteSpace(a.Shortcut)))
            {
                Line(s, "Filename: " + Quote("{app}\\" + artifact.Executable) + "; Description: " + Quote("Open " + artifact.Name) + "; Flags: nowait postinstall skipifsilent runasoriginaluser unchecked");
            }

            Line(s, string.Empty);

            Line(s, "[UninstallRun]");
            for (int i = 0; i < manifest.Artifacts.Count; i++)
            {
                Line(s, "Filename: \"{sys}\\taskkill.exe\"; Parameters: " + Quote("/F /IM \"" + manifest.Artifacts[i].Executable + "\"") + "; Flags: runhidden; RunOnceId: \"Stop" + i + "\"");
            }

            Line(s, string.Empty);
            AppendCode(s, manifest);
            return s.ToString();
        }

        /// <summary>
        /// Converts plain text to an Inno [Messages] value: line breaks become %n and % is doubled.
        /// </summary>
        /// <param name="text">The text.</param>
        /// <returns>The single-line message value.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="text"/> is null.</exception>
        public static string MessageText(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            return text.Replace("%", "%%", StringComparison.Ordinal)
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace("\n", "%n", StringComparison.Ordinal);
        }

        /// <summary>
        /// Quotes a value as an Inno Setup parameter string, doubling embedded quotes.
        /// </summary>
        /// <param name="value">The value.</param>
        /// <returns>The quoted value.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="value"/> is null.</exception>
        public static string Quote(string value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
        }

        /// <summary>
        /// Quotes a value as a Pascal string literal for the [Code] section, doubling single quotes.
        /// </summary>
        /// <param name="value">The value.</param>
        /// <returns>The quoted literal.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="value"/> is null.</exception>
        public static string PascalString(string value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            return "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
        }

        private static void AppendCode(StringBuilder s, PublisherManifest manifest)
        {
            Line(s, "[Code]");
            Line(s, "const");
            Line(s, "  EnvironmentKey = " + PascalString(EnvironmentKey) + ";");
            Line(s, string.Empty);
            Line(s, "function PathContains(Paths, Dir: string): Boolean;");
            Line(s, "var");
            Line(s, "  AllPaths: string;");
            Line(s, "  Target: string;");
            Line(s, "begin");
            Line(s, "  AllPaths := ';' + Uppercase(Paths) + ';';");
            Line(s, "  Target := Uppercase(Dir);");
            Line(s, "  Result := (Pos(';' + Target + ';', AllPaths) > 0) or (Pos(';' + Target + '\\;', AllPaths) > 0);");
            Line(s, "end;");
            Line(s, string.Empty);
            Line(s, "function NeedsAddPath(Param: string): Boolean;");
            Line(s, "var");
            Line(s, "  Paths: string;");
            Line(s, "begin");
            Line(s, "  if not RegQueryStringValue(HKEY_LOCAL_MACHINE, EnvironmentKey, 'Path', Paths) then");
            Line(s, "    Paths := '';");
            Line(s, "  Result := not PathContains(Paths, ExpandConstant(Param));");
            Line(s, "end;");
            Line(s, string.Empty);
            Line(s, "procedure RemovePath(Dir: string);");
            Line(s, "var");
            Line(s, "  Paths: string;");
            Line(s, "  Target: string;");
            Line(s, "  P: Integer;");
            Line(s, "begin");
            Line(s, "  if not RegQueryStringValue(HKEY_LOCAL_MACHINE, EnvironmentKey, 'Path', Paths) then");
            Line(s, "    exit;");
            Line(s, "  Paths := ';' + Paths + ';';");
            Line(s, "  Target := ';' + Uppercase(Dir) + ';';");
            Line(s, "  P := Pos(Target, Uppercase(Paths));");
            Line(s, "  if P = 0 then");
            Line(s, "    exit;");
            Line(s, "  Delete(Paths, P, Length(Target) - 1);");
            Line(s, "  Paths := Copy(Paths, 2, Length(Paths) - 2);");
            Line(s, "  RegWriteExpandStringValue(HKEY_LOCAL_MACHINE, EnvironmentKey, 'Path', Paths);");
            Line(s, "end;");
            Line(s, string.Empty);
            Line(s, "procedure StopRunningCopies();");
            Line(s, "var");
            Line(s, "  ResultCode: Integer;");
            Line(s, "begin");
            foreach (ArtifactInfo artifact in manifest.Artifacts)
            {
                Line(s, "  Exec(ExpandConstant('{sys}\\taskkill.exe'), " + PascalString("/F /IM \"" + artifact.Executable + "\"") + ", '', SW_HIDE, ewWaitUntilTerminated, ResultCode);");
            }

            Line(s, "end;");
            Line(s, string.Empty);
            Line(s, "function InitializeSetup(): Boolean;");
            Line(s, "var");
            Line(s, "  ErrorCode: Integer;");
            Line(s, "begin");
            Line(s, "  Result := True;");
            foreach (PrerequisiteInfo prerequisite in manifest.Windows.Prerequisites)
            {
                string message = prerequisite.Message + "\n\n" + prerequisite.Name + " is not installed on this computer. Setup will continue, but install "
                    + prerequisite.Name + " before using " + manifest.Project.Name + ".\n\nOpen the " + prerequisite.Name + " download page now?";
                Line(s, "  if not FileExists(ExpandConstant(" + PascalString(prerequisite.DetectFile) + ")) then");
                Line(s, "  begin");
                Line(s, "    if SuppressibleMsgBox(" + PascalLines(message) + ", mbInformation, MB_YESNO, IDNO) = IDYES then");
                Line(s, "      ShellExec('open', " + PascalString(prerequisite.Url) + ", '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);");
                Line(s, "  end;");
            }

            Line(s, "end;");
            Line(s, string.Empty);
            Line(s, "function PrepareToInstall(var NeedsRestart: Boolean): String;");
            Line(s, "begin");
            Line(s, "  StopRunningCopies();");
            Line(s, "  Result := '';");
            Line(s, "end;");
            Line(s, string.Empty);
            Line(s, "procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);");
            Line(s, "begin");
            Line(s, "  if CurUninstallStep = usUninstall then");
            Line(s, "    RemovePath(ExpandConstant('{app}'));");
            Line(s, "end;");
        }

        private static string PascalLines(string text)
        {
            string[] lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
            return string.Join(" + #13#10 + ", lines.Select(PascalString));
        }

        private static string StartupTask(int index)
        {
            return "startup" + index;
        }

        private static string Braces(string value)
        {
            return value.Replace("{", "{{", StringComparison.Ordinal);
        }

        private static void Safe(string? value, string name)
        {
            if (value == null) return;
            if (value.IndexOfAny(new[] { '{', '}', '"', '\r', '\n' }) >= 0)
                throw new PublisherException(name + " value '" + value + "' cannot contain braces, double quotes, or line breaks in an Inno Setup script.");
        }

        private static void SafePath(string value, string name)
        {
            if (string.IsNullOrEmpty(value)) throw new PublisherException("The " + name + " path is required to build the Inno Setup script.");
            if (value.IndexOfAny(new[] { '{', '}', '"', ',', '\r', '\n' }) >= 0)
                throw new PublisherException("The " + name + " path '" + value + "' cannot contain braces, double quotes, commas, or line breaks in an Inno Setup script.");
        }

        private static void Line(StringBuilder s, string line)
        {
            s.Append(line).Append("\r\n");
        }
    }
}
