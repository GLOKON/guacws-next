namespace GLOKON.GuacWS.Server.Guac
{
    internal class GuacOptions
    {
        public GuacDOptions GuacD { get; set; } = new GuacDOptions();

        public string UserDriveRoot { get; set; } = "/user-drives";

        public AllowedParameterOptions AllowedParameters { get; set; } = new AllowedParameterOptions();

        public bool LogTraceMessages { get; set; } = false;

        public int PingFrequency { get; set; } = 500;

        public int Timeout { get; set; } = 10000;
    }
}
