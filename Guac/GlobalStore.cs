using System;
using System.Text;

namespace GLOKON.GuacWS.Server.Guac
{
    public class GlobalStore
    {
        public byte[] PingData { get; private set; } = Array.Empty<byte>();

        public void UpdatePing(long timestamp)
        {
            PingData = Encoding.UTF8.GetBytes(GuacConnection.FormatProtocolMessage(GuacConnection.PingOpCode, timestamp.ToString()));
        }
    }
}
