using System.Collections;
using System.Collections.Generic;

namespace GLOKON.GuacWS.Server.Guac
{
    internal class AllowedParameterOptions
    {
        public HashSet<string> Global { get; set; } = new HashSet<string>();

        public Dictionary<string, HashSet<string>> Connection { get; set; }
    }
}
