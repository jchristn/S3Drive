namespace S3Drive.Core.Ipc
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using S3Drive.Core.Configuration;
    using S3Drive.Core.Diagnostics;
    using S3Drive.Core.Telemetry;

    /// <summary>
    /// Drains the agent's command channel: reads each pending command file, executes it, and
    /// deletes it. Each command is one instrumented pipeline job: a <c>command &lt;Type&gt;</c>
    /// consumer span (joined to the sender's trace when the command carries a <c>traceparent</c>)
    /// with <c>stage:queued</c>, <c>stage:parse</c>, and <c>stage:execute</c> children, plus the
    /// <c>s3drive.command.*</c> job, per-stage, last-success, and queue-depth metrics.
    /// </summary>
    public static class CommandDispatcher
    {
        /// <summary>
        /// Processes every pending command once, oldest first. A failing command is logged and
        /// deleted; it never stops the remaining commands from running.
        /// </summary>
        /// <param name="paths">The path resolver. Cannot be null.</param>
        /// <param name="executor">Executes one parsed command. Cannot be null. An exception marks the command failed.</param>
        /// <param name="token">A cancellation token passed to the executor.</param>
        /// <returns>The number of command files processed (including unreadable ones that were discarded).</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="paths"/> or <paramref name="executor"/> is null.</exception>
        public static async Task<int> DrainAsync(S3DrivePaths paths, Func<AgentCommand, CancellationToken, Task> executor, CancellationToken token)
        {
            if (paths == null) throw new ArgumentNullException(nameof(paths));
            if (executor == null) throw new ArgumentNullException(nameof(executor));

            IReadOnlyList<string> pending = CommandChannel.ListPending(paths);
            S3DriveTelemetry.RecordPoll(pending.Count);

            foreach (string file in pending)
            {
                await ProcessAsync(file, executor, token).ConfigureAwait(false);
            }

            return pending.Count;
        }

        private static async Task ProcessAsync(string file, Func<AgentCommand, CancellationToken, Task> executor, CancellationToken token)
        {
            DateTimeOffset pickedUp = DateTimeOffset.UtcNow;
            DateTimeOffset written = FileWrittenUtc(file, pickedUp);

            long parseStart = Stopwatch.GetTimestamp();
            bool parsed = CommandChannel.TryRead(file, out AgentCommand? command) && command != null;
            DateTimeOffset parsedAt = pickedUp + Stopwatch.GetElapsedTime(parseStart);

            string commandType = parsed ? command!.CommandType.ToString() : TelemetryNames.CommandTypeUnknown;
            DateTimeOffset queuedSince = parsed && command!.CreatedUtc != null
                ? new DateTimeOffset(DateTime.SpecifyKind(command.CreatedUtc.Value, DateTimeKind.Utc))
                : written;
            if (queuedSince > pickedUp) queuedSince = pickedUp;

            using (Activity? root = S3DriveTelemetry.StartActivity(TelemetryNames.SpanCommandPrefix + commandType, ActivityKind.Consumer, parsed ? command!.TraceParent : null, queuedSince))
            {
                root?.SetTag(TelemetryNames.AttrCommandType, commandType);
                if (parsed && !string.IsNullOrEmpty(command!.DriveId)) root?.SetTag(TelemetryNames.AttrDriveId, command.DriveId);

                S3DriveTelemetry.RecordCommandStage(commandType, TelemetryNames.StageQueued, TelemetryNames.OutcomeSuccess, queuedSince, pickedUp);
                S3DriveTelemetry.RecordCommandStage(commandType, TelemetryNames.StageParse, parsed ? TelemetryNames.OutcomeSuccess : TelemetryNames.OutcomeError, pickedUp, parsedAt);

                if (!parsed)
                {
                    S3DriveLog.Warn("Discarded unreadable command file " + Path.GetFileName(file));
                    S3DriveTelemetry.RecordCommandJob(commandType, TelemetryNames.OutcomeError);
                    root?.SetTag(TelemetryNames.AttrOutcome, TelemetryNames.OutcomeError);
                    root?.SetStatus(ActivityStatusCode.Error, "unreadable command file");
                    TryDelete(file);
                    return;
                }

                string outcome;
                using (TelemetryScope stage = S3DriveTelemetry.StartCommandStage(commandType, TelemetryNames.StageExecute))
                {
                    try
                    {
                        await executor(command!, token).ConfigureAwait(false);
                        stage.Complete(TelemetryNames.OutcomeSuccess);
                        outcome = TelemetryNames.OutcomeSuccess;
                    }
                    catch (Exception ex)
                    {
                        stage.Fail(ex);
                        outcome = S3DriveTelemetry.OutcomeFor(ex);
                        S3DriveTelemetry.AddException(root, ex);
                        S3DriveLog.Error("Command " + command!.CommandType + " failed: " + ex.Message);
                    }
                    finally
                    {
                        TryDelete(file);
                    }
                }

                S3DriveTelemetry.RecordCommandJob(commandType, outcome);
                if (root != null)
                {
                    root.SetTag(TelemetryNames.AttrOutcome, outcome);
                    if (string.Equals(outcome, TelemetryNames.OutcomeSuccess, StringComparison.Ordinal)) root.SetStatus(ActivityStatusCode.Ok);
                    else root.SetStatus(ActivityStatusCode.Error, outcome);
                }
            }
        }

        private static DateTimeOffset FileWrittenUtc(string file, DateTimeOffset fallback)
        {
            try
            {
                DateTime written = File.GetLastWriteTimeUtc(file);
                if (written.Year < 2000) return fallback;
                return new DateTimeOffset(written, TimeSpan.Zero);
            }
            catch (Exception)
            {
                return fallback;
            }
        }

        private static void TryDelete(string file)
        {
            try
            {
                File.Delete(file);
            }
            catch (Exception ex)
            {
                S3DriveLog.Warn("Could not delete command file " + Path.GetFileName(file) + ": " + ex.Message);
            }
        }
    }
}
