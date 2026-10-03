namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Security.Cryptography;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using S3Drive.Core.Security;
    using Test.Shared.Helpers;
    using Touchstone.Core;

    /// <summary>
    /// Tests for <see cref="AesGcmCipher"/> and <see cref="CredentialProtector"/>.
    /// </summary>
    public static class CryptoSuite
    {
        private const string SuiteId = "Crypto";

        /// <summary>
        /// Builds the suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>
            {
                TestCases.Create(SuiteId, "AesGcmRoundTrip", "AesGcm encrypt and decrypt round-trip", () =>
                {
                    byte[] key = RandomNumberGenerator.GetBytes(32);
                    byte[] plaintext = Encoding.UTF8.GetBytes("secret-value");
                    byte[] frame = AesGcmCipher.Encrypt(key, plaintext, null);
                    byte[] recovered = AesGcmCipher.Decrypt(key, frame, null);
                    Assert.Equal("secret-value", Encoding.UTF8.GetString(recovered));
                }),

                TestCases.Create(SuiteId, "AesGcmEmptyPlaintext", "AesGcm round-trips an empty plaintext", () =>
                {
                    byte[] key = RandomNumberGenerator.GetBytes(32);
                    byte[] frame = AesGcmCipher.Encrypt(key, Array.Empty<byte>(), null);
                    Assert.Equal(1 + AesGcmCipher.NonceLengthBytes + AesGcmCipher.TagLengthBytes, frame.Length);
                    Assert.Equal(0, AesGcmCipher.Decrypt(key, frame, null).Length);
                }),

                TestCases.Create(SuiteId, "AesGcmFrameLayout", "AesGcm frame is version byte + nonce + tag + ciphertext", () =>
                {
                    byte[] key = RandomNumberGenerator.GetBytes(32);
                    byte[] frame = AesGcmCipher.Encrypt(key, new byte[100], null);
                    Assert.Equal(1 + AesGcmCipher.NonceLengthBytes + AesGcmCipher.TagLengthBytes + 100, frame.Length);
                    Assert.Equal((byte)1, frame[0]);
                }),

                TestCases.Create(SuiteId, "AesGcmUsesFreshNonce", "AesGcm produces a different frame for the same input each time", () =>
                {
                    byte[] key = RandomNumberGenerator.GetBytes(32);
                    byte[] plaintext = Encoding.UTF8.GetBytes("same");
                    string a = Convert.ToBase64String(AesGcmCipher.Encrypt(key, plaintext, null));
                    string b = Convert.ToBase64String(AesGcmCipher.Encrypt(key, plaintext, null));
                    Assert.False(a == b, "nonce must not repeat");
                }),

                TestCases.Create(SuiteId, "AesGcmWrongKeyFails", "AesGcm decrypt with the wrong key fails", () =>
                {
                    byte[] key1 = RandomNumberGenerator.GetBytes(32);
                    byte[] key2 = RandomNumberGenerator.GetBytes(32);
                    byte[] frame = AesGcmCipher.Encrypt(key1, Encoding.UTF8.GetBytes("x"), null);
                    Assert.Throws<S3DriveCryptoException>(() => AesGcmCipher.Decrypt(key2, frame, null));
                }),

                TestCases.Create(SuiteId, "AesGcmTamperedCiphertextFails", "AesGcm decrypt of a tampered ciphertext fails", () =>
                {
                    byte[] key = RandomNumberGenerator.GetBytes(32);
                    byte[] frame = AesGcmCipher.Encrypt(key, Encoding.UTF8.GetBytes("hello"), null);
                    frame[frame.Length - 1] ^= 0xFF;
                    Assert.Throws<S3DriveCryptoException>(() => AesGcmCipher.Decrypt(key, frame, null));
                }),

                TestCases.Create(SuiteId, "AesGcmTamperedNonceAndTagFail", "AesGcm decrypt fails when the nonce or tag is tampered", () =>
                {
                    byte[] key = RandomNumberGenerator.GetBytes(32);
                    byte[] nonceTampered = AesGcmCipher.Encrypt(key, Encoding.UTF8.GetBytes("hello"), null);
                    nonceTampered[1] ^= 0x01;
                    Assert.Throws<S3DriveCryptoException>(() => AesGcmCipher.Decrypt(key, nonceTampered, null));

                    byte[] tagTampered = AesGcmCipher.Encrypt(key, Encoding.UTF8.GetBytes("hello"), null);
                    tagTampered[1 + AesGcmCipher.NonceLengthBytes] ^= 0x01;
                    Assert.Throws<S3DriveCryptoException>(() => AesGcmCipher.Decrypt(key, tagTampered, null));
                }),

                TestCases.Create(SuiteId, "AesGcmRejectsBadVersionAndShortFrame", "AesGcm rejects an unknown version byte and a short frame", () =>
                {
                    byte[] key = RandomNumberGenerator.GetBytes(32);
                    Assert.Throws<S3DriveCryptoException>(() => AesGcmCipher.Decrypt(key, new byte[5], null));
                    Assert.Throws<S3DriveCryptoException>(() => AesGcmCipher.Decrypt(key, Array.Empty<byte>(), null));

                    byte[] frame = AesGcmCipher.Encrypt(key, Encoding.UTF8.GetBytes("z"), null);
                    frame[0] = 9;
                    Assert.Throws<S3DriveCryptoException>(() => AesGcmCipher.Decrypt(key, frame, null));
                }),

                TestCases.Create(SuiteId, "AesGcmEncryptRejectsBadArguments", "AesGcm encrypt rejects a bad key length and null arguments", () =>
                {
                    Assert.Throws<ArgumentException>(() => AesGcmCipher.Encrypt(new byte[10], new byte[1], null));
                    Assert.Throws<ArgumentException>(() => AesGcmCipher.Encrypt(new byte[16], new byte[1], null));
                    Assert.Throws<ArgumentNullException>(() => AesGcmCipher.Encrypt(null!, new byte[1], null));
                    Assert.Throws<ArgumentNullException>(() => AesGcmCipher.Encrypt(RandomNumberGenerator.GetBytes(32), null!, null));
                }),

                TestCases.Create(SuiteId, "AesGcmDecryptRejectsBadArguments", "AesGcm decrypt rejects a bad key length and null arguments", () =>
                {
                    byte[] key = RandomNumberGenerator.GetBytes(32);
                    byte[] frame = AesGcmCipher.Encrypt(key, new byte[1], null);
                    Assert.Throws<ArgumentException>(() => AesGcmCipher.Decrypt(new byte[31], frame, null));
                    Assert.Throws<ArgumentNullException>(() => AesGcmCipher.Decrypt(null!, frame, null));
                    Assert.Throws<ArgumentNullException>(() => AesGcmCipher.Decrypt(key, null!, null));
                }),

                TestCases.Create(SuiteId, "AesGcmBindsAssociatedData", "AesGcm binds associated data to the ciphertext", () =>
                {
                    byte[] key = RandomNumberGenerator.GetBytes(32);
                    byte[] aad = Encoding.UTF8.GetBytes("aad-1");
                    byte[] frame = AesGcmCipher.Encrypt(key, Encoding.UTF8.GetBytes("x"), aad);
                    Assert.Equal("x", Encoding.UTF8.GetString(AesGcmCipher.Decrypt(key, frame, aad)));
                    Assert.Throws<S3DriveCryptoException>(() => AesGcmCipher.Decrypt(key, frame, Encoding.UTF8.GetBytes("aad-2")));
                    Assert.Throws<S3DriveCryptoException>(() => AesGcmCipher.Decrypt(key, frame, null));
                }),

                TestCases.Create(SuiteId, "CryptoExceptionCarriesInner", "S3DriveCryptoException preserves the message and inner exception", () =>
                {
                    InvalidOperationException inner = new InvalidOperationException("inner");
                    S3DriveCryptoException ex = new S3DriveCryptoException("outer", inner);
                    Assert.Equal("outer", ex.Message);
                    Assert.True(ReferenceEquals(inner, ex.InnerException));
                }),

                TestCases.Create(SuiteId, "ProtectorRoundTripCreatesKeyFile", "CredentialProtector round-trip creates a 32-byte key file and hides the plaintext", async ct =>
                {
                    await Temp.WithDirAsync(async root =>
                    {
                        CredentialProtector protector = new CredentialProtector(Path.Combine(root, "dp.key"));
                        string protectedValue = await protector.ProtectAsync("hunter2", ct).ConfigureAwait(false);
                        Assert.True(File.Exists(protector.KeyFilePath));
                        Assert.Equal(32L, new FileInfo(protector.KeyFilePath).Length);
                        Assert.False(protectedValue.Contains("hunter2", StringComparison.Ordinal));
                        string recovered = await protector.UnprotectAsync(protectedValue, ct).ConfigureAwait(false);
                        Assert.Equal("hunter2", recovered);
                    }).ConfigureAwait(false);
                }),

                TestCases.Create(SuiteId, "ProtectorRoundTripsUnicodeAndEmpty", "CredentialProtector round-trips unicode and empty secrets", async ct =>
                {
                    await Temp.WithDirAsync(async root =>
                    {
                        CredentialProtector protector = new CredentialProtector(Path.Combine(root, "dp.key"));
                        string unicode = "pässwörd-🔑-秘密";
                        Assert.Equal(unicode, await protector.UnprotectAsync(await protector.ProtectAsync(unicode, ct).ConfigureAwait(false), ct).ConfigureAwait(false));
                        Assert.Equal(string.Empty, await protector.UnprotectAsync(await protector.ProtectAsync(string.Empty, ct).ConfigureAwait(false), ct).ConfigureAwait(false));
                    }).ConfigureAwait(false);
                }),

                TestCases.Create(SuiteId, "ProtectorCreatesKeyDirectory", "CredentialProtector creates the key file's directory when missing", async ct =>
                {
                    await Temp.WithDirAsync(async root =>
                    {
                        string keyPath = Path.Combine(root, "a", "b", "dp.key");
                        CredentialProtector protector = new CredentialProtector(keyPath);
                        await protector.ProtectAsync("x", ct).ConfigureAwait(false);
                        Assert.True(File.Exists(keyPath));
                    }).ConfigureAwait(false);
                }),

                TestCases.Create(SuiteId, "ProtectorRestrictsKeyPermissions", "CredentialProtector restricts the key file to the owner on Unix", async ct =>
                {
                    if (OperatingSystem.IsWindows()) return;

                    await Temp.WithDirAsync(async root =>
                    {
                        CredentialProtector protector = new CredentialProtector(Path.Combine(root, "dp.key"));
                        await protector.ProtectAsync("x", ct).ConfigureAwait(false);
                        if (!OperatingSystem.IsWindows())
                        {
                            UnixFileMode mode = File.GetUnixFileMode(protector.KeyFilePath);
                            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, mode);
                        }
                    }).ConfigureAwait(false);
                }),

                TestCases.Create(SuiteId, "ProtectorDifferentKeyFails", "CredentialProtector with a different key cannot unprotect", async ct =>
                {
                    await Temp.WithDirAsync(async root =>
                    {
                        CredentialProtector p1 = new CredentialProtector(Path.Combine(root, "k1"));
                        string protectedValue = await p1.ProtectAsync("x", ct).ConfigureAwait(false);
                        CredentialProtector p2 = new CredentialProtector(Path.Combine(root, "k2"));
                        await Assert.ThrowsAsync<S3DriveCryptoException>(() => p2.UnprotectAsync(protectedValue, ct)).ConfigureAwait(false);
                    }).ConfigureAwait(false);
                }),

                TestCases.Create(SuiteId, "ProtectorRejectsBadBase64", "CredentialProtector rejects a value that is not base64", async ct =>
                {
                    await Temp.WithDirAsync(async root =>
                    {
                        CredentialProtector protector = new CredentialProtector(Path.Combine(root, "k"));
                        await Assert.ThrowsAsync<S3DriveCryptoException>(() => protector.UnprotectAsync("not valid base64 !!!", ct)).ConfigureAwait(false);
                    }).ConfigureAwait(false);
                }),

                TestCases.Create(SuiteId, "ProtectorRejectsGarbageFrame", "CredentialProtector rejects valid base64 that is not a ciphertext frame", async ct =>
                {
                    await Temp.WithDirAsync(async root =>
                    {
                        CredentialProtector protector = new CredentialProtector(Path.Combine(root, "k"));
                        string garbage = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
                        await Assert.ThrowsAsync<S3DriveCryptoException>(() => protector.UnprotectAsync(garbage, ct)).ConfigureAwait(false);
                        await Assert.ThrowsAsync<S3DriveCryptoException>(() => protector.UnprotectAsync(string.Empty, ct)).ConfigureAwait(false);
                    }).ConfigureAwait(false);
                }),

                TestCases.Create(SuiteId, "ProtectorRejectsCorruptKeyFile", "CredentialProtector refuses a key file of the wrong length", async ct =>
                {
                    await Temp.WithDirAsync(async root =>
                    {
                        string keyPath = Path.Combine(root, "dp.key");
                        await File.WriteAllBytesAsync(keyPath, new byte[7], ct).ConfigureAwait(false);
                        CredentialProtector protector = new CredentialProtector(keyPath);
                        await Assert.ThrowsAsync<S3DriveCryptoException>(() => protector.ProtectAsync("x", ct)).ConfigureAwait(false);
                        Assert.Equal(7L, new FileInfo(keyPath).Length);
                    }).ConfigureAwait(false);
                }),

                TestCases.Create(SuiteId, "ProtectorRejectsBadArguments", "CredentialProtector rejects an empty key path and null values", async ct =>
                {
                    Assert.Throws<ArgumentException>(() => new CredentialProtector(string.Empty));
                    Assert.Throws<ArgumentException>(() => new CredentialProtector(null!));

                    await Temp.WithDirAsync(async root =>
                    {
                        CredentialProtector protector = new CredentialProtector(Path.Combine(root, "k"));
                        await Assert.ThrowsAsync<ArgumentNullException>(() => protector.ProtectAsync(null!, ct)).ConfigureAwait(false);
                        await Assert.ThrowsAsync<ArgumentNullException>(() => protector.UnprotectAsync(null!, ct)).ConfigureAwait(false);
                        Assert.False(File.Exists(protector.KeyFilePath), "argument validation must not create the key");
                    }).ConfigureAwait(false);
                }),

                TestCases.Create(SuiteId, "ProtectorHonorsCancellation", "CredentialProtector honors a canceled token before creating a key", async ct =>
                {
                    await Temp.WithDirAsync(async root =>
                    {
                        CredentialProtector protector = new CredentialProtector(Path.Combine(root, "k"));
                        using (CancellationTokenSource cts = new CancellationTokenSource())
                        {
                            cts.Cancel();
                            await Assert.ThrowsAsync<OperationCanceledException>(() => protector.ProtectAsync("x", cts.Token)).ConfigureAwait(false);
                        }

                        Assert.False(File.Exists(protector.KeyFilePath));
                    }).ConfigureAwait(false);
                }),

                TestCases.Create(SuiteId, "ProtectorReusesPersistedKey", "CredentialProtector reuses the persisted key across instances", async ct =>
                {
                    await Temp.WithDirAsync(async root =>
                    {
                        string keyPath = Path.Combine(root, "dp.key");
                        CredentialProtector first = new CredentialProtector(keyPath);
                        string protectedValue = await first.ProtectAsync("persisted", ct).ConfigureAwait(false);
                        CredentialProtector second = new CredentialProtector(keyPath);
                        Assert.Equal("persisted", await second.UnprotectAsync(protectedValue, ct).ConfigureAwait(false));
                    }).ConfigureAwait(false);
                }),

                TestCases.Create(SuiteId, "ProtectorConcurrentFirstUseSharesOneKey", "CredentialProtector concurrent first use generates a single key", async ct =>
                {
                    await Temp.WithDirAsync(async root =>
                    {
                        CredentialProtector protector = new CredentialProtector(Path.Combine(root, "dp.key"));
                        List<Task<string>> tasks = new List<Task<string>>();
                        for (int i = 0; i < 16; i++)
                        {
                            string value = "secret-" + i;
                            tasks.Add(Task.Run(() => protector.ProtectAsync(value, ct), ct));
                        }

                        string[] results = await Task.WhenAll(tasks).ConfigureAwait(false);
                        CredentialProtector reader = new CredentialProtector(protector.KeyFilePath);
                        for (int i = 0; i < results.Length; i++)
                        {
                            Assert.Equal("secret-" + i, await reader.UnprotectAsync(results[i], ct).ConfigureAwait(false));
                        }
                    }).ConfigureAwait(false);
                })
            };

            return new TestSuiteDescriptor(SuiteId, "Cryptography", cases);
        }
    }
}
