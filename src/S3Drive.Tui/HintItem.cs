namespace S3Drive.Tui
{
    using System;

    /// <summary>
    /// One clickable entry in a <see cref="HintBar"/>: the key shown in the accent color, its
    /// label, and the action a click runs.
    /// </summary>
    internal sealed class HintItem
    {
        /// <summary>
        /// Initializes a hint.
        /// </summary>
        /// <param name="key">The key text, for example "c" or "^Q".</param>
        /// <param name="label">The action label.</param>
        /// <param name="action">The action run when the hint is clicked.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public HintItem(string key, string label, Action action)
        {
            Key = key ?? throw new ArgumentNullException(nameof(key));
            Label = label ?? throw new ArgumentNullException(nameof(label));
            Action = action ?? throw new ArgumentNullException(nameof(action));
        }

        /// <summary>
        /// The key text.
        /// </summary>
        public string Key { get; }

        /// <summary>
        /// The action label.
        /// </summary>
        public string Label { get; }

        /// <summary>
        /// The action run when the hint is clicked.
        /// </summary>
        public Action Action { get; }
    }
}
