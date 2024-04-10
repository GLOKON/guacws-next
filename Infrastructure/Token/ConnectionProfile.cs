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
                        default:
                            newProfile.Settings.Add(param.Key, param.Value.ToString());
                            break;
                    }
                });

            return newProfile;
        }
    }
}
