namespace GLOKON.GuacWS.Server.Infrastructure
{
    public class ServerOptions
    {
        public string ListenOn { get; set; } = null;

        public string ListenOnSocket { get; set; } = null;

        public ushort HttpPort { get; set; } = 8080;

        public ushort HttpsPort { get; set; } = 8081;

        public bool UseHsts { get; set; } = false;

        public LetsEncryptOptions LetsEncrypt { get; set; } = new LetsEncryptOptions();

        public SslOptions SSL { get; set; } = new SslOptions();

        public bool IsUsingHttps()
        {
            return (LetsEncrypt != null && LetsEncrypt.IsEnabled()) || (SSL != null && SSL.IsEnabled());
        }
    }
}
