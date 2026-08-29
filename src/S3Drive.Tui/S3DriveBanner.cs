namespace S3Drive.Tui
{
    using System;
    using System.Collections.Generic;
    using S3Drive.Core;
    using TUIKit.Ascii;
    using TUIKit.Ascii.Fonts;

    /// <summary>
    /// The S3Drive ASCII-art wordmark and the startup splash text. The wordmark is rendered with
    /// TUIKit's built-in FIGlet engine using the same Small font as the Armor and mux consoles, so it
    /// is always correctly aligned.
    /// </summary>
    public static class S3DriveBanner
    {
        /// <summary>
        /// Builds the startup splash lines: the wordmark, a blank line, the version and copyright, a
        /// blank line, and the project URL. The splash modal appends its own dismissal hint below these.
        /// </summary>
        /// <param name="version">The product version string (for example, <c>0.1.0</c>). May be null or empty, in which case <c>0.1.0</c> is used.</param>
        /// <returns>The splash content lines. Never null.</returns>
        public static IReadOnlyList<string> SplashLines(string version)
        {
            List<string> lines = new List<string>();
            foreach (string row in WordmarkLines())
                lines.Add(row);

            lines.Add(string.Empty);
            lines.Add("v" + (string.IsNullOrEmpty(version) ? "0.1.0" : version) + " " + Constants.ReleaseLabel + " - " + Constants.Copyright);
            lines.Add(string.Empty);
            lines.Add(Constants.RepositoryUrl);
            return lines;
        }

        /// <summary>
        /// Renders the "s3drive" wordmark with the TUIKit Small font, padded so every row is the same
        /// width (so it centers cleanly and lays out in a fixed-width column). Falls back to plain
        /// spaced text if the font engine is unavailable.
        /// </summary>
        /// <returns>The wordmark rows. Never null or empty.</returns>
        public static string[] WordmarkLines()
        {
            List<string> rows = new List<string>();
            try
            {
                foreach (string row in AsciiArt.Render("s3drive", new SmallAsciiFont()))
                    rows.Add(row);
            }
            catch (Exception)
            {
                rows.Clear();
                rows.Add("s 3 d r i v e");
            }

            // The FIGlet font can include blank rows above and below the glyphs; drop them so the
            // wordmark has no leading blank line and occupies exactly its glyph height.
            while (rows.Count > 0 && rows[0].Trim().Length == 0)
                rows.RemoveAt(0);
            while (rows.Count > 0 && rows[rows.Count - 1].Trim().Length == 0)
                rows.RemoveAt(rows.Count - 1);
            if (rows.Count == 0)
                rows.Add("s 3 d r i v e");

            int width = 0;
            foreach (string row in rows)
                width = Math.Max(width, row.Length);
            for (int i = 0; i < rows.Count; i++)
                rows[i] = rows[i].PadRight(width);

            return rows.ToArray();
        }
    }
}
