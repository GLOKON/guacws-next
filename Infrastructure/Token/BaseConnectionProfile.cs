using System.Collections.Generic;

namespace GLOKON.GuacWS.Server.Infrastructure.Token
{
    public abstract class BaseConnectionProfile<T>
    {
        public string Id { get; set; }

        public ConnectionType Type { get; set; }

        public string Group { get; set; }

        public Dictionary<string, T> Settings { get; set; } = [];
    }
}
