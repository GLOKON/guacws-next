using System.Linq;

namespace GLOKON.GuacWS.Server.Guac
{
    public static class GuacProtocol
    {
        public const string PingOpCode = "ping";
        public const string InternalDataOpCode = "";

        public static string GetData(string parameter)
        {
            // Guac data format of "18.abcefg123"
            return parameter.Substring(parameter.IndexOf('.') + 1);
        }

        public static string FormatProtocolMessage(string opCode, params string[] args)
        {
            // Guac Protocol Format of "OPCODE,ARG1,ARG2,ARG3,...;"
            if (args == null || args.Length == 0)
            {
                return FormatProtocolChunk(opCode) + ";";
            }

            string formattedArgs = string.Empty;
            if (args.Length == 1)
            {
                formattedArgs = FormatProtocolChunk(args[0]);
            }
            else
            {
                formattedArgs = string.Join(",", args.Select(FormatProtocolChunk).ToArray());
            }

            return FormatProtocolChunk(opCode) + "," + formattedArgs + ";";
        }

        private static string FormatProtocolChunk(string chunk)
        {
            string finalChunk = chunk ?? string.Empty;
            return string.Format("{0}.{1}", finalChunk.Length, finalChunk);
        }
    }
}
