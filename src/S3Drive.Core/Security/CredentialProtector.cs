namespace S3Drive.Core.Security
{
    using System;
    using System.IO;
    using System.Security.Cryptography;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Encrypts and decrypts secret values (such as S3 secret keys) using a machine-local
    /// 256-bit key stored with owner-only permissions. The key file is created on first use and
    /// cached in memory. Thread-safe.
    /// </summary>
    public class CredentialProtector
    {
        private const string AssociatedData = "s3drive-credential";

        private readonly string _KeyFilePath;
        private readonly SemaphoreSlim _KeyLock = new SemaphoreSlim(1, 1);
        private byte[]? _Key;

        /// <summary>
        /// Initializes a new instance backed by the given key file.
        /// </summary>
        /// <param name="keyFilePath">The path to the machine-local key file. Cannot be null or empty.</param>
        /// <exception cref="ArgumentException">Thrown when <paramref name="keyFilePath"/> is null or empty.</exception>
        public CredentialProtector(string keyFilePath)
        {
            if (string.IsNullOrEmpty(keyFilePath)) throw new ArgumentException("Key file path must be provided.", nameof(keyFilePath));
            _KeyFilePath = keyFilePath;
        }

        /// <summary>
        /// The path to the machine-local key file.
        /// </summary>
        public string KeyFilePath
        {
            get { return _KeyFilePath; }
        }

        /// <summary>
        /// Encrypts a plaintext value and returns a base64-encoded protected string.
        /// </summary>
        /// <param name="plaintext">The plaintext value. Cannot be null.</param>
        /// <param name="token">A cancellation token.</param>
        /// <returns>The base64-encoded protected value. Never null.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="plaintext"/> is null.</exception>
        public async Task<string> ProtectAsync(string plaintext, CancellationToken token = default)
        {
            if (plaintext == null) throw new ArgumentNullException(nameof(plaintext));

            byte[] key = await EnsureKeyAsync(token).ConfigureAwait(false);
            byte[] frame = AesGcmCipher.Encrypt(key, Encoding.UTF8.GetBytes(plaintext), Encoding.UTF8.GetBytes(AssociatedData));
            return Convert.ToBase64String(frame);
        }

        /// <summary>
        /// Decrypts a base64-encoded protected string produced by <see cref="ProtectAsync"/>.
        /// </summary>
        /// <param name="protectedValue">The protected value. Cannot be null.</param>
        /// <param name="token">A cancellation token.</param>
        /// <returns>The recovered plaintext. Never null.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="protectedValue"/> is null.</exception>
        /// <exception cref="S3DriveCryptoException">Thrown when the value is malformed or fails authentication.</exception>
        public async Task<string> UnprotectAsync(string protectedValue, CancellationToken token = default)
        {
            if (protectedValue == null) throw new ArgumentNullException(nameof(protectedValue));

            byte[] key = await EnsureKeyAsync(token).ConfigureAwait(false);
            byte[] frame;
            try
            {
                frame = Convert.FromBase64String(protectedValue);
            }
            catch (FormatException ex)
            {
                throw new S3DriveCryptoException("Protected value is not valid base64.", ex);
            }

            byte[] plaintext = AesGcmCipher.Decrypt(key, frame, Encoding.UTF8.GetBytes(AssociatedData));
            return Encoding.UTF8.GetString(plaintext);
        }

        private async Task<byte[]> EnsureKeyAsync(CancellationToken token)
        {
            if (_Key != null) return _Key;

            await _KeyLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (_Key != null) return _Key;

                if (File.Exists(_KeyFilePath))
                {
                    byte[] existing = await File.ReadAllBytesAsync(_KeyFilePath, token).ConfigureAwait(false);
                    if (existing.Length != AesGcmCipher.KeyLengthBytes)
                        throw new S3DriveCryptoException("Machine key file is corrupt or has an unexpected length.");
                    _Key = existing;
                    return _Key;
                }

                string? directory = Path.GetDirectoryName(Path.GetFullPath(_KeyFilePath));
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

                byte[] generated = RandomNumberGenerator.GetBytes(AesGcmCipher.KeyLengthBytes);
                using (FileStream stream = new FileStream(_KeyFilePath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    await stream.WriteAsync(generated.AsMemory(0, generated.Length), token).ConfigureAwait(false);
                }

                RestrictPermissions(_KeyFilePath);
                _Key = generated;
                return _Key;
            }
            finally
            {
                _KeyLock.Release();
            }
        }

        private static void RestrictPermissions(string path)
        {
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }
}
