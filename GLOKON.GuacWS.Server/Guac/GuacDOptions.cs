namespace GLOKON.GuacWS.Server.Guac
{
    internal class GuacDOptions
    {
        public string Host { get; set; }

        public ushort Port { get; set; }

        public int SendBufferSize { get; set; }

        public int ReceiveBufferSize { get; set; }

        public int SendTimeout { get; set; }

        public int ReceiveTimeout { get; set; }
    }
}
