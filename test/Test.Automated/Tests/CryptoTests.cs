namespace Test.Automated.Tests
{
    using System;
    using System.IO;
    using System.Security.Cryptography;
    using System.Text;
    using System.Threading.Tasks;
    using S3Drive.Core.Security;
    using Test.Automated.Harness;

    /// <summary>
    /// Tests for <see cref="AesGcmCipher"/> and <see cref="CredentialProtector"/>.
    /// </summary>
    public static class CryptoTests
    {
        /// <summary>
        /// Registers the tests.
        /// </summary>
        /// <param name="runner">The runner.</param>
        public static void Register(TestRunner runner)
        {
            runner.Add("AesGcm round-trip", () =>
            {
                byte[] key = RandomNumberGenerator.GetBytes(32);
                byte[] plaintext = Encoding.UTF8.GetBytes("secret-value");
                byte[] frame = AesGcmCipher.Encrypt(key, plaintext, null);
                byte[] recovered = AesGcmCipher.Decrypt(key, frame, null);
                Assert.Equal("secret-value", Encoding.UTF8.GetString(recovered));
            });

            runner.Add("AesGcm wrong key fails", () =>
            {
                byte[] key1 = RandomNumberGenerator.GetBytes(32);
                byte[] key2 = RandomNumberGenerator.GetBytes(32);
                byte[] frame = AesGcmCipher.Encrypt(key1, Encoding.UTF8.GetBytes("x"), null);
                Assert.Throws<S3DriveCryptoException>(() => AesGcmCipher.Decrypt(key2, frame, null));
            });

            runner.Add("AesGcm tampered frame fails", () =>
            {
                byte[] key = RandomNumberGenerator.GetBytes(32);
                byte[] frame = AesGcmCipher.Encrypt(key, Encoding.UTF8.GetBytes("hello"), null);
                frame[frame.Length - 1] ^= 0xFF;
                Assert.Throws<S3DriveCryptoException>(() => AesGcmCipher.Decrypt(key, frame, null));
            });

            runner.Add("AesGcm rejects bad version and short frame", () =>
            {
                byte[] key = RandomNumberGenerator.GetBytes(32);
                Assert.Throws<S3DriveCryptoException>(() => AesGcmCipher.Decrypt(key, new byte[5], null));

                byte[] frame = AesGcmCipher.Encrypt(key, Encoding.UTF8.GetBytes("z"), null);
                frame[0] = 9;
                Assert.Throws<S3DriveCryptoException>(() => AesGcmCipher.Decrypt(key, frame, null));
            });

            runner.Add("AesGcm rejects bad key length and nulls", () =>
            {
                Assert.Throws<ArgumentException>(() => AesGcmCipher.Encrypt(new byte[10], new byte[1], null));
                Assert.Throws<ArgumentNullException>(() => AesGcmCipher.Encrypt(null!, new byte[1], null));
                Assert.Throws<ArgumentNullException>(() => AesGcmCipher.Encrypt(RandomNumberGenerator.GetBytes(32), null!, null));
            });

            runner.Add("AesGcm binds associated data", () =>
            {
                byte[] key = RandomNumberGenerator.GetBytes(32);
                byte[] frame = AesGcmCipher.Encrypt(key, Encoding.UTF8.GetBytes("x"), Encoding.UTF8.GetBytes("aad-1"));
                Assert.Throws<S3DriveCryptoException>(() => AesGcmCipher.Decrypt(key, frame, Encoding.UTF8.GetBytes("aad-2")));
            });

            runner.Add("CredentialProtector round-trip creates key file", async () =>
            {
                string root = Temp.NewDir();
                try
                {
                    CredentialProtector protector = new CredentialProtector(Path.Combine(root, "dp.key"));
                    string protectedValue = await protector.ProtectAsync("hunter2");
                    Assert.True(File.Exists(protector.KeyFilePath));
                    Assert.False(protectedValue.Contains("hunter2", StringComparison.Ordinal));
                    string recovered = await protector.UnprotectAsync(protectedValue);
                    Assert.Equal("hunter2", recovered);
                }
                finally
                {
                    Temp.Delete(root);
                }
            });

            runner.Add("CredentialProtector with different key fails", async () =>
            {
                string root = Temp.NewDir();
                try
                {
                    CredentialProtector p1 = new CredentialProtector(Path.Combine(root, "k1"));
                    string protectedValue = await p1.ProtectAsync("x");
                    CredentialProtector p2 = new CredentialProtector(Path.Combine(root, "k2"));
                    await Assert.ThrowsAsync<S3DriveCryptoException>(() => p2.UnprotectAsync(protectedValue));
                }
                finally
                {
                    Temp.Delete(root);
                }
            });

            runner.Add("CredentialProtector rejects bad base64", async () =>
            {
                string root = Temp.NewDir();
                try
                {
                    CredentialProtector protector = new CredentialProtector(Path.Combine(root, "k"));
                    await Assert.ThrowsAsync<S3DriveCryptoException>(() => protector.UnprotectAsync("not valid base64 !!!"));
                }
                finally
                {
                    Temp.Delete(root);
                }
            });

            runner.Add("CredentialProtector reuses persisted key across instances", async () =>
            {
                string root = Temp.NewDir();
                try
                {
                    string keyPath = Path.Combine(root, "dp.key");
                    CredentialProtector first = new CredentialProtector(keyPath);
                    string protectedValue = await first.ProtectAsync("persisted");
                    CredentialProtector second = new CredentialProtector(keyPath);
                    Assert.Equal("persisted", await second.UnprotectAsync(protectedValue));
                }
                finally
                {
                    Temp.Delete(root);
                }
            });
        }
    }
}
