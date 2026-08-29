namespace S3Drive.Tui
{
    using System.Collections.Generic;
    using TUIKit;
    using TUIKit.Input;
    using TUIKit.Widgets;

    /// <summary>
    /// A one-row keyboard-shortcut bar drawn beneath a pane. Each hint is a key in an accent color
    /// followed by a dim label, matching the shortcut-hint formatting used in Armor. When the bar
    /// belongs to the focused pane, its title and keys are highlighted; otherwise the whole bar is
    /// dimmed so the focused pane is obvious.
    /// </summary>
    internal sealed class HintBar : IWidget
    {
        private const byte KeyColor = 6;    // cyan
        private const byte LabelColor = 8;  // dim gray
        private const string Separator = "  ";

        private readonly string _Title;
        private readonly List<KeyValuePair<string, string>> _Hints;

        /// <summary>
        /// Initializes a new bar with a title and an ordered set of key/label hints.
        /// </summary>
        /// <param name="title">The pane name shown at the left of the bar.</param>
        /// <param name="hints">The key/label pairs, in display order.</param>
        public HintBar(string title, IEnumerable<KeyValuePair<string, string>> hints)
        {
            _Title = title ?? string.Empty;
            _Hints = new List<KeyValuePair<string, string>>(hints);
        }

        /// <summary>
        /// Whether this bar's pane currently has focus.
        /// </summary>
        public bool Focused { get; set; }

        /// <inheritdoc />
        public Size Measure(Size available)
        {
            return new Size(available.Width, 1);
        }

        /// <inheritdoc />
        public bool HandleKey(KeyEvent key)
        {
            return false;
        }

        /// <inheritdoc />
        public void Render(ISurface surface)
        {
            int width = surface.Size.Width;
            int height = surface.Size.Height;
            if (width < 2 || height < 1) return;

            CellStyle baseStyle = CellStyle.Default;
            surface.Fill(new Rect(0, 0, width, height), Cell.Blank(baseStyle));

            CellStyle accent = baseStyle.WithForeground(Color.FromPalette(KeyColor)).WithAttribute(CellAttributes.Bold, true);
            CellStyle dim = baseStyle.WithForeground(Color.FromPalette(LabelColor));
            CellStyle keyStyle = Focused ? accent : dim;
            CellStyle titleStyle = Focused ? accent : dim;

            int x = 0;
            x += DrawText(surface, x, (Focused ? "▸ " : "  ") + _Title, titleStyle);
            x += DrawText(surface, x, Separator, dim);

            bool first = true;
            foreach (KeyValuePair<string, string> hint in _Hints)
            {
                if (!first) x += DrawText(surface, x, Separator, dim);
                x += DrawText(surface, x, hint.Key, keyStyle);
                x += DrawText(surface, x, " " + hint.Value, dim);
                first = false;
            }
        }

        private static int DrawText(ISurface surface, int x, string text, CellStyle style)
        {
            int width = surface.Size.Width;
            if (x >= width || string.IsNullOrEmpty(text)) return text?.Length ?? 0;
            string clipped = x + text.Length > width ? text.Substring(0, width - x) : text;
            surface.DrawText(x, 0, clipped, style);
            return text.Length;
        }
    }
}
