namespace S3Drive.Publisher.Imaging
{
    using System;
    using System.Buffers.Binary;
    using System.IO;

    /// <summary>
    /// Writes 24-bit uncompressed Windows bitmaps, the format every Inno Setup version accepts for
    /// wizard images. Alpha is dropped, so composite onto a background first. Thread-safe.
    /// </summary>
    public static class BmpWriter
    {
        /// <summary>
        /// Encodes an image as a 24-bit BMP.
        /// </summary>
        /// <param name="image">The image; its alpha channel is ignored.</param>
        /// <returns>The BMP file contents.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="image"/> is null.</exception>
        public static byte[] Encode(RgbaImage image)
        {
            if (image == null) throw new ArgumentNullException(nameof(image));

            int rowSize = (image.Width * 3 + 3) / 4 * 4;
            int pixelBytes = rowSize * image.Height;
            const int HeaderSize = 14 + 40;
            byte[] file = new byte[HeaderSize + pixelBytes];
            Span<byte> span = file.AsSpan();

            file[0] = (byte)'B';
            file[1] = (byte)'M';
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(2, 4), file.Length);
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(10, 4), HeaderSize);
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(14, 4), 40);
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(18, 4), image.Width);
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(22, 4), image.Height);
            BinaryPrimitives.WriteInt16LittleEndian(span.Slice(26, 2), 1);
            BinaryPrimitives.WriteInt16LittleEndian(span.Slice(28, 2), 24);
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(34, 4), pixelBytes);
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(38, 4), 2835);
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(42, 4), 2835);

            for (int y = 0; y < image.Height; y++)
            {
                int rowStart = HeaderSize + (image.Height - 1 - y) * rowSize;
                for (int x = 0; x < image.Width; x++)
                {
                    int s = (y * image.Width + x) * 4;
                    int d = rowStart + x * 3;
                    file[d] = image.Pixels[s + 2];
                    file[d + 1] = image.Pixels[s + 1];
                    file[d + 2] = image.Pixels[s];
                }
            }

            return file;
        }

        /// <summary>
        /// Writes an image to a 24-bit BMP file.
        /// </summary>
        /// <param name="image">The image; its alpha channel is ignored.</param>
        /// <param name="path">The output path.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="image"/> is null.</exception>
        /// <exception cref="ArgumentException">Thrown when <paramref name="path"/> is null or empty.</exception>
        /// <exception cref="IOException">Thrown when the file cannot be written.</exception>
        public static void WriteFile(RgbaImage image, string path)
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentException("BMP path must be provided.", nameof(path));
            File.WriteAllBytes(path, Encode(image));
        }
    }
}
