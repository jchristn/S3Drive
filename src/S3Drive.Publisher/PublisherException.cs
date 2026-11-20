namespace S3Drive.Publisher
{
    using System;

    /// <summary>
    /// A packaging failure with a message meant for the person running the Publisher: a bad
    /// manifest, a missing tool (with the command that installs it), or a failed packager run.
    /// </summary>
    public class PublisherException : Exception
    {
        /// <summary>
        /// Initializes the exception with a message.
        /// </summary>
        /// <param name="message">What went wrong and, where possible, how to fix it.</param>
        public PublisherException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// Initializes the exception with a message and the exception that caused it.
        /// </summary>
        /// <param name="message">What went wrong and, where possible, how to fix it.</param>
        /// <param name="innerException">The underlying exception.</param>
        public PublisherException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
