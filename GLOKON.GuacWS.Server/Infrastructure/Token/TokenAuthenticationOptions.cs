using Microsoft.AspNetCore.Authentication;
using System.Text.Json;

namespace GLOKON.GuacWS.Server.Infrastructure.Token
{
    public class TokenAuthenticationOptions : AuthenticationSchemeOptions
    {
        public const string Scheme = "GuacWSTokenAuthenticationScheme";
        public string TokenQueryName { get; set; } = "token";
        public string TokenClaimName { get; set; } = "token";
        public JsonSerializerOptions TokenSerializerOptions { get; set; } = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    }
}
