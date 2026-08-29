namespace S3Drive.Core.Security
{
    using System;
    using System.Security.Cryptography;

    /// <summary>
    /// AES-256-GCM authenticated encryption with a self-describing frame. The frame layout is a
    /// single version byte (1), followed by a 12-byte nonce, a 16-byte tag, and the ciphertext.
    /// </summary>
    public static class AesGcmCipher
    {
        /// <summary>
        /// The required key length in bytes (256-bit).
        /// </summary>
        public const int KeyLengthBytes = 32;

        /// <summary>
        /// The nonce length in bytes.
        /// </summary>
        public const int NonceLengthBytes = 12;

        /// <summary>
        /// The authentication tag length in bytes.
        /// </summary>
        public const int TagLengthBytes = 16;

        private const byte FrameVersion = 1;
        private const int HeaderLengthBytes = 1 + NonceLengthBytes + TagLengthBytes;

        /// <summary>
        /// Encrypts plaintext with a fresh random nonce and returns the framed result.
        /// </summary>
        /// <param name="key">The 32-byte key. Cannot be null and must be exactly <see cref="KeyLengthBytes"/> bytes.</param>
        /// <param name="plaintext">The plaintext. Cannot be null.</param>
        /// <param name="associatedData">Optional associated data bound to the ciphertext. May be null.</param>
        /// <returns>The framed ciphertext. Never null.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="key"/> or <paramref name="plaintext"/> is null.</exception>
        /// <exception cref="ArgumentException">Thrown when <paramref name="key"/> is not exactly <see cref="KeyLengthBytes"/> bytes.</exception>
        public static byte[] Encrypt(byte[] key, byte[] plaintext, byte[]? associatedData)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (plaintext == null) throw new ArgumentNullException(nameof(plaintext));
            if (key.Length != KeyLengthBytes) throw new ArgumentException("Key must be exactly " + KeyLengthBytes + " bytes.", nameof(key));

            byte[] nonce = RandomNumberGenerator.GetBytes(NonceLengthBytes);
            byte[] ciphertext = new byte[plaintext.Length];
            byte[] tag = new byte[TagLengthBytes];

            using (AesGcm gcm = new AesGcm(key, TagLengthBytes))
            {
                gcm.Encrypt(nonce, plaintext, ciphertext, tag, associatedData);
            }

            byte[] frame = new byte[HeaderLengthBytes + ciphertext.Length];
            frame[0] = FrameVersion;
            Buffer.BlockCopy(nonce, 0, frame, 1, NonceLengthBytes);
            Buffer.BlockCopy(tag, 0, frame, 1 + NonceLengthBytes, TagLengthBytes);
            Buffer.BlockCopy(ciphertext, 0, frame, HeaderLengthBytes, ciphertext.Length);
            return frame;
        }

        /// <summary>
        /// Decrypts a framed ciphertext produced by <see cref="Encrypt"/>.
        /// </summary>
        /// <param name="key">The 32-byte key. Cannot be null and must be exactly <see cref="KeyLengthBytes"/> bytes.</param>
        /// <param name="frame">The framed ciphertext. Cannot be null.</param>
        /// <param name="associatedData">The associated data that was bound at encryption time. May be null.</param>
        /// <returns>The recovered plaintext. Never null.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="key"/> or <paramref name="frame"/> is null.</exception>
        /// <exception cref="ArgumentException">Thrown when <paramref name="key"/> is not exactly <see cref="KeyLengthBytes"/> bytes.</exception>
        /// <exception cref="S3DriveCryptoException">Thrown when the frame is malformed or fails authentication.</exception>
        public static byte[] Decrypt(byte[] key, byte[] frame, byte[]? associatedData)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (frame == null) throw new ArgumentNullException(nameof(frame));
            if (key.Length != KeyLengthBytes) throw new ArgumentException("Key must be exactly " + KeyLengthBytes + " bytes.", nameof(key));
            if (frame.Length < HeaderLengthBytes) throw new S3DriveCryptoException("Ciphertext frame is too short.");
            if (frame[0] != FrameVersion) throw new S3DriveCryptoException("Unsupported ciphertext frame version.");

            byte[] nonce = new byte[NonceLengthBytes];
            byte[] tag = new byte[TagLengthBytes];
            int cipherLength = frame.Length - HeaderLengthBytes;
            byte[] ciphertext = new byte[cipherLength];

            Buffer.BlockCopy(frame, 1, nonce, 0, NonceLengthBytes);
            Buffer.BlockCopy(frame, 1 + NonceLengthBytes, tag, 0, TagLengthBytes);
            Buffer.BlockCopy(frame, HeaderLengthBytes, ciphertext, 0, cipherLength);

            byte[] plaintext = new byte[cipherLength];
            try
            {
                using (AesGcm gcm = new AesGcm(key, TagLengthBytes))
                {
                    gcm.Decrypt(nonce, ciphertext, tag, plaintext, associatedData);
                }
            }
            catch (CryptographicException ex)
            {
                throw new S3DriveCryptoException("Decryption failed authentication; the value or key may be corrupt.", ex);
            }

            return plaintext;
        }
    }
}
