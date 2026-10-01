namespace S3Drive.Tui
{
    using System;
    using System.Collections.Generic;
    using S3Drive.Core.Configuration;
    using TUIKit;
    using TUIKit.Input;
    using TUIKit.Modals;
    using TUIKit.Widgets;

    /// <summary>
    /// A modal form for creating or editing a drive connection profile. Fields respond to the
    /// keyboard and the mouse: a click focuses a field (placing the caret, toggling a checkbox, or
    /// picking a radio option), the wheel moves between fields, and the Save and Cancel buttons are
    /// clickable.
    /// </summary>
    internal sealed class DriveFormModal : Modal
    {
        private readonly string _Title;
        private readonly Form _Form = new Form();
        private readonly List<TextField?> _TextByIndex;
        private string? _Error;

        // Geometry from the most recent render, used to hit-test mouse events.
        private Rect _Viewport;
        private int _ScrollY;
        private Rect _SaveButton;
        private Rect _CancelButton;
        private int _HoveredButton = -1;

        private readonly TextField _Name = new TextField();
        private readonly RadioGroup _Provider = new RadioGroup(new string[] { "AwsS3", "S3Compatible" });
        private readonly TextField _ServiceUrl = new TextField();
        private readonly TextField _Region = new TextField();
        private readonly TextField _Bucket = new TextField();
        private readonly TextField _AccessKey = new TextField();
        private readonly TextField _Secret = new TextField();
        private readonly Checkbox _UseSsl;
        private readonly Checkbox _UsePathStyle;
        private readonly TextField _DriveLetter = new TextField();

        /// <summary>
        /// Initializes the form, optionally prefilled from an existing profile.
        /// </summary>
        /// <param name="existing">The profile to edit, or null to create a new one.</param>
        public DriveFormModal(DriveProfile? existing)
        {
            _Title = existing == null ? "Add drive" : "Edit drive";

            _UseSsl = new Checkbox("Use SSL", existing?.UseSsl ?? true);
            _UsePathStyle = new Checkbox("Path-style addressing", existing?.UsePathStyle ?? false);

            if (existing != null)
            {
                _Name.Value = existing.Name;
                _ServiceUrl.Value = existing.ServiceUrl ?? string.Empty;
                _Region.Value = existing.Region ?? string.Empty;
                _Bucket.Value = existing.Bucket;
                _AccessKey.Value = existing.AccessKey;
                _DriveLetter.Value = existing.DriveLetter;
            }

            _Form.Add("Name", _Name, () => _Name.Value.Trim().Length == 0 ? "Name is required." : null);
            _Form.Add("Provider", _Provider);
            _Form.Add("Service URL (S3-compatible)", _ServiceUrl);
            _Form.Add("Region", _Region);
            _Form.Add("Bucket", _Bucket, () => _Bucket.Value.Trim().Length == 0 ? "Bucket is required." : null);
            _Form.Add("Access key", _AccessKey);
            _Form.Add("Secret key (blank keeps existing)", _Secret);
            _Form.Add("Use SSL", _UseSsl);
            _Form.Add("Path-style addressing", _UsePathStyle);
            _Form.Add(OperatingSystem.IsWindows() ? "Drive letter or mount name (e.g. S:)" : "Drive letter or mount name (e.g. mybucket)", _DriveLetter, () => _DriveLetter.Value.Trim().Length == 0 ? "Drive letter or mount name is required." : null);

            // Maps each form-field index to its text field (null for non-text fields), so a
            // paste can be inserted into the focused field. Order must match the Add calls above.
            _TextByIndex = new List<TextField?>
            {
                _Name,          // 0
                null,           // 1  Provider (radio)
                _ServiceUrl,    // 2
                _Region,        // 3
                _Bucket,        // 4
                _AccessKey,     // 5
                _Secret,        // 6
                null,           // 7  Use SSL (checkbox)
                null,           // 8  Path-style (checkbox)
                _DriveLetter    // 9
            };
        }

        /// <inheritdoc />
        public override bool HandleKey(KeyEvent key)
        {
            if (key.Code == KeyCode.Escape)
            {
                Close(null);
                return true;
            }

            if (key.Code == KeyCode.Enter)
            {
                Submit();
                return true;
            }

            return _Form.HandleKey(key);
        }

        /// <inheritdoc />
        public override bool HandleMouse(MouseEvent mouse)
        {
            if (mouse == null) throw new ArgumentNullException(nameof(mouse));

            int button = Contains(_SaveButton, mouse.X, mouse.Y) ? 0 : (Contains(_CancelButton, mouse.X, mouse.Y) ? 1 : -1);

            if (mouse.Kind == MouseEventKind.Enter || mouse.Kind == MouseEventKind.Move || mouse.Kind == MouseEventKind.Leave)
            {
                _HoveredButton = mouse.Kind == MouseEventKind.Leave ? -1 : button;
            }

            if (mouse.Kind == MouseEventKind.Press && mouse.Button == MouseButton.Left && button >= 0)
            {
                if (button == 0) Submit();
                else Close(null);
                return true;
            }

            if (mouse.Kind == MouseEventKind.Wheel)
            {
                // Scroll by moving focus so the viewport follows, rather than letting a field under
                // the pointer (such as the provider radio group) consume the wheel.
                int delta = mouse.Button == MouseButton.WheelUp ? -1 : (mouse.Button == MouseButton.WheelDown ? 1 : 0);
                if (delta == 0 || _Form.FieldCount == 0) return false;
                _Form.SetFocusedField(Math.Clamp(_Form.FocusedIndex + delta, 0, _Form.FieldCount - 1));
                return true;
            }

            if (!Contains(_Viewport, mouse.X, mouse.Y)) return mouse.Kind == MouseEventKind.Press;

            MouseEvent local = new MouseEvent(
                mouse.Kind,
                mouse.Button,
                mouse.X - _Viewport.X,
                mouse.Y - _Viewport.Y + _ScrollY,
                mouse.Modifiers,
                mouse.ClickCount);
            _Form.HandleMouse(local);
            return true;
        }

        /// <inheritdoc />
        public override bool HandlePaste(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;

            int index = _Form.FocusedIndex;
            if (index < 0 || index >= _TextByIndex.Count) return false;

            TextField? field = _TextByIndex[index];
            if (field == null) return false;

            // Form fields are single-line; strip any newlines a paste may carry so a pasted
            // access key or secret stays on one line.
            field.Insert(text.Replace("\r", string.Empty).Replace("\n", string.Empty));
            return true;
        }

        /// <inheritdoc />
        public override void Render(ISurface surface)
        {
            Size size = surface.Size;
            int width = Math.Min(80, size.Width - 4);

            // Size to the form's content (plus borders, top padding, and the error line) so every
            // field shows when the terminal is tall enough; otherwise fill the terminal and scroll.
            int desiredHeight = _Form.ContentHeight + 5;
            int height = Math.Min(size.Height - 2, Math.Max(20, desiredHeight));
            if (width < 8 || height < 8) return;

            int x = (size.Width - width) / 2;
            int y = (size.Height - height) / 2;

            // Clear the whole box first so nothing underneath shows through the unpainted columns
            // between the border and the form content.
            surface.Fill(new Rect(x, y, width, height), Cell.Blank(CellStyle.Default));
            surface.DrawBox(new Rect(x, y, width, height), CellStyle.Default, _Title + "  (Tab moves, Enter saves, Esc cancels)");

            int innerWidth = width - 4;
            int viewportHeight = height - 5;

            // Render the whole form into an off-screen buffer, then blit a vertical window that
            // follows the focused field so every field is reachable even when the form is taller
            // than the modal.
            int contentHeight = Math.Max(viewportHeight, _Form.ContentHeight);
            CellBuffer buffer = new CellBuffer(innerWidth, contentHeight);
            _Form.Render(new BufferSurface(buffer));

            int scrollY = 0;
            if (_Form.TryGetFocusRect(out Rect focus))
            {
                if (focus.Bottom > viewportHeight) scrollY = focus.Bottom - viewportHeight;
                if (focus.Top < scrollY) scrollY = focus.Top;
            }

            scrollY = Math.Clamp(scrollY, 0, Math.Max(0, contentHeight - viewportHeight));
            _ScrollY = scrollY;
            _Viewport = new Rect(x + 2, y + 2, innerWidth, viewportHeight);

            for (int row = 0; row < viewportHeight; row++)
            {
                for (int column = 0; column < innerWidth; column++)
                {
                    surface.Set(x + 2 + column, y + 2 + row, buffer.Get(column, row + scrollY));
                }
            }

            if (contentHeight > viewportHeight)
            {
                string indicator = scrollY > 0
                    ? (scrollY + viewportHeight < contentHeight ? "▲▼ more" : "▲ more")
                    : "▼ more";
                surface.DrawText(x + width - indicator.Length - 2, y, indicator, CellStyle.Default.WithForeground(Color.FromPalette(8)));
            }

            // Save and Cancel buttons, right-aligned on the bottom row; the error shares the row.
            const string SaveText = "[ Save ]";
            const string CancelText = "[ Cancel ]";
            int buttonRow = y + height - 2;
            int cancelX = x + width - 2 - CancelText.Length;
            int saveX = cancelX - 2 - SaveText.Length;
            _SaveButton = new Rect(saveX, buttonRow, SaveText.Length, 1);
            _CancelButton = new Rect(cancelX, buttonRow, CancelText.Length, 1);

            CellStyle accent = CellStyle.Default.WithBackground(Color.FromPalette(6)).WithForeground(Color.FromPalette(0));
            CellStyle hover = CellStyle.Default.WithAttribute(CellAttributes.Underline, true);
            surface.DrawText(saveX, buttonRow, SaveText, _HoveredButton == 0 ? accent : CellStyle.Default.WithForeground(Color.FromPalette(6)).WithAttribute(CellAttributes.Bold, true));
            surface.DrawText(cancelX, buttonRow, CancelText, _HoveredButton == 1 ? hover : CellStyle.Default);

            if (_Error != null)
            {
                int room = saveX - (x + 2) - 1;
                string message = "! " + _Error;
                if (message.Length > room) message = message.Substring(0, Math.Max(0, room));
                surface.DrawText(x + 2, buttonRow, message, CellStyle.Default.WithForeground(Color.FromPalette(9)));
            }
        }

        private void Submit()
        {
            _Error = _Form.Validate();
            if (_Error != null) return;

            DriveFormResult result = new DriveFormResult
            {
                Name = _Name.Value.Trim(),
                Provider = _Provider.SelectedIndex == 1 ? S3ProviderEnum.S3Compatible : S3ProviderEnum.AwsS3,
                ServiceUrl = NullIfEmpty(_ServiceUrl.Value),
                Region = NullIfEmpty(_Region.Value),
                Bucket = _Bucket.Value.Trim(),
                AccessKey = _AccessKey.Value.Trim(),
                SecretPlain = _Secret.Value,
                UseSsl = _UseSsl.Checked,
                UsePathStyle = _UsePathStyle.Checked,
                DriveLetter = _DriveLetter.Value.Trim()
            };

            Close(result);
        }

        private static bool Contains(Rect rect, int x, int y)
        {
            return x >= rect.X && x < rect.X + rect.Width && y >= rect.Y && y < rect.Y + rect.Height;
        }

        private static string? NullIfEmpty(string value)
        {
            string trimmed = value.Trim();
            return trimmed.Length == 0 ? null : trimmed;
        }
    }
}
