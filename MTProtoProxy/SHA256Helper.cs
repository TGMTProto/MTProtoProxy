using System.Security.Cryptography;

namespace MTProtoProxy
{
    internal static class SHA256Helper
    {
        public static byte[] ComputeHashsum(byte[] data)
        {
            using (var sha256 = new SHA256Managed())
            {
                return sha256.ComputeHash(data, 0, data.Length);
            }
        }
    }
}
