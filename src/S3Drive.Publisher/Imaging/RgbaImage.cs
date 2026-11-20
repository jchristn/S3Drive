namespace S3Drive.Publisher.Imaging
{
    using System;

    /// <summary>
    /// An 8-bit-per-channel RGBA raster with straight (non-premultiplied) alpha, stored row-major
    /// from the top-left. Not thread-safe for concurrent writes.
    /// </summary>
    public class RgbaImage
    {
        private readonly int _Width;
        private readonly int _Height;
        private readonly byte[] _Pixels;

        /// <summary>
        /// Creates a fully transparent image.
        /// </summary>
        /// <param name="width">The width in pixels. Minimum 1, maximum 16384.</param>
        /// <param name="height">The height in pixels. Minimum 1, maximum 16384.</param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when a dimension is out of range.</exception>
        public RgbaImage(int width, int height)
        {
            CheckDimension(width, nameof(width));
            CheckDimension(height, nameof(height));
            _Width = width;
            _Height = height;
            _Pixels = new byte[width * height * 4];
        }

        /// <summary>
        /// Wraps existing RGBA pixel data.
        /// </summary>
        /// <param name="width">The width in pixels. Minimum 1, maximum 16384.</param>
        /// <param name="height">The height in pixels. Minimum 1, maximum 16384.</param>
        /// <param name="pixels">The pixels, width * height * 4 bytes. Not copied.</param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when a dimension is out of range.</exception>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="pixels"/> is null.</exception>
        /// <exception cref="ArgumentException">Thrown when <paramref name="pixels"/> has the wrong length.</exception>
        public RgbaImage(int width, int height, byte[] pixels)
        {
            CheckDimension(width, nameof(width));
            CheckDimension(height, nameof(height));
            if (pixels == null) throw new ArgumentNullException(nameof(pixels));
            if (pixels.Length != width * height * 4)
                throw new ArgumentException("Expected " + (width * height * 4) + " bytes of RGBA data for " + width + "x" + height + " but got " + pixels.Length + ".", nameof(pixels));
            _Width = width;
            _Height = height;
            _Pixels = pixels;
        }

        /// <summary>
        /// The width in pixels.
        /// </summary>
        public int Width
        {
            get { return _Width; }
        }

        /// <summary>
        /// The height in pixels.
        /// </summary>
        public int Height
        {
            get { return _Height; }
        }

        /// <summary>
        /// The RGBA pixel data, row-major from the top-left. Writes change the image.
        /// </summary>
        public byte[] Pixels
        {
            get { return _Pixels; }
        }

        /// <summary>
        /// Fills the whole image with one opaque color.
        /// </summary>
        /// <param name="red">Red, 0 to 255.</param>
        /// <param name="green">Green, 0 to 255.</param>
        /// <param name="blue">Blue, 0 to 255.</param>
        public void Fill(byte red, byte green, byte blue)
        {
            for (int i = 0; i < _Pixels.Length; i += 4)
            {
                _Pixels[i] = red;
                _Pixels[i + 1] = green;
                _Pixels[i + 2] = blue;
                _Pixels[i + 3] = 255;
            }
        }

        /// <summary>
        /// Alpha-composites another image over this one with its top-left corner at (x, y).
        /// Parts that fall outside this image are clipped.
        /// </summary>
        /// <param name="source">The image to draw.</param>
        /// <param name="x">The destination column of the source's left edge. May be negative.</param>
        /// <param name="y">The destination row of the source's top edge. May be negative.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="source"/> is null.</exception>
        public void DrawOver(RgbaImage source, int x, int y)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));

            for (int sy = 0; sy < source.Height; sy++)
            {
                int dy = y + sy;
                if (dy < 0 || dy >= _Height) continue;
                for (int sx = 0; sx < source.Width; sx++)
                {
                    int dx = x + sx;
                    if (dx < 0 || dx >= _Width) continue;

                    int s = (sy * source.Width + sx) * 4;
                    int d = (dy * _Width + dx) * 4;
                    double sa = source.Pixels[s + 3] / 255.0;
                    if (sa <= 0) continue;
                    double da = _Pixels[d + 3] / 255.0;
                    double outA = sa + da * (1 - sa);
                    for (int c = 0; c < 3; c++)
                    {
                        double value = (source.Pixels[s + c] * sa + _Pixels[d + c] * da * (1 - sa)) / outA;
                        _Pixels[d + c] = ToByte(value);
                    }

                    _Pixels[d + 3] = ToByte(outA * 255.0);
                }
            }
        }

        /// <summary>
        /// Returns a square copy with this image centered on a transparent background, so icons are
        /// not stretched.
        /// </summary>
        /// <returns>The square image; its side is the larger of width and height.</returns>
        public RgbaImage PadToSquare()
        {
            int side = Math.Max(_Width, _Height);
            RgbaImage square = new RgbaImage(side, side);
            square.DrawOver(this, (side - _Width) / 2, (side - _Height) / 2);
            return square;
        }

        private static byte ToByte(double value)
        {
            if (value <= 0) return 0;
            if (value >= 255) return 255;
            return (byte)Math.Round(value);
        }

        private static void CheckDimension(int value, string name)
        {
            if (value < 1 || value > 16384) throw new ArgumentOutOfRangeException(name, value, "Image dimensions must be between 1 and 16384 pixels.");
        }
    }
}
