using System.Collections.Generic;
using System.Text.Json;

namespace GLOKON.GuacWS.Server.Guac.Parameters
{
    internal class ConnectionProfile
    {
        public ConnectionType Type { get; set; }

        public Dictionary<string, JsonElement> Settings { get; set; } = new Dictionary<string, JsonElement>();
    }
}
