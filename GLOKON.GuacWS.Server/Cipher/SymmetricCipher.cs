using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace GLOKON.GuacWS.Server.Cipher
{
    internal class SymmetricCipher
    {
        private readonly SymmetricAlgorithm algorithm;

        internal SymmetricCipher(SymmetricAlgorithm algorithm, CipherMode cipherMode, int blockSize)
        {
            this.algorithm = algorithm;
            this.algorithm.BlockSize = blockSize;
            this.algorithm.Mode = cipherMode;
        }

        public string Decrypt(byte[] payload, byte[] iv, string key)
        {
            byte[] keyData = Encoding.UTF8.GetBytes(key);
            algorithm.Key = keyData;
            algorithm.IV = iv;

            string plainText = null;

            using (ICryptoTransform decryptor = algorithm.CreateDecryptor(keyData, iv))
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
