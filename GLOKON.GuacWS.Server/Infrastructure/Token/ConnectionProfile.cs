using System.Linq;
using System.Text.Json;

namespace GLOKON.GuacWS.Server.Infrastructure.Token
{
    public class ConnectionProfile : BaseConnectionProfile<string>
    {
        public static ConnectionProfile FromJsonConnectionProfile(BaseConnectionProfile<JsonElement> connectionProfile)
        {
            ConnectionProfile newProfile = new()
            {
                Id = connectionProfile.Id,
                Type = connectionProfile.Type,
                ExistingConnectionId = connectionProfile.ExistingConnectionId,
                Group = connectionProfile.Group,
            };

            connectionProfile.Settings
                .ToList()
                .ForEach(param =>
                {
                    switch (param.Value.ValueKind)
                    {
                        case JsonValueKind.Null:
                            newProfile.Settings.Add(param.Key, null);
                            break;
                        case JsonValueKind.False:
                            newProfile.Settings.Add(param.Key, "false");
                            break;
                        case JsonValueKind.True:
                            newProfile.Settings.Add(param.Key, "true");
                            break;
                        case JsonValueKind.String:
                            newProfile.Settings.Add(param.Key, param.Value.GetString());
                            break;
                        case JsonValueKind.Number:
                            newProfile.Settings.Add(param.Key, param.Value.GetRawText());
                            break;
                        default:
                            // guacd settings are flat strings; an array/object value means the
                            // token was malformed rather than something we can meaningfully flatten.
                            throw new JsonException($"Setting '{param.Key}' has an unsupported value kind '{param.Value.ValueKind}'");
                    }
                });

            return newProfile;
        }
    }
}
