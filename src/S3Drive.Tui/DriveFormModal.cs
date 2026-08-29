namespace S3Drive.Tui
{
    using System;
    using System.Collections.Generic;
    using S3Drive.Core.Configuration;
    using S3Drive.Core.Sharing;
    using TUIKit;
    using TUIKit.Input;
    using TUIKit.Modals;
    using TUIKit.Widgets;

    /// <summary>
    /// A modal form for creating or editing a drive connection profile.
    /// </summary>
    internal sealed class DriveFormModal : Modal
    {
        private readonly string _Title;
        private readonly Form _Form = new Form();
        private readonly List<TextField?> _TextByIndex;
        private string? _Error;

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
        private readonly Checkbox _AutoMount;
        private readonly Checkbox _ShareEnabled;
        private readonly TextField _ShareName = new TextField();
        private readonly RadioGroup _ShareAccess = new RadioGroup(new string[] { "ReadOnly", "ReadWrite" });
        private readonly TextField _AllowedPrincipals = new TextField();

        /// <summary>
        /// Initializes the form, optionally prefilled from an existing profile.
        /// </summary>
        /// <param name="existing">The profile to edit, or null to create a new one.</param>
        public DriveFormModal(DriveProfile? existing)
        {
            _Title = existing == null ? "Add drive" : "Edit drive";

            _UseSsl = new Checkbox("Use SSL", existing?.UseSsl ?? true);
            _UsePathStyle = new Checkbox("Path-style addressing", existing?.UsePathStyle ?? false);
            _AutoMount = new Checkbox("Auto-mount", existing?.AutoMount ?? false);
            _ShareEnabled = new Checkbox("Enable network share", existing?.Share.Enabled ?? false);

            if (existing != null)
            {
                _Name.Value = existing.Name;
                _ServiceUrl.Value = existing.ServiceUrl ?? string.Empty;
                _Region.Value = existing.Region ?? string.Empty;
                _Bucket.Value = existing.Bucket;
                _AccessKey.Value = existing.AccessKey;
                _DriveLetter.Value = existing.DriveLetter;
                _ShareName.Value = existing.Share.ShareName ?? string.Empty;
                _AllowedPrincipals.Value = string.Join(", ", existing.Share.AllowedPrincipals);
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
            _Form.Add("Drive letter (e.g. S:)", _DriveLetter, () => _DriveLetter.Value.Trim().Length == 0 ? "Drive letter is required." : null);
            _Form.Add("Auto-mount", _AutoMount);
            _Form.Add("Enable network share", _ShareEnabled);
            _Form.Add("Share name", _ShareName);
            _Form.Add("Share access", _ShareAccess);
            _Form.Add("Allowed principals (comma-separated)", _AllowedPrincipals);

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
                _DriveLetter,   // 9
                null,           // 10 Auto-mount (checkbox)
                null,           // 11 Enable share (checkbox)
                _ShareName,     // 12
                null,           // 13 Share access (radio)
                _AllowedPrincipals // 14
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

            if (_Error != null)
            {
                string message = "! " + _Error;
                if (message.Length > innerWidth) message = message.Substring(0, innerWidth);
                surface.DrawText(x + 2, y + height - 2, message, CellStyle.Default.WithForeground(Color.FromPalette(9)));
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
                DriveLetter = _DriveLetter.Value.Trim(),
                AutoMount = _AutoMount.Checked,
                ShareEnabled = _ShareEnabled.Checked,
                ShareName = NullIfEmpty(_ShareName.Value),
                ShareAccess = _ShareAccess.SelectedIndex == 1 ? ShareAccessEnum.ReadWrite : ShareAccessEnum.ReadOnly,
                AllowedPrincipals = Split(_AllowedPrincipals.Value)
            };

            Close(result);
        }

        private static string? NullIfEmpty(string value)
        {
            string trimmed = value.Trim();
            return trimmed.Length == 0 ? null : trimmed;
        }

        private static List<string> Split(string value)
        {
            List<string> list = new List<string>();
            foreach (string part in value.Split(','))
            {
                string trimmed = part.Trim();
                if (trimmed.Length > 0) list.Add(trimmed);
            }

            return list;
        }
    }
}
