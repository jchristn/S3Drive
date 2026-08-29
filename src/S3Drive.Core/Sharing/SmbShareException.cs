namespace S3Drive.Core.Sharing
{
    using System;

    /// <summary>
    /// Thrown when creating or removing an SMB share fails.
    /// </summary>
    public class SmbShareException : Exception
    {
        /// <summary>
        /// Initializes a new instance with a message.
        /// </summary>
        /// <param name="message">The error message.</param>
        public SmbShareException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// Initializes a new instance with a message and an inner exception.
        /// </summary>
        /// <param name="message">The error message.</param>
        /// <param name="innerException">The underlying exception.</param>
        public SmbShareException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
