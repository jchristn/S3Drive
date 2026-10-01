namespace S3Drive.Tui
{
    using System;
    using System.Collections.Generic;
    using TUIKit;
    using TUIKit.Input;
    using TUIKit.Modals;

    /// <summary>
    /// A centered dialog with a message and a row of buttons that works with both the keyboard and
    /// the mouse. Left/Right or Tab move the highlighted button and Enter chooses it; a left click
    /// chooses the button under the pointer. Escape cancels. The result is the zero-based index of
    /// the chosen button, or -1 when cancelled.
    /// </summary>
    internal sealed class ButtonDialogModal : Modal
    {
        private const byte AccentColor = 6;  // cyan
        private const int MaxBoxWidth = 90;

        private readonly string _Title;
        private readonly List<string> _Message;
        private readonly List<string> _Buttons;
        private readonly List<Rect> _ButtonRects = new List<Rect>();
        private int _Selected;
        private int _Hovered = -1;

        /// <summary>
        /// Initializes the dialog.
        /// </summary>
        /// <param name="title">The title drawn on the top border.</param>
        /// <param name="message">The message; newlines start new lines.</param>
        /// <param name="buttons">The button labels, in display order. At least one is required.</param>
        /// <param name="selected">The initially highlighted button index. Defaults to 0.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        /// <exception cref="ArgumentException">Thrown when <paramref name="buttons"/> is empty.</exception>
        public ButtonDialogModal(string title, string message, IList<string> buttons, int selected = 0)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));
            if (buttons == null) throw new ArgumentNullException(nameof(buttons));
            if (buttons.Count == 0) throw new ArgumentException("At least one button is required.", nameof(buttons));

            _Title = title ?? throw new ArgumentNullException(nameof(title));
            _Message = new List<string>(message.Replace("\r", string.Empty).Split('\n'));
            _Buttons = new List<string>(buttons);
            _Selected = Math.Clamp(selected, 0, _Buttons.Count - 1);
        }

        /// <inheritdoc />
        public override bool HandleKey(KeyEvent key)
        {
            switch (key.Code)
            {
                case KeyCode.Escape:
                    Close(-1);
                    return true;
                case KeyCode.Enter:
                    Close(_Selected);
                    return true;
                case KeyCode.Left:
                    _Selected = (_Selected + _Buttons.Count - 1) % _Buttons.Count;
                    return true;
                case KeyCode.Right:
                case KeyCode.Tab:
                    _Selected = (_Selected + 1) % _Buttons.Count;
                    return true;
                default:
                    return true;
            }
        }

        /// <inheritdoc />
        public override bool HandleMouse(MouseEvent mouse)
        {
            if (mouse == null) throw new ArgumentNullException(nameof(mouse));

            int index = ButtonAt(mouse.X, mouse.Y);
            switch (mouse.Kind)
            {
                case MouseEventKind.Enter:
                case MouseEventKind.Move:
                    _Hovered = index;
                    return true;
                case MouseEventKind.Leave:
                    _Hovered = -1;
                    return true;
                case MouseEventKind.Press:
                    if (mouse.Button == MouseButton.Left && index >= 0) Close(index);
                    return true;
                default:
                    return true;
            }
        }

        /// <inheritdoc />
        public override void Render(ISurface surface)
        {
            if (surface == null) throw new ArgumentNullException(nameof(surface));

            int screenWidth = surface.Size.Width;
            int screenHeight = surface.Size.Height;

            int buttonsWidth = 0;
            foreach (string button in _Buttons) buttonsWidth += button.Length + 6;

            int contentWidth = Math.Max(_Title.Length + 4, buttonsWidth);
            foreach (string line in _Message) contentWidth = Math.Max(contentWidth, line.Length);

            int boxWidth = Math.Min(Math.Min(MaxBoxWidth, screenWidth), contentWidth + 6);
            int innerWidth = Math.Max(1, boxWidth - 6);

            List<string> lines = Wrap(_Message, innerWidth);
            int boxHeight = Math.Min(screenHeight, lines.Count + 5);
            if (boxWidth < 8 || boxHeight < 5) return;

            int boxX = (screenWidth - boxWidth) / 2;
            int boxY = (screenHeight - boxHeight) / 2;
            Rect box = new Rect(boxX, boxY, boxWidth, boxHeight);

            surface.Fill(box, Cell.Blank(CellStyle.Default));
            surface.DrawBox(box, CellStyle.Default.WithForeground(Color.FromPalette(AccentColor)), _Title);

            int maxLines = boxHeight - 5;
            for (int i = 0; i < lines.Count && i < maxLines; i++)
            {
                surface.DrawText(boxX + 3, boxY + 2 + i, lines[i], CellStyle.Default);
            }

            int buttonRow = boxY + boxHeight - 2;
            int x = boxX + (boxWidth - buttonsWidth) / 2 + 2;
            _ButtonRects.Clear();
            for (int i = 0; i < _Buttons.Count; i++)
            {
                string text = "[ " + _Buttons[i] + " ]";
                CellStyle style = CellStyle.Default;
                if (i == _Selected) style = style.WithBackground(Color.FromPalette(AccentColor)).WithForeground(Color.FromPalette(0));
                else if (i == _Hovered) style = style.WithAttribute(CellAttributes.Underline, true);

                surface.DrawText(x, buttonRow, text, style);
                _ButtonRects.Add(new Rect(x, buttonRow, text.Length, 1));
                x += text.Length + 2;
            }
        }

        private int ButtonAt(int x, int y)
        {
            for (int i = 0; i < _ButtonRects.Count; i++)
            {
                Rect rect = _ButtonRects[i];
                if (y == rect.Y && x >= rect.X && x < rect.X + rect.Width) return i;
            }

            return -1;
        }

        private static List<string> Wrap(List<string> paragraphs, int width)
        {
            List<string> lines = new List<string>();
            foreach (string paragraph in paragraphs)
            {
                string remaining = paragraph;
                while (remaining.Length > width)
                {
                    int cut = remaining.LastIndexOf(' ', width);
                    if (cut <= 0) cut = width;
                    lines.Add(remaining.Substring(0, cut).TrimEnd());
                    remaining = remaining.Substring(cut).TrimStart();
                }

                lines.Add(remaining);
            }

            return lines;
        }
    }
}
