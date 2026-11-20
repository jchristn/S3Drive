namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;
    using S3Drive.Publisher;
    using S3Drive.Publisher.Channels;
    using S3Drive.Publisher.Imaging;
    using S3Drive.Publisher.Inno;
    using S3Drive.Publisher.Manifest;
    using Test.Shared.Helpers;
    using Touchstone.Core;

    /// <summary>
    /// Tests for S3Drive.Publisher: the committed publisher.json, version handling, the stable
    /// AppId, command-line parsing, file naming, staging helpers, checksums, and the generated
    /// Inno Setup script.
    /// </summary>
    public static class PublisherSuite
    {
        private const string SuiteId = "Publisher";

        /// <summary>
        /// Builds the suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>
            {
                TestCases.Create(SuiteId, "CommittedManifestIsValid", "The committed publisher.json parses, validates, and references files that exist", () =>
                {
                    string root = RepositoryRoot();
                    PublisherManifest manifest = ManifestLoader.Load(Path.Combine(root, ManifestLoader.FileName));
                    ManifestLoader.ValidateFiles(manifest, root);
                    Assert.Equal("S3Drive", manifest.Project.Name);
                    Assert.Equal("s3drive", manifest.Project.PackageName);
                    Assert.Equal("ai.latticetech.s3drive", manifest.Project.Identifier);
                    Assert.Equal("Lattice AI, LLC", manifest.Project.Publisher);
                    Assert.True(manifest.Build.Runtimes.SequenceEqual(new[] { "win-x64", "win-arm64" }), "runtimes");
                    Assert.True(manifest.Artifacts.Any(a => a.Kind == ArtifactKindEnum.TrayAgent && a.Startup == "S3Drive Agent"), "agent starts at sign-in as 'S3Drive Agent'");
                    Assert.True(manifest.Artifacts.Any(a => a.Command == "s3drive"), "s3drive command");
                    Assert.False(manifest.Signing.Windows.IsConfigured, "unsigned by policy");
                }),

                TestCases.Create(SuiteId, "CommittedVersionIsSemVer", "Directory.Build.props holds a valid SemVer version", () =>
                {
                    string root = RepositoryRoot();
                    string version = VersionResolver.ReadFromFile(Path.Combine(root, "Directory.Build.props"));
                    VersionResolver.Validate(version, "Directory.Build.props");
                }),

                TestCases.Create(SuiteId, "ManifestRejectsUnknownProperty", "A misspelled manifest property fails instead of being ignored", () =>
                {
                    string json = MinimalManifestJson().Replace("\"tagline\"", "\"tagLyne\"", StringComparison.Ordinal);
                    Assert.Throws<PublisherException>(() => ManifestLoader.Parse(json));
                }),

                TestCases.Create(SuiteId, "ManifestNamesMissingFields", "Validation lists every missing required field", () =>
                {
                    string json = MinimalManifestJson().Replace("\"name\": \"Demo\"", "\"name\": \"\"", StringComparison.Ordinal);
                    string message = Capture(() => ManifestLoader.Parse(json));
                    Assert.Contains(message, "project.name is required");
                }),

                TestCases.Create(SuiteId, "ManifestRejectsStartupOnNonAgent", "Only TrayAgent artifacts may register a startup entry", () =>
                {
                    string json = MinimalManifestJson().Replace("\"kind\": \"TrayAgent\"", "\"kind\": \"Cli\"", StringComparison.Ordinal);
                    string message = Capture(() => ManifestLoader.Parse(json));
                    Assert.Contains(message, "startup is only allowed for TrayAgent");
                }),

                TestCases.Create(SuiteId, "ManifestRejectsUnknownChannelAndRuntime", "Unknown channels and runtime identifiers are rejected", () =>
                {
                    string json = MinimalManifestJson()
                        .Replace("\"publish\", \"zip\"", "\"publish\", \"msix\"", StringComparison.Ordinal)
                        .Replace("\"win-arm64\"", "\"win-x86\"", StringComparison.Ordinal);
                    string message = Capture(() => ManifestLoader.Parse(json));
                    Assert.Contains(message, "'msix' is unknown");
                    Assert.Contains(message, "'win-x86' is not a supported runtime identifier");
                }),

                TestCases.Create(SuiteId, "ManifestRequiresSigningCommandWhenConfigured", "Turning signing on without a command fails validation", () =>
                {
                    string json = MinimalManifestJson().Replace("\"isConfigured\": false", "\"isConfigured\": true", StringComparison.Ordinal);
                    string message = Capture(() => ManifestLoader.Parse(json));
                    Assert.Contains(message, "signing.windows.command is required");
                }),

                TestCases.Create(SuiteId, "VersionFromProps", "The version is read from the Version element and validated", () =>
                {
                    string xml = "<Project><PropertyGroup><TargetFramework>net8.0</TargetFramework><Version> 1.2.3-rc.1 </Version></PropertyGroup></Project>";
                    Assert.Equal("1.2.3-rc.1", VersionResolver.ReadFromXml(xml, "test.props"));
                    Assert.Throws<PublisherException>(() => VersionResolver.ReadFromXml("<Project><PropertyGroup /></Project>", "test.props"));
                    Assert.Throws<PublisherException>(() => VersionResolver.ReadFromXml("<Project><PropertyGroup><Version>1.2</Version></PropertyGroup></Project>", "test.props"));
                    Assert.Throws<PublisherException>(() => VersionResolver.ReadFromXml("<Project>", "test.props"));
                }),

                TestCases.Create(SuiteId, "VersionNumericAndPrerelease", "Numeric versions drop suffixes; 0.x and suffixed versions are pre-releases", () =>
                {
                    Assert.Equal("0.1.1.0", VersionResolver.ToNumericVersion("0.1.1"));
                    Assert.Equal("2.3.4.0", VersionResolver.ToNumericVersion("2.3.4-beta.2+build.5"));
                    Assert.True(VersionResolver.IsPrerelease("0.1.1"), "0.x is alpha");
                    Assert.True(VersionResolver.IsPrerelease("1.0.0-rc.1"), "suffix");
                    Assert.False(VersionResolver.IsPrerelease("1.0.0"), "stable");
                    Assert.False(VersionResolver.IsPrerelease("1.0.0+build.7"), "build metadata is not a pre-release");
                }),

                TestCases.Create(SuiteId, "DeterministicGuidMatchesRfc4122", "Version 5 UUIDs match the RFC 4122 reference value and are stable", () =>
                {
                    Guid python = DeterministicGuid.Create(DeterministicGuid.DnsNamespace, "python.org");
                    Assert.Equal(new Guid("886313e1-3b8a-5372-9b90-0c9aee199e5d"), python);

                    Guid first = InnoScriptBuilder.AppIdFor("ai.latticetech.s3drive");
                    Guid second = InnoScriptBuilder.AppIdFor("ai.latticetech.s3drive");
                    Assert.Equal(first, second);
                    Assert.Equal('5', first.ToString("D")[14]);
                    Assert.True(first != InnoScriptBuilder.AppIdFor("ai.latticetech.mux"), "different identifiers give different AppIds");
                }),

                TestCases.Create(SuiteId, "OptionsParse", "Command-line options parse runtimes, versions, and reject unknown flags", () =>
                {
                    PublisherOptions options = PublisherOptions.Parse(new[] { "--channel", "zip", "--rid", "win-x64,win-arm64", "--rid", "win-x64", "--version", "1.0.0" });
                    Assert.Equal("zip", options.Channel);
                    Assert.True(options.Runtimes.SequenceEqual(new[] { "win-x64", "win-arm64" }), "runtimes de-duplicated in order");
                    Assert.Equal("1.0.0", options.Version);
                    Assert.Throws<PublisherException>(() => PublisherOptions.Parse(new[] { "--channel" }));
                    Assert.Throws<PublisherException>(() => PublisherOptions.Parse(new[] { "--bogus" }));
                    Assert.Throws<PublisherException>(() => PublisherOptions.Parse(Array.Empty<string>()));
                    Assert.True(PublisherOptions.Parse(new[] { "--list-channels" }).ListChannels, "list");
                }),

                TestCases.Create(SuiteId, "LayoutFileNames", "Output names follow <app>-<version>-<rid>", () =>
                {
                    PublisherLayout layout = new PublisherLayout(Path.GetTempPath(), "s3drive", "0.1.1");
                    Assert.Equal("s3drive-0.1.1-win-x64.zip", layout.ZipFileName("win-x64"));
                    Assert.Equal("s3drive-0.1.1-win-arm64-setup", layout.SetupBaseName("win-arm64"));
                    Assert.Equal("s3drive.ico", layout.IconFileName);
                    Assert.True(layout.InstallersDirectory.EndsWith(Path.Combine("installers", "0.1.1"), StringComparison.Ordinal), layout.InstallersDirectory);
                    Assert.Equal("SHA256SUMS", Path.GetFileName(layout.ChecksumsFile));
                }),

                TestCases.Create(SuiteId, "PublishArgumentsAreSelfContainedSingleFile", "dotnet publish runs self-contained, single-file, per runtime, stamped with the version", () =>
                {
                    BuildSettings build = new BuildSettings { Framework = "net8.0", Configuration = "Release" };
                    List<string> args = PublishChannel.PublishArguments("app.csproj", build, "win-arm64", "0.1.1", "out");
                    string joined = string.Join(" ", args);
                    Assert.Contains(joined, "-r win-arm64");
                    Assert.Contains(joined, "-f net8.0");
                    Assert.Contains(joined, "--self-contained true");
                    Assert.Contains(joined, "-p:PublishSingleFile=true");
                    Assert.Contains(joined, "-p:IncludeNativeLibrariesForSelfExtract=true");
                    Assert.Contains(joined, "-p:DebugType=none");
                    Assert.Contains(joined, "-p:Version=0.1.1");
                }),

                TestCases.Create(SuiteId, "StageHelpers", "Stage helpers write the command shim, normalize line endings, and remove excluded files", () =>
                {
                    Assert.Equal("@echo off\r\n\"%~dp0S3Drive.Tui.exe\" %*\r\nexit /b %ERRORLEVEL%\r\n", StageFiles.CommandShim("S3Drive.Tui.exe"));
                    Assert.Equal("a\r\nb\r\nc", StageFiles.ToCrlf("a\nb\r\nc"));

                    Temp.WithDir(dir =>
                    {
                        File.WriteAllText(Path.Combine(dir, "keep.exe"), "x");
                        File.WriteAllText(Path.Combine(dir, "libSkiaSharp.pdb"), "x");
                        File.WriteAllText(Path.Combine(dir, "S3Drive.Core.xml"), "x");
                        List<string> removed = StageFiles.RemoveExcluded(dir, new[] { "*.pdb", "*.xml" });
                        Assert.Equal(2, removed.Count);
                        Assert.True(File.Exists(Path.Combine(dir, "keep.exe")), "unmatched files stay");

                        PublisherManifest manifest = ManifestLoader.Parse(MinimalManifestJson());
                        string message = Capture(() => StageFiles.RequireArtifacts(dir, manifest, "win-x64"));
                        Assert.Contains(message, "demo-agent.exe");
                        Assert.Throws<PublisherException>(() => StageFiles.RequireArtifacts(Path.Combine(dir, "missing"), manifest, "win-x64"));
                    });
                }),

                TestCases.Create(SuiteId, "ChecksumFormat", "SHA256SUMS is sorted, lowercase hex, two spaces, LF", async ct =>
                {
                    Dictionary<string, string> hashes = new Dictionary<string, string> { { "b.zip", "bb" }, { "a.exe", "aa" } };
                    Assert.Equal("aa  a.exe\nbb  b.zip\n", ChecksumsChannel.Format(hashes));

                    await Temp.WithDirAsync(async dir =>
                    {
                        string file = Path.Combine(dir, "abc.txt");
                        await File.WriteAllTextAsync(file, "abc", new UTF8Encoding(false), ct).ConfigureAwait(false);
                        string hash = await ChecksumsChannel.HashFileAsync(file, ct).ConfigureAwait(false);
                        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", hash);
                    }).ConfigureAwait(false);
                }),

                TestCases.Create(SuiteId, "InnoScriptForCommittedManifest", "The generated Inno script brands the wizard, requires the license, sets PATH and startup, and checks for Dokany", () =>
                {
                    PublisherManifest manifest = ManifestLoader.Load(Path.Combine(RepositoryRoot(), ManifestLoader.FileName));
                    string script = InnoScriptBuilder.Build(Input(manifest, "win-x64", null));
                    string[] lines = script.Split("\r\n");

                    Assert.False(script.Replace("\r\n", string.Empty, StringComparison.Ordinal).Contains('\n'), "CRLF line endings only");
                    AssertLine(lines, "AppId={{" + InnoScriptBuilder.AppIdFor("ai.latticetech.s3drive").ToString("D").ToUpperInvariant() + "}");
                    AssertLine(lines, "AppName=S3Drive");
                    AssertLine(lines, "AppVersion=0.1.1");
                    AssertLine(lines, "AppPublisher=Lattice AI, LLC");
                    AssertLine(lines, "AppPublisherURL=https://latticetech.ai");
                    AssertLine(lines, "VersionInfoVersion=0.1.1.0");
                    AssertLine(lines, "DisableWelcomePage=no");
                    AssertLine(lines, "WizardStyle=modern");
                    AssertLine(lines, "PrivilegesRequired=admin");
                    AssertLine(lines, "ArchitecturesAllowed=x64os");
                    AssertLine(lines, "ArchitecturesInstallIn64BitMode=x64os");
                    AssertLine(lines, "ChangesEnvironment=yes");
                    AssertLine(lines, "UninstallDisplayIcon={app}\\s3drive.ico");
                    Assert.True(lines.Any(l => l.StartsWith("LicenseFile=", StringComparison.Ordinal) && l.EndsWith("LICENSE.txt", StringComparison.Ordinal)), "license page");
                    Assert.True(lines.Any(l => l.StartsWith("WizardImageFile=", StringComparison.Ordinal) && l.Contains(',')), "high-DPI wizard images");
                    Assert.True(lines.Any(l => l.StartsWith("WelcomeLabel2=Your S3 bucket as a local Windows drive.%n%n", StringComparison.Ordinal)), "tagline on the welcome page");
                    Assert.Contains(script, "Check: NeedsAddPath('{app}'); Tasks: addtopath");
                    Assert.Contains(script, "ValueName: \"S3Drive Agent\"; ValueData: \"\"\"{app}\\S3Drive.Agent.exe\"\"\"; Flags: uninsdeletevalue; Tasks: startup0");
                    Assert.Contains(script, "Root: HKCU; Subkey: \"Software\\Microsoft\\Windows\\CurrentVersion\\Run\"; ValueType: none; ValueName: \"S3DriveAgent\"; Flags: deletevalue");
                    Assert.Contains(script, "Name: \"{autoprograms}\\S3Drive\"; Filename: \"{app}\\S3Drive.Tui.exe\"");
                    Assert.Contains(script, "Flags: nowait postinstall skipifsilent runasoriginaluser");
                    Assert.Contains(script, "FileExists(ExpandConstant('{sys}\\dokan2.dll'))");
                    Assert.Contains(script, "Parameters: \"/F /IM \"\"S3Drive.Agent.exe\"\"\"");
                    Assert.Contains(script, "RemovePath(ExpandConstant('{app}'));");
                    Assert.False(script.Contains("SignTool=", StringComparison.Ordinal), "unsigned by default");
                    Assert.False(script.Contains("signonce", StringComparison.Ordinal), "no file signing by default");
                }),

                TestCases.Create(SuiteId, "InnoScriptArchitecturesAndSigning", "ARM64 builds install only on ARM64; signing adds SignTool and per-file signing", () =>
                {
                    PublisherManifest manifest = ManifestLoader.Parse(MinimalManifestJson());
                    string arm = InnoScriptBuilder.Build(Input(manifest, "win-arm64", "publisher"));
                    string[] lines = arm.Split("\r\n");
                    AssertLine(lines, "ArchitecturesAllowed=arm64");
                    AssertLine(lines, "ArchitecturesInstallIn64BitMode=arm64");
                    AssertLine(lines, "SignTool=publisher");
                    AssertLine(lines, "SignedUninstaller=yes");
                    Assert.Contains(arm, "Source: \"demo-agent.exe\"; DestDir: \"{app}\"; Flags: ignoreversion signonce");
                    Assert.Contains(arm, "Excludes: \"\\demo-agent.exe\"");
                    Assert.Throws<PublisherException>(() => InnoScriptBuilder.Build(Input(manifest, "linux-x64", null)));
                }),

                TestCases.Create(SuiteId, "InnoEscaping", "Inno values are escaped or rejected so manifest text cannot break the script", () =>
                {
                    Assert.Equal("one%ntwo 50%% done", InnoScriptBuilder.MessageText("one\r\ntwo 50% done"));
                    Assert.Equal("\"say \"\"hi\"\"\"", InnoScriptBuilder.Quote("say \"hi\""));
                    Assert.Equal("'it''s'", InnoScriptBuilder.PascalString("it's"));

                    PublisherManifest manifest = ManifestLoader.Parse(MinimalManifestJson());
                    manifest.Project.Name = "Demo {app}";
                    Assert.Throws<PublisherException>(() => InnoScriptBuilder.Build(Input(manifest, "win-x64", null)));
                })
            };

            return new TestSuiteDescriptor(SuiteId, "S3Drive.Publisher manifest, naming, and Inno script generation", cases);
        }

        private static InnoScriptInput Input(PublisherManifest manifest, string runtime, string? signTool)
        {
            string work = Path.Combine(Path.GetTempPath(), "s3drive-publisher-test");
            return new InnoScriptInput
            {
                Manifest = manifest,
                Version = "0.1.1",
                Runtime = runtime,
                StageDirectory = Path.Combine(work, runtime, "stage"),
                OutputDirectory = Path.Combine(work, "installers"),
                OutputBaseFilename = "demo-0.1.1-" + runtime + "-setup",
                LicenseFile = Path.Combine(work, runtime, "stage", "LICENSE.txt"),
                SetupIconFile = Path.Combine(work, runtime, "stage", "s3drive.ico"),
                InstalledIconFileName = "s3drive.ico",
                Images = new WizardImageSet
                {
                    LargeImages = new List<string> { Path.Combine(work, "wizard-100.bmp"), Path.Combine(work, "wizard-200.bmp") },
                    SmallImages = new List<string> { Path.Combine(work, "wizard-small-100.bmp") }
                },
                SignToolName = signTool
            };
        }

        private static void AssertLine(string[] lines, string expected)
        {
            Assert.True(lines.Contains(expected, StringComparer.Ordinal), "Expected the script to contain the line '" + expected + "'.");
        }

        private static string Capture(Action action)
        {
            try
            {
                action();
            }
            catch (PublisherException ex)
            {
                return ex.Message;
            }

            throw new AssertException("Expected PublisherException but nothing was thrown.");
        }

        private static string RepositoryRoot()
        {
            return PublisherRunner.FindRoot(AppContext.BaseDirectory);
        }

        private static string MinimalManifestJson()
        {
            return @"{
  // comments and trailing commas are allowed
  ""project"": {
    ""name"": ""Demo"",
    ""packageName"": ""demo"",
    ""identifier"": ""ai.latticetech.demo"",
    ""tagline"": ""A demo."",
    ""license"": ""MIT"",
    ""licenseFile"": ""LICENSE.md"",
    ""logo"": ""assets/logo.png"",
    ""publisher"": ""Lattice AI, LLC"",
  },
  ""build"": { ""framework"": ""net8.0"", ""runtimes"": [ ""win-x64"", ""win-arm64"" ] },
  ""artifacts"": [
    { ""name"": ""Demo Agent"", ""project"": ""src/Demo/Demo.csproj"", ""kind"": ""TrayAgent"", ""executable"": ""demo-agent.exe"", ""startup"": ""Demo Agent"" }
  ],
  ""channels"": [ ""publish"", ""zip"" ],
  ""signing"": { ""windows"": { ""isConfigured"": false } }
}";
        }
    }
}
