namespace S3Drive.Publisher.Imaging
{
    using System;
    using System.Buffers.Binary;
    using System.IO;
    using System.IO.Compression;
    using System.Text;

    /// <summary>
    /// A minimal PNG decoder and encoder: enough to read a logo and to write the PNG frames inside
    /// an .ico. Decodes 8-bit grayscale, RGB, palette, gray+alpha, and RGBA, non-interlaced.
    /// Encodes 8-bit RGBA. Thread-safe: holds no state.
    /// </summary>
    public static class PngCodec
    {
        private static readonly byte[] _Signature = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        /// <summary>
        /// Decodes a PNG file.
        /// </summary>
        /// <param name="path">The file path.</param>
        /// <returns>The image.</returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="path"/> is null or empty.</exception>
        /// <exception cref="InvalidDataException">Thrown when the file is not a PNG this decoder supports.</exception>
        /// <exception cref="IOException">Thrown when the file cannot be read.</exception>
        public static RgbaImage DecodeFile(string path)
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentException("PNG path must be provided.", nameof(path));
            try
            {
                return Decode(File.ReadAllBytes(path));
            }
            catch (InvalidDataException ex)
            {
                throw new InvalidDataException(path + ": " + ex.Message, ex);
            }
        }

        /// <summary>
        /// Decodes PNG bytes.
        /// </summary>
        /// <param name="data">The PNG file contents.</param>
        /// <returns>The image.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="data"/> is null.</exception>
        /// <exception cref="InvalidDataException">Thrown when the data is not a PNG this decoder supports.</exception>
        public static RgbaImage Decode(byte[] data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (data.Length < _Signature.Length || !data.AsSpan(0, _Signature.Length).SequenceEqual(_Signature))
                throw new InvalidDataException("Not a PNG: the 8-byte signature is missing.");

            int width = 0;
            int height = 0;
            byte bitDepth = 0;
            byte colorType = 0;
            byte interlace = 0;
            byte[]? palette = null;
            byte[]? transparency = null;
            bool sawHeader = false;
            byte[] compressed;

            using (MemoryStream idat = new MemoryStream())
            {
                int offset = _Signature.Length;
                bool sawEnd = false;
                while (!sawEnd)
                {
                    if (offset + 8 > data.Length) throw new InvalidDataException("PNG ended before its IEND chunk.");
                    int length = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(offset, 4));
                    string type = Encoding.ASCII.GetString(data, offset + 4, 4);
                    int start = offset + 8;
                    if (length < 0 || (long)start + length + 4 > data.Length) throw new InvalidDataException("PNG chunk '" + type + "' runs past the end of the data.");

                    switch (type)
                    {
                        case "IHDR":
                            if (length < 13) throw new InvalidDataException("PNG IHDR chunk is too short.");
                            width = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(start, 4));
                            height = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(start + 4, 4));
                            bitDepth = data[start + 8];
                            colorType = data[start + 9];
                            interlace = data[start + 12];
                            sawHeader = true;
                            break;
                        case "PLTE":
                            palette = data.AsSpan(start, length).ToArray();
                            break;
                        case "tRNS":
                            transparency = data.AsSpan(start, length).ToArray();
                            break;
                        case "IDAT":
                            idat.Write(data, start, length);
                            break;
                        case "IEND":
                            sawEnd = true;
                            break;
                    }

