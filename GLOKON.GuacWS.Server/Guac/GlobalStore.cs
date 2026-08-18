using System;
using System.Text;

namespace GLOKON.GuacWS.Server.Guac
{
    public class GlobalStore
    {
        public byte[] PingData { get; private set; } = [];

        public void UpdatePing(long timestamp)
        {
            PingData = Encoding.UTF8.GetBytes(GuacProtocol.FormatProtocolMessage(GuacProtocol.PingOpCode, timestamp.ToString()));
        }
    }
}
