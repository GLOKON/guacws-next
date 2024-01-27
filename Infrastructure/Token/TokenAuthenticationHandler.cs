using GLOKON.GuacWS.Server.Cipher;
using GLOKON.GuacWS.Server.Guac;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using System.Linq;
using System.Text;
using System;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.Tasks;
using System.Security.Claims;
using System.Collections.Generic;
using GLOKON.GuacWS.Server.Exceptions;

namespace GLOKON.GuacWS.Server.Infrastructure.Token
{
    internal class TokenAuthenticationHandler : AuthenticationHandler<TokenAuthenticationOptions>
    {
        private readonly GuacOptions guacOptions;
        private readonly SymmetricCipher cipher;

        public TokenAuthenticationHandler(
            IOptionsMonitor<TokenAuthenticationOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder,
            IOptions<GuacOptions> guacOptions,
            SymmetricCipher cipher) : base(options, logger, encoder)
        {
            this.guacOptions = guacOptions.Value;
            this.cipher = cipher;
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            try
            {
                if (!Request.Query.TryGetValue(Options.TokenQueryName, out StringValues tokenQuery))
                {
                    throw new TokenMissingOrInvalidException("Token is missing or invalid from the query string");
                }

                try
                {
                    string rawToken = tokenQuery.ToString();

                    // Decrypt the token and parse it
                    string encryptedTokenValue = Encoding.UTF8.GetString(Convert.FromBase64String(rawToken));
                    EncryptedToken parsedEncryptedToken = JsonSerializer.Deserialize<EncryptedToken>(encryptedTokenValue, Options.TokenSerializerOptions);
                    string tokenValue = cipher.Decrypt(Convert.FromBase64String(parsedEncryptedToken.Value), Convert.FromBase64String(parsedEncryptedToken.IV));
                    Token guacToken = JsonSerializer.Deserialize<Token>(tokenValue, Options.TokenSerializerOptions);

                    // TODO: Is there a way to do this?
                    ConnectionProfile newToken = new()
                    {
                        Id = guacToken.Connection.Id,
                        Type = guacToken.Connection.Type,
                        ExistingConnectionId = guacToken.Connection.ExistingConnectionId,
                        Group = guacToken.Connection.Group,
                    };

                    guacToken.Connection.Settings
                        .ToList()
                        .ForEach(param =>
                        {
                            switch (param.Value.ValueKind)
                            {
                                case JsonValueKind.Null:
                                    newToken.Settings.Add(param.Key, null);
                                    break;
                                case JsonValueKind.False:
                                    newToken.Settings.Add(param.Key, "false");
                                    break;
                                case JsonValueKind.True:
                                    newToken.Settings.Add(param.Key, "true");
                                    break;
                                default:
                                    newToken.Settings.Add(param.Key, param.Value.ToString());
                                    break;
                            }
                        });

                    HashSet<string> allowedUntrustedParams = new(guacOptions.AllowedParameters.Global);
                    string connectionType = newToken.Type.ToString().ToLower();

                    if (guacOptions.AllowedParameters.Connection.TryGetValue(connectionType, out HashSet<string> allowedConnParams))
                    {
                        allowedUntrustedParams.UnionWith(allowedConnParams);
                    }

                    Request.Query
                        .Where(param =>
                        {
                            // Token is a reserved keyword
                            return param.Key != Options.TokenQueryName && allowedUntrustedParams.Contains(param.Key);
                        })
                        .ToList()
                        .ForEach(param =>
                        {
                            // Add or update the params
                            newToken.Settings[param.Key] = param.Value.ToString();
                        });

                    var claims = new List<Claim>() { new(Options.TokenClaimName, JsonSerializer.Serialize(newToken, Options.TokenSerializerOptions)) };

                    return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, "TokenAuth")), Scheme.Name)));
                }
                catch (Exception)
                {
                    throw new TokenMissingOrInvalidException("Token is missing or invalid from the query string");
                }
            }
            catch (Exception ex)
            {
                return Task.FromResult(AuthenticateResult.Fail(ex));
            }
        }
    }
}
