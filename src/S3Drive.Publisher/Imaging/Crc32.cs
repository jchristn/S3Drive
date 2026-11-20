namespace S3Drive.Publisher.Imaging
{
    using System;

    /// <summary>
    /// The CRC-32 (IEEE 802.3, polynomial 0xEDB88320) that PNG chunks carry. Thread-safe.
    /// </summary>
    public static class Crc32
    {
        private static readonly uint[] _Table = BuildTable();

        /// <summary>
        /// Computes the CRC-32 of a byte range.
        /// </summary>
        /// <param name="data">The data.</param>
        /// <param name="offset">The first byte to include.</param>
        /// <param name="count">The number of bytes to include.</param>
        /// <returns>The CRC.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="data"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the range falls outside <paramref name="data"/>.</exception>
        public static uint Compute(byte[] data, int offset, int count)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (offset < 0 || count < 0 || offset + count > data.Length) throw new ArgumentOutOfRangeException(nameof(count), "The range " + offset + "+" + count + " falls outside the " + data.Length + "-byte buffer.");

            uint crc = 0xFFFFFFFFu;
            for (int i = offset; i < offset + count; i++)
            {
                crc = _Table[(crc ^ data[i]) & 0xFF] ^ (crc >> 8);
            }

            return crc ^ 0xFFFFFFFFu;
        }

        private static uint[] BuildTable()
        {
            uint[] table = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                uint c = n;
                for (int k = 0; k < 8; k++)
                {
                    c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                }

                table[n] = c;
            }

            return table;
        }
    }
}