                    offset = start + length + 4;
                }

                compressed = idat.ToArray();
            }

            if (!sawHeader) throw new InvalidDataException("PNG has no IHDR chunk.");
            if (width < 1 || height < 1 || width > 16384 || height > 16384) throw new InvalidDataException("PNG size " + width + "x" + height + " is out of range.");
            if (bitDepth != 8) throw new InvalidDataException("PNG bit depth " + bitDepth + " is not supported; save the image as 8 bits per channel.");
            if (interlace != 0) throw new InvalidDataException("Interlaced PNGs are not supported; save the image without interlacing.");
            int channels = ChannelsFor(colorType);
            if (colorType == 3 && palette == null) throw new InvalidDataException("Palette PNG has no PLTE chunk.");

            byte[] raw = Inflate(compressed);
            int stride = width * channels;
            if (raw.Length < (long)height * (stride + 1)) throw new InvalidDataException("PNG image data is shorter than its declared size.");

            byte[] pixels = new byte[width * height * 4];
            byte[] previous = new byte[stride];
            byte[] current = new byte[stride];
            int position = 0;
            for (int y = 0; y < height; y++)
            {
                byte filter = raw[position];
                Buffer.BlockCopy(raw, position + 1, current, 0, stride);
                position += stride + 1;
                Unfilter(filter, current, previous, channels, y);
                ConvertRow(current, pixels, y * width * 4, width, colorType, palette, transparency);

                byte[] swap = previous;
                previous = current;
                current = swap;
            }

            return new RgbaImage(width, height, pixels);
        }

        /// <summary>
        /// Encodes an image as an 8-bit RGBA PNG.
        /// </summary>
        /// <param name="image">The image.</param>
        /// <returns>The PNG file contents.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="image"/> is null.</exception>
        public static byte[] Encode(RgbaImage image)
        {
            if (image == null) throw new ArgumentNullException(nameof(image));

            int stride = image.Width * 4;
            byte[] raw = new byte[image.Height * (stride + 1)];
            for (int y = 0; y < image.Height; y++)
            {
                raw[y * (stride + 1)] = 0;
                Buffer.BlockCopy(image.Pixels, y * stride, raw, y * (stride + 1) + 1, stride);
            }

            byte[] compressed;
            using (MemoryStream buffer = new MemoryStream())
            {
                using (ZLibStream zlib = new ZLibStream(buffer, CompressionLevel.Optimal, true))
                {
                    zlib.Write(raw, 0, raw.Length);
                }

                compressed = buffer.ToArray();
            }

            byte[] header = new byte[13];
            BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0, 4), image.Width);
            BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4, 4), image.Height);
            header[8] = 8;
            header[9] = 6;

            using (MemoryStream output = new MemoryStream())
            {
                output.Write(_Signature, 0, _Signature.Length);
                WriteChunk(output, "IHDR", header);
                WriteChunk(output, "IDAT", compressed);
                WriteChunk(output, "IEND", Array.Empty<byte>());
                return output.ToArray();
            }
        }

        private static int ChannelsFor(byte colorType)
        {
            switch (colorType)
            {
                case 0: return 1;
                case 2: return 3;
                case 3: return 1;
                case 4: return 2;
                case 6: return 4;
                default: throw new InvalidDataException("PNG color type " + colorType + " is not valid.");
            }
        }

        private static byte[] Inflate(byte[] compressed)
        {
            try
            {
                using (MemoryStream input = new MemoryStream(compressed))
                using (ZLibStream zlib = new ZLibStream(input, CompressionMode.Decompress))
                using (MemoryStream output = new MemoryStream())
                {
                    zlib.CopyTo(output);
                    return output.ToArray();
                }
            }
            catch (InvalidDataException ex)
            {
                throw new InvalidDataException("PNG image data is not valid zlib: " + ex.Message, ex);
            }
        }

        private static void Unfilter(byte filter, byte[] current, byte[] previous, int bytesPerPixel, int row)
        {
            for (int i = 0; i < current.Length; i++)
            {
                int left = i >= bytesPerPixel ? current[i - bytesPerPixel] : 0;
                int up = previous[i];
                int upLeft = i >= bytesPerPixel ? previous[i - bytesPerPixel] : 0;
                int predictor;
                switch (filter)
                {
                    case 0: predictor = 0; break;
                    case 1: predictor = left; break;
                    case 2: predictor = up; break;
                    case 3: predictor = (left + up) / 2; break;
                    case 4: predictor = Paeth(left, up, upLeft); break;
                    default: throw new InvalidDataException("PNG row " + row + " uses unknown filter type " + filter + ".");
                }

                current[i] = (byte)(current[i] + predictor);
            }
        }

        private static int Paeth(int left, int up, int upLeft)
        {
            int estimate = left + up - upLeft;
            int distanceLeft = Math.Abs(estimate - left);
            int distanceUp = Math.Abs(estimate - up);
            int distanceUpLeft = Math.Abs(estimate - upLeft);
            if (distanceLeft <= distanceUp && distanceLeft <= distanceUpLeft) return left;
            if (distanceUp <= distanceUpLeft) return up;
            return upLeft;
        }

        private static void ConvertRow(byte[] row, byte[] pixels, int offset, int width, byte colorType, byte[]? palette, byte[]? transparency)
        {
            for (int x = 0; x < width; x++)
            {
                int d = offset + x * 4;
                switch (colorType)
                {
                    case 0:
                        pixels[d] = pixels[d + 1] = pixels[d + 2] = row[x];
                        pixels[d + 3] = 255;
                        break;
                    case 2:
                        pixels[d] = row[x * 3];
                        pixels[d + 1] = row[x * 3 + 1];
                        pixels[d + 2] = row[x * 3 + 2];
                        pixels[d + 3] = 255;
                        break;
                    case 3:
                        int index = row[x];
                        if (palette == null || index * 3 + 2 >= palette.Length) throw new InvalidDataException("PNG palette index " + index + " is outside the palette.");
                        pixels[d] = palette[index * 3];
                        pixels[d + 1] = palette[index * 3 + 1];
                        pixels[d + 2] = palette[index * 3 + 2];
                        pixels[d + 3] = transparency != null && index < transparency.Length ? transparency[index] : (byte)255;
                        break;
                    case 4:
                        pixels[d] = pixels[d + 1] = pixels[d + 2] = row[x * 2];
                        pixels[d + 3] = row[x * 2 + 1];
                        break;
                    default:
                        Buffer.BlockCopy(row, x * 4, pixels, d, 4);
                        break;
                }
            }
        }

        private static void WriteChunk(Stream output, string type, byte[] body)
        {
            byte[] typeBytes = Encoding.ASCII.GetBytes(type);
            byte[] lengthBytes = new byte[4];
            BinaryPrimitives.WriteInt32BigEndian(lengthBytes, body.Length);

            byte[] crcInput = new byte[typeBytes.Length + body.Length];
            Buffer.BlockCopy(typeBytes, 0, crcInput, 0, typeBytes.Length);
            Buffer.BlockCopy(body, 0, crcInput, typeBytes.Length, body.Length);
            byte[] crcBytes = new byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(crcBytes, Crc32.Compute(crcInput, 0, crcInput.Length));

            output.Write(lengthBytes, 0, 4);
            output.Write(crcInput, 0, crcInput.Length);
            output.Write(crcBytes, 0, 4);
        }
    }
}
