namespace S3Drive.Publisher
{
    using System;
    using System.Security.Cryptography;
    using System.Text;

    /// <summary>
    /// Name-based (version 5, SHA-1) UUIDs per RFC 4122. The Windows installer AppId is derived
    /// from the reverse-DNS identifier this way, so it stays stable across releases without a
    /// hand-copied GUID. Thread-safe.
    /// </summary>
    public static class DeterministicGuid
    {
        /// <summary>
        /// The RFC 4122 DNS namespace (6ba7b810-9dad-11d1-80b4-00c04fd430c8).
        /// </summary>
        public static readonly Guid DnsNamespace = new Guid("6ba7b810-9dad-11d1-80b4-00c04fd430c8");

        /// <summary>
        /// Creates a version 5 UUID from a namespace and a name.
        /// </summary>
        /// <param name="namespaceId">The namespace UUID.</param>
        /// <param name="name">The name, hashed as UTF-8.</param>
        /// <returns>The UUID.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="name"/> is null.</exception>
        public static Guid Create(Guid namespaceId, string name)
        {
            if (name == null) throw new ArgumentNullException(nameof(name));

            byte[] namespaceBytes = namespaceId.ToByteArray();
            SwapByteOrder(namespaceBytes);
            byte[] nameBytes = Encoding.UTF8.GetBytes(name);

            byte[] input = new byte[namespaceBytes.Length + nameBytes.Length];
            Buffer.BlockCopy(namespaceBytes, 0, input, 0, namespaceBytes.Length);
            Buffer.BlockCopy(nameBytes, 0, input, namespaceBytes.Length, nameBytes.Length);
            byte[] hash = SHA1.HashData(input);

            byte[] result = new byte[16];
            Array.Copy(hash, result, 16);
            result[6] = (byte)((result[6] & 0x0F) | 0x50);
            result[8] = (byte)((result[8] & 0x3F) | 0x80);
            SwapByteOrder(result);
            return new Guid(result);
        }

        private static void SwapByteOrder(byte[] guid)
        {
            Swap(guid, 0, 3);
            Swap(guid, 1, 2);
            Swap(guid, 4, 5);
            Swap(guid, 6, 7);
        }

        private static void Swap(byte[] bytes, int left, int right)
        {
            byte temp = bytes[left];
            bytes[left] = bytes[right];
            bytes[right] = temp;
        }
    }
}
