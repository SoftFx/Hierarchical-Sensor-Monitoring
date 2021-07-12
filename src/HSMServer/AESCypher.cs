using System.Security.Cryptography;
using System.Text;

namespace HSMServer
{
    public static class AESCypher
    {
        public static (string, string, string) Encrypt(string str, byte[] key)
        {
            RandomNumberGenerator.Fill(key);

            using var aes = new AesGcm(key);

            var nonce = new byte[AesGcm.NonceByteSizes.MaxSize];
            RandomNumberGenerator.Fill(nonce);

            var textBytes = Encoding.UTF8.GetBytes(str);
            var cipherText = new byte[textBytes.Length];
            var tag = new byte[AesGcm.TagByteSizes.MaxSize];

            aes.Encrypt(nonce, textBytes, cipherText, tag);

            return (ToString(cipherText), ToString(nonce), ToString(tag));
        }

        public static string Decrypt(string cipher, string n, string t, byte[] key)
        {
            var cipherText = ToBytes(cipher);
            var nonce = ToBytes(n);
            var tag = ToBytes(t);

            using(var aes = new AesGcm(key))
            {
                var textBytes = new byte[cipher.Length];

                aes.Decrypt(nonce, cipherText, tag, textBytes);

                return ToString(textBytes);
            }
        }

        public static byte[] ToBytes(string text) => Encoding.UTF8.GetBytes(text);
        public static string ToString(byte[] bytes) => Encoding.UTF8.GetString(bytes);
    }
}
