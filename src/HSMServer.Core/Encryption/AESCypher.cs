using System;
using System.Security.Cryptography;
using System.Text;

namespace HSMServer.Core.Encryption
{
    public static class AESCypher
    {
        public static (string, string, string) Encrypt(string str, byte[] key)
        {
            using(var aes = new AesGcm(key))
            {
                var nonce = new byte[AesGcm.NonceByteSizes.MaxSize];
                RandomNumberGenerator.Fill(nonce);

                var tag = new byte[AesGcm.TagByteSizes.MaxSize];
                RandomNumberGenerator.Fill(tag);

                var textBytes = ToBytes(str);
                var cipherText = new byte[textBytes.Length];
                
                aes.Encrypt(nonce, textBytes, cipherText, tag);

                return (Convert.ToBase64String(cipherText),
                    Convert.ToBase64String(nonce), Convert.ToBase64String(tag));
            }
        }

        public static string Decrypt(string cipher, string n, string t, byte[] key)
        {
            var cipherText = Convert.FromBase64String(cipher);
            var nonce = Convert.FromBase64String(n);
            var tag = Convert.FromBase64String(t);

            using(var aes = new AesGcm(key))
            {
                var textBytes = new byte[cipherText.Length];

                aes.Decrypt(nonce, cipherText, tag, textBytes);

                return ToString(textBytes);
            }
        }

        public static byte[] ToBytes(string text) => Encoding.Unicode.GetBytes(text);
        public static string ToString(byte[] bytes) => Encoding.Unicode.GetString(bytes);
    }
}
