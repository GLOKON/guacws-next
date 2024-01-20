using System.Security.Cryptography;

namespace GLOKON.GuacWS.Server.Cipher
{
    public class CipherOptions
    {
        public CipherType Type { get; set; }

        public CipherMode Mode { get; set; }

        public int BlockSize { get; set; }

        public string Key { get; set; }
    }
}
