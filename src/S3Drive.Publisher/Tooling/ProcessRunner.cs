namespace S3Drive.Publisher.Tooling
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Runs external tools (dotnet, iscc), streaming their output to the Publisher's output.
    /// Safe to use from one caller at a time.
    /// </summary>
    public class ProcessRunner
    {
        private readonly TextWriter _Output;
        private readonly object _OutputLock = new object();

        /// <summary>
        /// Initializes the runner.
        /// </summary>
        /// <param name="output">Where tool output and command echoes go.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="output"/> is null.</exception>
        public ProcessRunner(TextWriter output)
        {
            _Output = output ?? throw new ArgumentNullException(nameof(output));
        }

        /// <summary>
        /// Runs a tool to completion and returns its exit code.
        /// </summary>
        /// <param name="fileName">The executable.</param>
        /// <param name="arguments">The arguments, passed without shell parsing.</param>
        /// <param name="workingDirectory">The working directory.</param>
        /// <param name="secrets">Values masked in the echoed command line. Optional; null masks nothing.</param>
        /// <param name="token">Cancels the run and kills the process tree.</param>
        /// <returns>The exit code.</returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="fileName"/> or <paramref name="workingDirectory"/> is null or empty.</exception>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="arguments"/> is null.</exception>
        /// <exception cref="PublisherException">Thrown when the tool cannot be started.</exception>
        /// <exception cref="OperationCanceledException">Thrown when <paramref name="token"/> is canceled.</exception>
        public async Task<int> RunAsync(string fileName, IReadOnlyList<string> arguments, string workingDirectory, IReadOnlyCollection<string>? secrets, CancellationToken token)
        {
            if (string.IsNullOrEmpty(fileName)) throw new ArgumentException("Executable must be provided.", nameof(fileName));
            if (arguments == null) throw new ArgumentNullException(nameof(arguments));
            if (string.IsNullOrEmpty(workingDirectory)) throw new ArgumentException("Working directory must be provided.", nameof(workingDirectory));
            token.ThrowIfCancellationRequested();

            ProcessStartInfo info = new ProcessStartInfo(fileName)
            {
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            foreach (string argument in arguments)
            {
                info.ArgumentList.Add(argument);
            }

            WriteLine("> " + Describe(fileName, arguments, secrets));

            using (Process process = new Process())
            {
                process.StartInfo = info;
                process.OutputDataReceived += (sender, e) =>
                {
                    if (e.Data != null) WriteLine(e.Data);
                };
                process.ErrorDataReceived += (sender, e) =>
                {
                    if (e.Data != null) WriteLine(e.Data);
                };

                try
                {
                    process.Start();
                }
                catch (Win32Exception ex)
                {
                    throw new PublisherException("Could not start " + fileName + ": " + ex.Message, ex);
                }

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                try
                {
                    await process.WaitForExitAsync(token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    try
                    {
                        process.Kill(true);
                    }
                    catch (InvalidOperationException)
                    {
                    }

                    throw;
                }

                process.WaitForExit();
                return process.ExitCode;
            }
        }

        /// <summary>
        /// Formats a command line for display, quoting arguments with spaces and masking secrets.
        /// </summary>
        /// <param name="fileName">The executable.</param>
        /// <param name="arguments">The arguments.</param>
        /// <param name="secrets">Values to replace with ***. Optional.</param>
        /// <returns>The display string.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="fileName"/> or <paramref name="arguments"/> is null.</exception>
        public static string Describe(string fileName, IReadOnlyList<string> arguments, IReadOnlyCollection<string>? secrets)
        {
            if (fileName == null) throw new ArgumentNullException(nameof(fileName));
            if (arguments == null) throw new ArgumentNullException(nameof(arguments));

            IEnumerable<string> parts = new[] { fileName }.Concat(arguments).Select(part =>
            {
                string shown = part;
                if (secrets != null)
                {
                    foreach (string secret in secrets.Where(s => !string.IsNullOrEmpty(s)))
                    {
                        shown = shown.Replace(secret, "***", StringComparison.Ordinal);
                    }
                }

                return shown.Contains(' ') ? "\"" + shown + "\"" : shown;
            });
            return string.Join(" ", parts);
        }

        private void WriteLine(string line)
        {
            lock (_OutputLock)
            {
                _Output.WriteLine(line);
            }
        }
    }
}
