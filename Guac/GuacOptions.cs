using System.Collections.Generic;
using GLOKON.GuacWS.Server.Cipher;
using GLOKON.GuacWS.Server.Guac;
using GLOKON.GuacWS.Server.Infrastructure;

namespace GLOKON.GuacWS.Server.Middlewares
{
    internal class GuacOptions
    {
        public GuacDOptions GuacD { get; set; } = new GuacDOptions();

        public string UserDriveRoot { get; set; } = "/user-drives";

        public CipherOptions Cipher { get; set; } = new CipherOptions();

        public AllowedParameterOptions AllowedParameters { get; set; } = new AllowedParameterOptions();

        public int Timeout { get; set; } = 10000;
    }
}
