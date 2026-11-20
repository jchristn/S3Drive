namespace S3Drive.Publisher.Imaging
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;

    /// <summary>
    /// Generates every installer image from the one source logo, so no one hand-maintains a set of
    /// sized copies. Thread-safe: holds no mutable state.
    /// </summary>
    public class BrandingGenerator
    {
        private static readonly int[] _IconSizes = new int[] { 16, 20, 24, 32, 40, 48, 64, 96, 128, 256 };
        private static readonly int[] _ScalePercents = new int[] { 100, 125, 150, 200 };
        private const int LargeWidth = 164;
        private const int LargeHeight = 314;
        private const int SmallSide = 55;

        private readonly RgbaImage _Logo;

        /// <summary>
        /// Loads the source logo.
        /// </summary>
        /// <param name="logoPath">The source PNG.</param>
        /// <exception cref="ArgumentException">Thrown when <paramref name="logoPath"/> is null or empty.</exception>
        /// <exception cref="PublisherException">Thrown when the logo cannot be read or decoded.</exception>
        public BrandingGenerator(string logoPath)
        {
            if (string.IsNullOrEmpty(logoPath)) throw new ArgumentException("Logo path must be provided.", nameof(logoPath));
            try
            {
                _Logo = PngCodec.DecodeFile(logoPath);
            }
            catch (InvalidDataException ex)
            {
                throw new PublisherException("The logo could not be decoded: " + ex.Message, ex);
            }
            catch (IOException ex)
            {
                throw new PublisherException("The logo could not be read: " + ex.Message, ex);
            }
        }

        /// <summary>
        /// Initializes the generator from an already decoded logo.
        /// </summary>
        /// <param name="logo">The logo.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="logo"/> is null.</exception>
        public BrandingGenerator(RgbaImage logo)
        {
            _Logo = logo ?? throw new ArgumentNullException(nameof(logo));
        }

        /// <summary>
        /// The icon frame sizes written to the .ico, in pixels.
        /// </summary>
        public static IReadOnlyList<int> IconSizes
        {
            get { return _IconSizes; }
        }

        /// <summary>
        /// Writes a multi-resolution .ico of the logo, padded to a square.
        /// </summary>
        /// <param name="path">The output path.</param>
        /// <exception cref="ArgumentException">Thrown when <paramref name="path"/> is null or empty.</exception>
        /// <exception cref="IOException">Thrown when the file cannot be written.</exception>
        public void WriteIcon(string path)
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentException("Icon path must be provided.", nameof(path));

            RgbaImage square = _Logo.PadToSquare();
            List<RgbaImage> frames = new List<RgbaImage>();
            foreach (int size in _IconSizes)
            {
                frames.Add(ImageResizer.Resize(square, size, size));
            }

            File.WriteAllBytes(path, IcoWriter.Encode(frames));
        }

        /// <summary>
        /// Writes the Inno Setup wizard bitmaps at 100%, 125%, 150%, and 200% scaling: the logo
        /// centered on the background color.
        /// </summary>
        /// <param name="directory">The output directory; created if missing.</param>
        /// <param name="background">The background color as #RRGGBB.</param>
        /// <returns>The written files.</returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="directory"/> is null or empty, or <paramref name="background"/> is not #RRGGBB.</exception>
        /// <exception cref="IOException">Thrown when a file cannot be written.</exception>
        public WizardImageSet WriteWizardImages(string directory, string background)
        {
            if (string.IsNullOrEmpty(directory)) throw new ArgumentException("Output directory must be provided.", nameof(directory));
            byte[] color = ParseColor(background);
            Directory.CreateDirectory(directory);

            WizardImageSet set = new WizardImageSet();
            foreach (int percent in _ScalePercents)
            {
                int largeWidth = LargeWidth * percent / 100;
                int largeHeight = LargeHeight * percent / 100;
                RgbaImage large = Compose(largeWidth, largeHeight, (int)(largeWidth * 0.72), (int)(largeHeight * 0.40), 0.45, color);
                string largePath = Path.Combine(directory, "wizard-" + percent + ".bmp");
                BmpWriter.WriteFile(large, largePath);
                set.LargeImages.Add(largePath);

                int smallSide = SmallSide * percent / 100;
                int inset = (int)(smallSide * 0.90);
                RgbaImage small = Compose(smallSide, smallSide, inset, inset, 0.5, color);
                string smallPath = Path.Combine(directory, "wizard-small-" + percent + ".bmp");
                BmpWriter.WriteFile(small, smallPath);
                set.SmallImages.Add(smallPath);
            }

            return set;
        }

        /// <summary>
        /// Parses a #RRGGBB color.
        /// </summary>
        /// <param name="value">The color.</param>
        /// <returns>Three bytes: red, green, blue.</returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="value"/> is not #RRGGBB.</exception>
        public static byte[] ParseColor(string value)
        {
            if (value == null || value.Length != 7 || value[0] != '#') throw new ArgumentException("Color '" + value + "' must be #RRGGBB.", nameof(value));

            byte[] color = new byte[3];
            for (int i = 0; i < 3; i++)
            {
                if (!byte.TryParse(value.AsSpan(1 + i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out color[i]))
                    throw new ArgumentException("Color '" + value + "' must be #RRGGBB.", nameof(value));
            }

            return color;
        }

        private RgbaImage Compose(int width, int height, int boxWidth, int boxHeight, double verticalCenter, byte[] color)
        {
            RgbaImage canvas = new RgbaImage(width, height);
            canvas.Fill(color[0], color[1], color[2]);
            RgbaImage logo = ImageResizer.Fit(_Logo, Math.Max(1, boxWidth), Math.Max(1, boxHeight));
            int x = (width - logo.Width) / 2;
            int y = (int)(height * verticalCenter) - logo.Height / 2;
            canvas.DrawOver(logo, x, Math.Max(0, y));
            return canvas;
        }
    }
}
