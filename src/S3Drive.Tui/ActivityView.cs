namespace S3Drive.Tui
{
    using System;
    using TUIKit;
    using TUIKit.Content;
    using TUIKit.Input;
    using TUIKit.Widgets;

    /// <summary>
    /// Wraps the Activity log pane so it joins the host focus ring: clicking it (or Tab) focuses it,
    /// and the mouse wheel scrolls its scrollback.
    /// </summary>
    internal sealed class ActivityView : IWidget, IFocusable, IFocusAware, IMouseAware
    {
        private readonly Pane _Pane;

        /// <summary>
        /// Initializes the view over a pane.
        /// </summary>
        /// <param name="pane">The log pane.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="pane"/> is null.</exception>
        public ActivityView(Pane pane)
        {
            _Pane = pane ?? throw new ArgumentNullException(nameof(pane));
        }

        /// <summary>
        /// The background style the pane is rendered over, normally the theme's text style.
        /// Defaults to <see cref="CellStyle.Default"/>.
        /// </summary>
        public CellStyle BaseStyle { get; set; } = CellStyle.Default;

        /// <summary>
        /// Whether the view currently holds keyboard focus.
        /// </summary>
        public bool Focused { get; private set; }

        /// <inheritdoc />
        public Size Measure(Size available)
        {
            return available;
        }

        /// <inheritdoc />
        public void Render(ISurface surface)
        {
            _Pane.Render(surface, BaseStyle);
        }

        /// <inheritdoc />
        public bool HandleKey(KeyEvent key)
        {
            return false;
        }

        /// <inheritdoc />
        public void OnFocusChanged(bool focused)
        {
            Focused = focused;
        }

        /// <inheritdoc />
        public bool HandleMouse(MouseEvent mouse)
        {
            return _Pane.HandleMouse(mouse);
        }
    }
}
