namespace S3Drive.Tui
{
    using System;
    using System.Collections.Generic;
    using TUIKit;
    using TUIKit.Input;
    using TUIKit.Widgets;

    /// <summary>
    /// A one-row keyboard-shortcut bar drawn beneath a pane. Each hint is a key in an accent color
    /// followed by a dim label, matching the shortcut-hint formatting used in Armor. When the bar
    /// belongs to the focused pane, its title and keys are highlighted; otherwise the whole bar is
    /// dimmed so the focused pane is obvious. Hints are clickable: a left click runs the hint's
    /// action, a click on the title runs the title action, and the hint under the pointer is
    /// underlined.
    /// </summary>
    internal sealed class HintBar : IWidget, IMouseAware
    {
        private const byte KeyColor = 6;    // cyan
        private const byte LabelColor = 8;  // dim gray
        private const string Separator = "  ";

        private readonly string _Title;
        private readonly Action _TitleAction;
        private readonly List<HintItem> _Hints;
        private readonly List<int> _SpanStarts = new List<int>();
        private readonly List<int> _SpanEnds = new List<int>();
        private int _TitleEnd;
        private int _Hovered = -1;

        /// <summary>
        /// Initializes a new bar with a title and an ordered set of hints.
        /// </summary>
        /// <param name="title">The pane name shown at the left of the bar.</param>
        /// <param name="titleAction">The action run when the title is clicked.</param>
        /// <param name="hints">The hints, in display order.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="titleAction"/> or <paramref name="hints"/> is null.</exception>
        public HintBar(string title, Action titleAction, IEnumerable<HintItem> hints)
        {
            if (hints == null) throw new ArgumentNullException(nameof(hints));
            _Title = title ?? string.Empty;
            _TitleAction = titleAction ?? throw new ArgumentNullException(nameof(titleAction));
            _Hints = new List<HintItem>(hints);
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

            _SpanStarts.Clear();
            _SpanEnds.Clear();

            int x = 0;
            x += DrawText(surface, x, (Focused ? "▸ " : "  ") + _Title, titleStyle);
            _TitleEnd = x;
            x += DrawText(surface, x, Separator, dim);

            for (int i = 0; i < _Hints.Count; i++)
            {
                if (i > 0) x += DrawText(surface, x, Separator, dim);

                bool hovered = i == _Hovered;
                CellStyle hintKey = hovered ? keyStyle.WithAttribute(CellAttributes.Underline, true) : keyStyle;
                CellStyle hintLabel = hovered ? dim.WithAttribute(CellAttributes.Underline, true) : dim;

                _SpanStarts.Add(x);
                x += DrawText(surface, x, _Hints[i].Key, hintKey);
                x += DrawText(surface, x, " " + _Hints[i].Label, hintLabel);
                _SpanEnds.Add(x);
            }
        }

        /// <inheritdoc />
        public bool HandleMouse(MouseEvent mouse)
        {
            if (mouse == null) throw new ArgumentNullException(nameof(mouse));

            switch (mouse.Kind)
            {
                case MouseEventKind.Enter:
                case MouseEventKind.Move:
                    _Hovered = mouse.Y == 0 ? HintAt(mouse.X) : -1;
                    return false;
                case MouseEventKind.Leave:
                    _Hovered = -1;
                    return false;
                case MouseEventKind.Press:
                    if (mouse.Button != MouseButton.Left || mouse.Y != 0) return false;
                    if (mouse.X < _TitleEnd)
                    {
                        _TitleAction();
                        return true;
                    }

                    int index = HintAt(mouse.X);
                    if (index < 0) return false;
                    _Hints[index].Action();
                    return true;
                default:
                    return false;
            }
        }

        private int HintAt(int x)
        {
            for (int i = 0; i < _SpanStarts.Count; i++)
            {
                if (x >= _SpanStarts[i] && x < _SpanEnds[i]) return i;
            }

            return -1;
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
