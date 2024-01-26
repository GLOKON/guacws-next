namespace GLOKON.GuacWS.Server.Infrastructure.Token
{
    internal class EncryptedToken
    {
        public string IV { get; set; }

        public string Value { get; set; }
    }
}
