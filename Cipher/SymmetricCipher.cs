using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace GLOKON.GuacWS.Server.Cipher
{
    internal class SymmetricCipher
    {
        private readonly SymmetricAlgorithm algorithm;
        private readonly byte[] key;

        internal SymmetricCipher(SymmetricAlgorithm algorithm, string key, CipherMode cipherMode, int keySize)
        {
            if (string.IsNullOrEmpty(key))
            {
                throw new ArgumentNullException("key", "Cipher key has not been provided");
            }

            this.algorithm = algorithm;
            this.key = Encoding.UTF8.GetBytes(key);
            this.algorithm.Key = this.key;
            this.algorithm.KeySize = keySize;
            this.algorithm.Mode = cipherMode;
        }

        public string Decrypt(byte[] payload, byte[] iv)
        {
            string plainText = null;

            using (ICryptoTransform decryptor = algorithm.CreateDecryptor(key, iv))
            {
                using (MemoryStream ms = new MemoryStream(payload))
                {
                    using (CryptoStream cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read))
                    {
                        using (StreamReader sr = new StreamReader(cs))
                        {
                            plainText = sr.ReadToEnd();
                        }
                    }
                }
            }

            return plainText;
        }
    }
}
