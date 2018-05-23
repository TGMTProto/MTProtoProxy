using System.Security.Cryptography;

namespace MTProtoProxy
{
    public static class AesHelper
    {
        public static ICryptoTransform CreateEncryptorFromAes(in byte[] key)
        {
            using (var aesManaged = new AesManaged())
            {
                aesManaged.Key = key;
                aesManaged.Mode = CipherMode.ECB;
                aesManaged.Padding = PaddingMode.None;
                return aesManaged.CreateEncryptor();
            }
        }
    }
}
