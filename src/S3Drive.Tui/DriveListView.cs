namespace S3Drive.Tui
{
    using System;
    using System.Collections.Generic;
    using TUIKit;
    using TUIKit.Input;
    using TUIKit.Widgets;

    /// <summary>
    /// The Drives pane: a column header over a list of drive rows with a movable highlight. Up/Down,
    /// PageUp/PageDown, Home/End, a left click, or the mouse wheel move the highlight; Enter or a
    /// double-click activates the highlighted drive. The selection is tracked by drive id so it
    /// survives refreshes that reorder or replace the rows.
    /// </summary>
    internal sealed class DriveListView : IWidget, IFocusable, IFocusAware, IMouseAware
    {
        private const byte HighlightColor = 6;  // cyan
        private const byte HeaderColor = 8;     // dim gray
        private const int DoubleClickMilliseconds = 500;

        private string _Header = string.Empty;
        private string _EmptyText = string.Empty;
        private List<string> _Rows = new List<string>();
        private List<string> _Ids = new List<string>();
        private int _Selected = -1;
        private int _Hovered = -1;
        private int _Scroll;
        private int _Viewport = 1;
        private bool _Focused;
        private int _LastPressIndex = -1;
        private long _LastPressTicks;

        /// <summary>
        /// Raised on the loop thread when the highlighted drive is activated (Enter or double-click),
        /// with that drive's id.
        /// </summary>
        public event Action<string>? Activated;

        /// <summary>
        /// Raised on the loop thread when the list is clicked, so the host can move focus to it.
        /// </summary>
        public event Action? Clicked;

        /// <summary>
        /// The background style every row is composed over, normally the theme's text style.
        /// Defaults to <see cref="CellStyle.Default"/>.
        /// </summary>
        public CellStyle BaseStyle { get; set; } = CellStyle.Default;

        /// <summary>
        /// The id of the highlighted drive, or null when the list is empty.
        /// </summary>
        public string? SelectedId
        {
            get { return _Selected >= 0 && _Selected < _Ids.Count ? _Ids[_Selected] : null; }
        }

        /// <summary>
        /// Replaces the header and rows, keeping the highlight on the same drive id when it is still
        /// present; otherwise the highlight is clamped to the list.
        /// </summary>
        /// <param name="header">The column header line.</param>
        /// <param name="rows">The display line for each drive.</param>
        /// <param name="ids">The drive id for each row, in the same order as <paramref name="rows"/>.</param>
        /// <param name="emptyText">The text shown when there are no rows.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        /// <exception cref="ArgumentException">Thrown when <paramref name="rows"/> and <paramref name="ids"/> differ in length.</exception>
        public void SetRows(string header, IList<string> rows, IList<string> ids, string emptyText)
        {
            if (header == null) throw new ArgumentNullException(nameof(header));
            if (rows == null) throw new ArgumentNullException(nameof(rows));
            if (ids == null) throw new ArgumentNullException(nameof(ids));
            if (emptyText == null) throw new ArgumentNullException(nameof(emptyText));
            if (rows.Count != ids.Count) throw new ArgumentException("Each drive row must have exactly one id.", nameof(ids));

            string? previous = SelectedId;
            _Header = header;
            _EmptyText = emptyText;
            _Rows = new List<string>(rows);
            _Ids = new List<string>(ids);
            if (_Hovered >= _Rows.Count) _Hovered = -1;

            int index = previous != null ? _Ids.IndexOf(previous) : -1;
            if (index < 0) index = Math.Min(Math.Max(_Selected, 0), _Rows.Count - 1);
            _Selected = _Rows.Count == 0 ? -1 : index;
        }

        /// <summary>
        /// Moves the highlight to the drive with the supplied id, if present.
        /// </summary>
        /// <param name="id">The drive id.</param>
        public void Select(string id)
        {
            int index = _Ids.IndexOf(id);
            if (index >= 0) _Selected = index;
        }

        /// <inheritdoc />
        public Size Measure(Size available)
        {
            return available;
        }

        /// <inheritdoc />
        public void Render(ISurface surface)
        {
            int width = surface.Size.Width;
            int height = surface.Size.Height;
            CellStyle normal = BaseStyle;
            surface.Fill(new Rect(0, 0, width, height), Cell.Blank(normal));
            if (width < 1 || height < 1) return;

            if (_Rows.Count == 0)
            {
                surface.DrawText(0, 0, Clip(_EmptyText, width), normal);
                return;
            }

            surface.DrawText(0, 0, Clip(_Header, width), normal.WithForeground(Color.FromPalette(HeaderColor)).WithAttribute(CellAttributes.Bold, true));

            _Viewport = Math.Max(1, height - 1);
            if (_Selected < _Scroll) _Scroll = _Selected;
            if (_Selected >= _Scroll + _Viewport) _Scroll = _Selected - _Viewport + 1;
            _Scroll = Math.Clamp(_Scroll, 0, Math.Max(0, _Rows.Count - _Viewport));

            CellStyle selected = _Focused
                ? normal.WithBackground(Color.FromPalette(HighlightColor)).WithForeground(Color.FromPalette(0))
                : normal.WithAttribute(CellAttributes.Bold, true);
            CellStyle hovered = normal.WithAttribute(CellAttributes.Underline, true);

            for (int row = 0; row < _Viewport; row++)
            {
                int index = _Scroll + row;
                if (index >= _Rows.Count) break;

                CellStyle style = index == _Selected ? selected : (index == _Hovered ? hovered : normal);
                if (index == _Selected) surface.Fill(new Rect(0, row + 1, width, 1), Cell.Blank(style));
                surface.DrawText(0, row + 1, Clip(_Rows[index], width), style);
            }
        }

        /// <inheritdoc />
        public bool HandleKey(KeyEvent key)
        {
            if (_Rows.Count == 0) return false;

            switch (key.Code)
            {
                case KeyCode.Up:
                    Move(-1);
                    return true;
                case KeyCode.Down:
                    Move(1);
                    return true;
                case KeyCode.PageUp:
                    Move(-_Viewport);
                    return true;
                case KeyCode.PageDown:
                    Move(_Viewport);
                    return true;
                case KeyCode.Home:
                    _Selected = 0;
                    return true;
                case KeyCode.End:
                    _Selected = _Rows.Count - 1;
                    return true;
                case KeyCode.Enter:
                    Activate();
                    return true;
                default:
                    return false;
            }
        }

        /// <inheritdoc />
        public void OnFocusChanged(bool focused)
        {
            _Focused = focused;
        }

        /// <inheritdoc />
        public bool HandleMouse(MouseEvent mouse)
        {
            if (mouse == null) throw new ArgumentNullException(nameof(mouse));

            int index = RowAt(mouse.Y);

            switch (mouse.Kind)
            {
                case MouseEventKind.Enter:
                case MouseEventKind.Move:
                    _Hovered = index;
                    return false;
                case MouseEventKind.Leave:
                    _Hovered = -1;
                    return false;
                case MouseEventKind.Wheel:
                    if (mouse.Button == MouseButton.WheelUp) Move(-1);
                    else if (mouse.Button == MouseButton.WheelDown) Move(1);
                    else return false;
                    return true;
                case MouseEventKind.Press:
                    if (mouse.Button != MouseButton.Left) return false;
                    Clicked?.Invoke();
                    if (index < 0) return true;

                    // Double-clicks are detected here from press timing on the same row rather
                    // than relying on the host's synthesized click count.
                    long now = Environment.TickCount64;
                    bool doubleClick = mouse.ClickCount >= 2
                        || (index == _LastPressIndex && now - _LastPressTicks <= DoubleClickMilliseconds);
                    _Selected = index;
                    _LastPressIndex = doubleClick ? -1 : index;
                    _LastPressTicks = now;
                    if (doubleClick) Activate();
                    return true;
                default:
                    return false;
            }
        }

        private int RowAt(int y)
        {
            if (y < 1) return -1;
            int index = _Scroll + y - 1;
            return index < _Rows.Count ? index : -1;
        }

        private void Move(int delta)
        {
            if (_Rows.Count == 0) return;
            _Selected = Math.Clamp(_Selected + delta, 0, _Rows.Count - 1);
        }

        private void Activate()
        {
            string? id = SelectedId;
            if (id != null) Activated?.Invoke(id);
        }

        private static string Clip(string text, int width)
        {
            return text.Length > width ? text.Substring(0, width) : text;
        }
    }
}
