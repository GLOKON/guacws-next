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

        public AllowedParameterOptions AllowedParameters { get; set; } = new AllowedParameterOptions();

        public bool LogTraceMessages { get; set; } = false;

        public int ProcessingBufferSize { get; set; } = 4096;

        public int Timeout { get; set; } = 10000;
    }
}
