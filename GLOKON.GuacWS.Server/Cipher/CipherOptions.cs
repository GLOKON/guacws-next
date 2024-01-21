using System.Security.Cryptography;

namespace GLOKON.GuacWS.Server.Cipher
{
    public class CipherOptions
    {
        public CipherType Type { get; set; } = CipherType.AES;

        public CipherMode Mode { get; set; } = CipherMode.CBC;

        public int KeySize { get; set; } = 256;

        public string Key { get; set; }
    }
}
