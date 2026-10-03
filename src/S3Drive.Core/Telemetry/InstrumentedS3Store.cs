namespace S3Drive.Core.Telemetry
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using S3Drive.Core.Configuration;
    using S3Drive.Core.Storage;

    /// <summary>
    /// An <see cref="IS3Store"/> decorator that wraps every outbound storage call in a client span
    /// (<c>S3 &lt;Operation&gt;</c>) and records the <c>s3drive.s3.*</c> request, latency, and byte
    /// metrics. Behavior and exceptions of the inner store are preserved exactly; telemetry is
    /// best-effort and never alters the result.
    /// </summary>
    public sealed class InstrumentedS3Store : IS3Store
    {
        private readonly IS3Store _Inner;
        private readonly string _Drive;
        private readonly string _Provider;
        private readonly string? _Bucket;

        /// <summary>
        /// Initializes a new decorator.
        /// </summary>
        /// <param name="inner">The store to instrument. Cannot be null.</param>
        /// <param name="driveLetter">The drive letter used as the <c>s3drive.drive</c> label. Null or invalid values report <c>unknown</c>.</param>
        /// <param name="provider">The S3 provider kind, reported as the <c>s3drive.provider</c> label.</param>
        /// <param name="bucket">The bucket name, recorded on spans only (never a metric label). May be null.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="inner"/> is null.</exception>
        public InstrumentedS3Store(IS3Store inner, string? driveLetter, S3ProviderEnum provider, string? bucket)
        {
            _Inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _Drive = S3DriveTelemetry.NormalizeDrive(driveLetter);
            _Provider = provider == S3ProviderEnum.AwsS3 ? "aws_s3" : "s3_compatible";
            _Bucket = bucket;
        }

        /// <summary>
        /// The wrapped store. Never null.
        /// </summary>
        public IS3Store Inner
        {
            get { return _Inner; }
        }

        /// <inheritdoc />
        public async Task<bool> ExistsAsync(string key, CancellationToken token)
        {
            using (TelemetryScope scope = Start(TelemetryNames.S3ObjectExists, key))
            {
                try
                {
                    bool exists = await _Inner.ExistsAsync(key, token).ConfigureAwait(false);
                    scope.SetTag("s3drive.exists", exists);
                    scope.Complete(TelemetryNames.OutcomeSuccess);
                    return exists;
                }
                catch (Exception ex)
                {
                    scope.Fail(ex);
                    throw;
                }
            }
        }

        /// <inheritdoc />
        public async Task<S3Entry?> HeadAsync(string key, CancellationToken token)
        {
            using (TelemetryScope scope = Start(TelemetryNames.S3HeadObject, key))
            {
                try
                {
                    S3Entry? entry = await _Inner.HeadAsync(key, token).ConfigureAwait(false);
                    if (entry != null) scope.SetTag(TelemetryNames.AttrBytes, entry.SizeBytes);
                    scope.Complete(entry == null ? TelemetryNames.OutcomeNotFound : TelemetryNames.OutcomeSuccess);
                    return entry;
                }
                catch (Exception ex)
                {
                    scope.Fail(ex);
                    throw;
                }
            }
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<S3Entry>> ListAsync(string prefix, CancellationToken token)
        {
            using (TelemetryScope scope = Start(TelemetryNames.S3ListObjects, prefix))
            {
                try
                {
                    IReadOnlyList<S3Entry> entries = await _Inner.ListAsync(prefix, token).ConfigureAwait(false);
                    scope.SetTag(TelemetryNames.AttrCount, entries.Count);
                    scope.Complete(TelemetryNames.OutcomeSuccess);
                    return entries;
                }
                catch (Exception ex)
                {
                    scope.Fail(ex);
                    throw;
                }
            }
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<string>> ListAllKeysAsync(string prefix, CancellationToken token)
        {
            using (TelemetryScope scope = Start(TelemetryNames.S3ListAllKeys, prefix))
            {
                try
                {
                    IReadOnlyList<string> keys = await _Inner.ListAllKeysAsync(prefix, token).ConfigureAwait(false);
                    scope.SetTag(TelemetryNames.AttrCount, keys.Count);
                    scope.Complete(TelemetryNames.OutcomeSuccess);
                    return keys;
                }
                catch (Exception ex)
                {
                    scope.Fail(ex);
                    throw;
                }
            }
        }

        /// <inheritdoc />
        public async Task<byte[]> GetAsync(string key, CancellationToken token)
        {
            using (TelemetryScope scope = Start(TelemetryNames.S3GetObject, key))
            {
                try
                {
                    byte[] data = await _Inner.GetAsync(key, token).ConfigureAwait(false);
                    scope.SetTag(TelemetryNames.AttrBytes, data.LongLength);
                    S3DriveTelemetry.RecordS3Bytes(_Drive, "download", data.LongLength);
                    scope.Complete(TelemetryNames.OutcomeSuccess);
                    return data;
                }
                catch (Exception ex)
                {
                    scope.Fail(ex);
                    throw;
                }
            }
        }

        /// <inheritdoc />
        public async Task GetToFileAsync(string key, string destinationPath, CancellationToken token)
        {
            using (TelemetryScope scope = Start(TelemetryNames.S3GetObject, key))
            {
                try
                {
                    await _Inner.GetToFileAsync(key, destinationPath, token).ConfigureAwait(false);
                    long bytes = FileLength(destinationPath);
                    scope.SetTag(TelemetryNames.AttrBytes, bytes);
                    S3DriveTelemetry.RecordS3Bytes(_Drive, "download", bytes);
                    scope.Complete(TelemetryNames.OutcomeSuccess);
                }
                catch (Exception ex)
                {
                    scope.Fail(ex);
                    throw;
                }
            }
        }

        /// <inheritdoc />
        public async Task PutAsync(string key, byte[] data, CancellationToken token)
        {
            using (TelemetryScope scope = Start(TelemetryNames.S3PutObject, key))
            {
                try
                {
                    await _Inner.PutAsync(key, data, token).ConfigureAwait(false);
                    long bytes = data == null ? 0 : data.LongLength;
                    scope.SetTag(TelemetryNames.AttrBytes, bytes);
                    S3DriveTelemetry.RecordS3Bytes(_Drive, "upload", bytes);
                    scope.Complete(TelemetryNames.OutcomeSuccess);
                }
                catch (Exception ex)
                {
                    scope.Fail(ex);
                    throw;
                }
            }
        }

        /// <inheritdoc />
        public async Task PutFromFileAsync(string key, string sourcePath, CancellationToken token)
        {
            using (TelemetryScope scope = Start(TelemetryNames.S3PutObject, key))
            {
                try
                {
                    long bytes = FileLength(sourcePath);
                    await _Inner.PutFromFileAsync(key, sourcePath, token).ConfigureAwait(false);
                    scope.SetTag(TelemetryNames.AttrBytes, bytes);
                    S3DriveTelemetry.RecordS3Bytes(_Drive, "upload", bytes);
                    scope.Complete(TelemetryNames.OutcomeSuccess);
                }
                catch (Exception ex)
                {
                    scope.Fail(ex);
                    throw;
                }
            }
        }

        /// <inheritdoc />
        public async Task DeleteAsync(string key, CancellationToken token)
        {
            using (TelemetryScope scope = Start(TelemetryNames.S3DeleteObject, key))
            {
                try
                {
                    await _Inner.DeleteAsync(key, token).ConfigureAwait(false);
                    scope.Complete(TelemetryNames.OutcomeSuccess);
                }
                catch (Exception ex)
                {
                    scope.Fail(ex);
                    throw;
                }
            }
        }

        /// <inheritdoc />
        public async Task DeleteManyAsync(IReadOnlyCollection<string> keys, CancellationToken token)
        {
            using (TelemetryScope scope = Start(TelemetryNames.S3DeleteObjects, null))
            {
                try
                {
                    if (keys != null) scope.SetTag(TelemetryNames.AttrCount, keys.Count);
                    await _Inner.DeleteManyAsync(keys!, token).ConfigureAwait(false);
                    scope.Complete(TelemetryNames.OutcomeSuccess);
                }
                catch (Exception ex)
                {
                    scope.Fail(ex);
                    throw;
                }
            }
        }

        /// <inheritdoc />
        public async Task CopyAsync(string sourceKey, string destinationKey, CancellationToken token)
        {
            using (TelemetryScope scope = Start(TelemetryNames.S3CopyObject, sourceKey))
            {
                try
                {
                    if (S3DriveTelemetry.IncludeObjectKeys) scope.SetTag(TelemetryNames.AttrObjectDestination, destinationKey);
                    await _Inner.CopyAsync(sourceKey, destinationKey, token).ConfigureAwait(false);
                    scope.Complete(TelemetryNames.OutcomeSuccess);
                }
                catch (Exception ex)
                {
                    scope.Fail(ex);
                    throw;
                }
            }
        }

        /// <inheritdoc />
        public async Task<bool> ValidateConnectivityAsync(CancellationToken token)
        {
            using (TelemetryScope scope = Start(TelemetryNames.S3ValidateConnectivity, null))
            {
                try
                {
                    bool ok = await _Inner.ValidateConnectivityAsync(token).ConfigureAwait(false);
                    scope.SetTag("s3drive.connected", ok);
                    if (!ok) scope.SetErrorType("ConnectivityFailed");
                    scope.Complete(ok ? TelemetryNames.OutcomeSuccess : TelemetryNames.OutcomeError);
                    return ok;
                }
                catch (Exception ex)
                {
                    scope.Fail(ex);
                    throw;
                }
            }
        }

        private TelemetryScope Start(string operation, string? key)
        {
            TelemetryScope scope = S3DriveTelemetry.StartS3(_Drive, _Provider, operation);
            if (_Bucket != null) scope.SetTag(TelemetryNames.AttrBucket, _Bucket);
            if (key != null) scope.SetObjectKey(key);
            return scope;
        }

        private static long FileLength(string path)
        {
            try
            {
                return File.Exists(path) ? new FileInfo(path).Length : 0;
            }
            catch (Exception)
            {
                return 0;
            }
        }
    }
}
