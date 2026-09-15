using System;

namespace GLOKON.GuacWS.Server.Exceptions
{
    public class TokenMissingOrInvalidException : Exception
    {
        public TokenMissingOrInvalidException(string message) : base(message)
        {
        }

        public TokenMissingOrInvalidException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
