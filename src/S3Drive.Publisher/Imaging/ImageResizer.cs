namespace S3Drive.Publisher.Imaging
{
    using System;

    /// <summary>
    /// Resamples images with supersampled bilinear filtering in premultiplied-alpha space, so
    /// transparent edges do not pick up dark fringes. Works for both reduction and enlargement.
    /// Thread-safe: holds no state.
    /// </summary>
    public static class ImageResizer
    {
        /// <summary>
        /// Resizes an image to exact dimensions.
        /// </summary>
        /// <param name="source">The source image.</param>
        /// <param name="width">The target width. Minimum 1.</param>
        /// <param name="height">The target height. Minimum 1.</param>
        /// <returns>The resized image.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="source"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when a target dimension is out of range.</exception>
        public static RgbaImage Resize(RgbaImage source, int width, int height)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));

            RgbaImage target = new RgbaImage(width, height);
            double scaleX = (double)source.Width / width;
            double scaleY = (double)source.Height / height;
            int samplesX = Math.Clamp((int)Math.Ceiling(scaleX) * 2, 2, 16);
            int samplesY = Math.Clamp((int)Math.Ceiling(scaleY) * 2, 2, 16);
            double[] sample = new double[4];

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    double red = 0;
                    double green = 0;
                    double blue = 0;
                    double alpha = 0;
                    for (int j = 0; j < samplesY; j++)
                    {
                        double sy = (y + (j + 0.5) / samplesY) * scaleY;
                        for (int i = 0; i < samplesX; i++)
                        {
                            double sx = (x + (i + 0.5) / samplesX) * scaleX;
                            SampleBilinear(source, sx, sy, sample);
                            red += sample[0];
                            green += sample[1];
                            blue += sample[2];
                            alpha += sample[3];
                        }
                    }

                    int count = samplesX * samplesY;
                    int d = (y * width + x) * 4;
                    double a = alpha / count;
                    if (a > 0)
                    {
                        target.Pixels[d] = ToByte(red / count / a * 255.0);
                        target.Pixels[d + 1] = ToByte(green / count / a * 255.0);
                        target.Pixels[d + 2] = ToByte(blue / count / a * 255.0);
                    }

                    target.Pixels[d + 3] = ToByte(a * 255.0);
                }
            }

            return target;
        }

        /// <summary>
        /// Resizes an image to the largest size that fits within a box, keeping its aspect ratio.
        /// </summary>
        /// <param name="source">The source image.</param>
        /// <param name="maxWidth">The box width. Minimum 1.</param>
        /// <param name="maxHeight">The box height. Minimum 1.</param>
        /// <returns>The resized image; at least 1x1.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="source"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when a box dimension is less than 1.</exception>
        public static RgbaImage Fit(RgbaImage source, int maxWidth, int maxHeight)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (maxWidth < 1) throw new ArgumentOutOfRangeException(nameof(maxWidth), maxWidth, "The box width must be at least 1.");
            if (maxHeight < 1) throw new ArgumentOutOfRangeException(nameof(maxHeight), maxHeight, "The box height must be at least 1.");

            double scale = Math.Min((double)maxWidth / source.Width, (double)maxHeight / source.Height);
            int width = Math.Max(1, (int)Math.Round(source.Width * scale));
            int height = Math.Max(1, (int)Math.Round(source.Height * scale));
            return Resize(source, Math.Min(width, maxWidth), Math.Min(height, maxHeight));
        }

        private static void SampleBilinear(RgbaImage source, double sx, double sy, double[] result)
        {
            double fx = sx - 0.5;
            double fy = sy - 0.5;
            int x0 = (int)Math.Floor(fx);
            int y0 = (int)Math.Floor(fy);
            double tx = fx - x0;
            double ty = fy - y0;

            Array.Clear(result);
            Accumulate(source, x0, y0, (1 - tx) * (1 - ty), result);
            Accumulate(source, x0 + 1, y0, tx * (1 - ty), result);
            Accumulate(source, x0, y0 + 1, (1 - tx) * ty, result);
            Accumulate(source, x0 + 1, y0 + 1, tx * ty, result);
        }

        private static void Accumulate(RgbaImage source, int x, int y, double weight, double[] result)
        {
            if (weight <= 0) return;
            int cx = Math.Clamp(x, 0, source.Width - 1);
            int cy = Math.Clamp(y, 0, source.Height - 1);
            int s = (cy * source.Width + cx) * 4;
            double alpha = source.Pixels[s + 3] / 255.0;
            result[0] += weight * alpha * source.Pixels[s] / 255.0;
            result[1] += weight * alpha * source.Pixels[s + 1] / 255.0;
            result[2] += weight * alpha * source.Pixels[s + 2] / 255.0;
            result[3] += weight * alpha;
        }

        private static byte ToByte(double value)
        {
            if (value <= 0) return 0;
            if (value >= 255) return 255;
            return (byte)Math.Round(value);
        }
    }
}
