namespace GLOKON.GuacWS.Server.Guac
{
    internal class GuacDOptions
    {
        public string Host { get; set; } = "127.0.0.1";

        public ushort Port { get; set; } = 4822;

        public bool TcpNoDelay { get; set; } = false;

        public int SendBufferSize { get; set; } = 8192;

        public int ReceiveBufferSize { get; set; } = 8192;

        public int SendTimeout { get; set; } = 1000;

        public int ReceiveTimeout { get; set; } = 0;
    }
}
