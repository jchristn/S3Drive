namespace S3Drive.Core.Security
{
    using System;

    /// <summary>
    /// Thrown when encryption or decryption of a protected value fails.
    /// </summary>
    public class S3DriveCryptoException : Exception
    {
        /// <summary>
        /// Initializes a new instance with a message.
        /// </summary>
        /// <param name="message">The error message.</param>
        public S3DriveCryptoException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// Initializes a new instance with a message and an inner exception.
        /// </summary>
        /// <param name="message">The error message.</param>
        /// <param name="innerException">The underlying exception.</param>
        public S3DriveCryptoException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
