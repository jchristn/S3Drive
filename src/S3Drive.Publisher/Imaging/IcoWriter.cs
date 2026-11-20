namespace S3Drive.Publisher.Imaging
{
    using System;
    using System.Buffers.Binary;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;

    /// <summary>
    /// Writes multi-resolution Windows .ico files: 32-bit DIB frames below 256 pixels and a PNG
    /// frame at 256, the layout Windows Explorer and resource compilers expect. Thread-safe.
    /// </summary>
    public static class IcoWriter
    {
        /// <summary>
        /// Encodes square frames into an .ico.
        /// </summary>
        /// <param name="frames">The frames, each square and at most 256 pixels on a side.</param>
        /// <returns>The .ico file contents.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="frames"/> is null.</exception>
        /// <exception cref="ArgumentException">Thrown when there are no frames, or a frame is not square or is larger than 256 pixels.</exception>
        public static byte[] Encode(IReadOnlyList<RgbaImage> frames)
        {
            if (frames == null) throw new ArgumentNullException(nameof(frames));
            if (!frames.Any()) throw new ArgumentException("An icon needs at least one frame.", nameof(frames));

            List<byte[]> images = new List<byte[]>();
            foreach (RgbaImage frame in frames)
            {
                if (frame == null) throw new ArgumentException("Icon frames cannot be null.", nameof(frames));
                if (frame.Width != frame.Height || frame.Width > 256)
                    throw new ArgumentException("Icon frames must be square and at most 256 pixels; got " + frame.Width + "x" + frame.Height + ".", nameof(frames));
                images.Add(frame.Width == 256 ? PngCodec.Encode(frame) : EncodeDib(frame));
            }

            int headerSize = 6 + 16 * frames.Count;
            using (MemoryStream output = new MemoryStream())
            {
                byte[] directory = new byte[headerSize];
                Span<byte> span = directory.AsSpan();
                BinaryPrimitives.WriteInt16LittleEndian(span.Slice(2, 2), 1);
                BinaryPrimitives.WriteInt16LittleEndian(span.Slice(4, 2), (short)frames.Count);

                int offset = headerSize;
                for (int i = 0; i < frames.Count; i++)
                {
                    int entry = 6 + 16 * i;
                    int side = frames[i].Width;
                    directory[entry] = (byte)(side == 256 ? 0 : side);
                    directory[entry + 1] = (byte)(side == 256 ? 0 : side);
                    BinaryPrimitives.WriteInt16LittleEndian(span.Slice(entry + 4, 2), 1);
                    BinaryPrimitives.WriteInt16LittleEndian(span.Slice(entry + 6, 2), 32);
                    BinaryPrimitives.WriteInt32LittleEndian(span.Slice(entry + 8, 4), images[i].Length);
                    BinaryPrimitives.WriteInt32LittleEndian(span.Slice(entry + 12, 4), offset);
                    offset += images[i].Length;
                }

                output.Write(directory, 0, directory.Length);
                foreach (byte[] image in images)
                {
                    output.Write(image, 0, image.Length);
                }

                return output.ToArray();
            }
        }

        private static byte[] EncodeDib(RgbaImage frame)
        {
            int side = frame.Width;
            int xorSize = side * side * 4;
            int maskRow = (side + 31) / 32 * 4;
            int andSize = maskRow * side;
            byte[] dib = new byte[40 + xorSize + andSize];
            Span<byte> span = dib.AsSpan();

            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(0, 4), 40);
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(4, 4), side);
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(8, 4), side * 2);
            BinaryPrimitives.WriteInt16LittleEndian(span.Slice(12, 2), 1);
            BinaryPrimitives.WriteInt16LittleEndian(span.Slice(14, 2), 32);
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(20, 4), xorSize + andSize);

            for (int y = 0; y < side; y++)
            {
                int rowStart = 40 + (side - 1 - y) * side * 4;
                for (int x = 0; x < side; x++)
                {
                    int s = (y * side + x) * 4;
                    int d = rowStart + x * 4;
                    dib[d] = frame.Pixels[s + 2];
                    dib[d + 1] = frame.Pixels[s + 1];
                    dib[d + 2] = frame.Pixels[s];
                    dib[d + 3] = frame.Pixels[s + 3];
                }
            }

            return dib;
        }
    }
}
