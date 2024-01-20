using System.Collections.Generic;

namespace GLOKON.GuacWS.Server.Guac.Parameters
{
    internal class ConnectionProfile
    {
        public ConnectionType Type { get; set; }

        public Dictionary<string, string> Settings { get; set; }
    }
}
