using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace GLOKON.GuacWS.Server.Guac.Parameters
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    internal enum ConnectionType
    {
        [EnumMember(Value = "rdp")]
        RDP,
        [EnumMember(Value = "ssh")]
        SSH,
        [EnumMember(Value = "vnc")]
        VNC,
        [EnumMember(Value = "telnet")]
        Telnet,
        [EnumMember(Value = "k8s")]
        K8S,
    }
}
