namespace S3Drive.Tui
{
    using System;
    using System.Collections.Generic;
    using TUIKit;
    using TUIKit.Input;
    using TUIKit.Modals;
    using TUIKit.Unicode;

    /// <summary>
    /// A modal that renders multi-line content verbatim (no reflow) in a centered bordered box, with an
    /// optional dimmed hint line at the bottom. Used for the startup splash. Any key dismisses it, so it
    /// never blocks the user from reaching the console.
    /// </summary>
    internal sealed class SplashModal : Modal
    {
        private const int PadX = 3;
        private const int PadY = 1;

        private readonly string _Title;
        private readonly IReadOnlyList<string> _Lines;
        private readonly string _Hint;
        private readonly bool _Centered;

        /// <summary>
        /// Initializes a new instance of the <see cref="SplashModal"/> class.
        /// </summary>
        /// <param name="title">The box title. May be null (treated as empty).</param>
        /// <param name="lines">The content lines, rendered verbatim. Cannot be null.</param>
        /// <param name="hint">An optional dimmed footer hint; empty to omit. Defaults to a dismissal prompt.</param>
        /// <param name="centered">When true, each content line and the hint are horizontally centered. Defaults to true.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="lines"/> is null.</exception>
        public SplashModal(string title, IReadOnlyList<string> lines, string hint = "Press any key to continue", bool centered = true)
        {
            _Title = title ?? string.Empty;
            _Lines = lines ?? throw new ArgumentNullException(nameof(lines));
            _Hint = hint ?? string.Empty;
            _Centered = centered;
        }

        /// <summary>
        /// Dismisses the splash on any key press.
        /// </summary>
        /// <param name="key">The key event.</param>
        /// <returns>Always true; the key is consumed and the modal closes.</returns>
        public override bool HandleKey(KeyEvent key)
        {
            Close(0);
            return true;
        }

        /// <summary>
        /// Renders the centered splash box.
        /// </summary>
        /// <param name="surface">The surface to draw on. Cannot be null.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="surface"/> is null.</exception>
        public override void Render(ISurface surface)
        {
            if (surface == null)
                throw new ArgumentNullException(nameof(surface));

            int screenWidth = surface.Size.Width;
            int screenHeight = surface.Size.Height;

            int contentWidth = Measure(_Title);
            for (int i = 0; i < _Lines.Count; i++)
                contentWidth = Math.Max(contentWidth, Measure(_Lines[i]));
            if (_Hint.Length > 0)
                contentWidth = Math.Max(contentWidth, Measure(_Hint));

            contentWidth = Math.Max(4, Math.Min(contentWidth, screenWidth - 2 - (2 * PadX)));

            int hintRows = _Hint.Length > 0 ? 2 : 0;
            int contentHeight = _Lines.Count + hintRows;
            int boxWidth = Math.Min(screenWidth, contentWidth + 2 + (2 * PadX));
            int boxHeight = Math.Min(screenHeight, contentHeight + 2 + (2 * PadY));

            int boxX = Math.Max(0, (screenWidth - boxWidth) / 2);
            int boxY = Math.Max(0, (screenHeight - boxHeight) / 2);
            Rect box = new Rect(boxX, boxY, boxWidth, boxHeight);

            surface.Fill(box, Cell.Blank(CellStyle.Default));
            surface.DrawBox(box, CellStyle.Default.WithForeground(Color.FromPalette(6)), _Title);

            int contentX = boxX + 1 + PadX;
            int firstRow = boxY + 1 + PadY;
            int lastContentRow = boxY + boxHeight - 2 - PadY;

            for (int i = 0; i < _Lines.Count; i++)
            {
                int row = firstRow + i;
                if (row > lastContentRow)
                    break;
                surface.DrawText(LineX(contentX, contentWidth, _Lines[i]), row, _Lines[i], CellStyle.Default);
            }

            if (_Hint.Length > 0)
            {
                int hintRow = lastContentRow;
                if (hintRow > firstRow + _Lines.Count - 1)
                    surface.DrawText(LineX(contentX, contentWidth, _Hint), hintRow, _Hint, CellStyle.Default.WithForeground(Color.FromPalette(8)));
            }
        }

        private int LineX(int contentX, int contentWidth, string line)
        {
            if (!_Centered)
                return contentX;
            return contentX + Math.Max(0, (contentWidth - Measure(line)) / 2);
        }

        private static int Measure(string text)
        {
            return Graphemes.MeasureWidth(text ?? string.Empty);
        }
    }
}
