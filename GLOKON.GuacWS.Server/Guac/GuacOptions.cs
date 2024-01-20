using System.Collections.Generic;
using GLOKON.GuacWS.Server.Cipher;
using GLOKON.GuacWS.Server.Guac;
using GLOKON.GuacWS.Server.Infrastructure;

namespace GLOKON.GuacWS.Server.Middlewares
{
    internal class GuacOptions
    {
        public GuacDOptions GuacD { get; set; }

        public string UserDriveRoot { get; set; }

        public CipherOptions Cipher { get; set; }

        public AllowedParameterOptions AllowedParameters { get; set; }

        public int Timeout { get; set; }
    }
}
