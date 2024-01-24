using System.Collections.Generic;

namespace GLOKON.GuacWS.Server.Infrastructure
{
    public class LetsEncryptOptions
    {
        public bool UseHsts { get; set; } = false;

        public bool UseStagingServer { get; set; } = false;

        public List<string> Domains { get; set; } = new List<string>();

        public string EmailAddress { get; set; } = string.Empty;

        public bool IsEnabled()
        {
            return Domains?.Count > 0 && !string.IsNullOrEmpty(EmailAddress);
        }
    }
}
