using System.Text.Json;
using GLOKON.GuacWS.Server.Infrastructure.Token;

namespace GLOKON.GuacWS.Server.Tests.Infrastructure.Token
{
    public class ConnectionProfileTest
    {
        private static JsonElement Parse(string json)
        {
            return JsonDocument.Parse(json).RootElement;
        }

        private static BaseConnectionProfile<JsonElement> BuildProfile(string settingsJson)
        {
            JsonElement root = Parse($$"""{"settings":{{settingsJson}}}""");

            var profile = new TestJsonConnectionProfile
            {
                Id = "profile-1",
                Type = ConnectionType.RDP,
                Group = "group-1",
                ExistingConnectionId = "existing-1",
            };

            foreach (JsonProperty property in root.GetProperty("settings").EnumerateObject())
            {
                profile.Settings[property.Name] = property.Value;
            }

            return profile;
        }

        [Fact]
        public void FromJsonConnectionProfile_CopiesTopLevelFields()
        {
            BaseConnectionProfile<JsonElement> source = BuildProfile("{}");

            ConnectionProfile result = ConnectionProfile.FromJsonConnectionProfile(source);

            Assert.Equal("profile-1", result.Id);
            Assert.Equal(ConnectionType.RDP, result.Type);
            Assert.Equal("group-1", result.Group);
            Assert.Equal("existing-1", result.ExistingConnectionId);
        }

        [Fact]
        public void FromJsonConnectionProfile_StringSetting_IsCopiedAsPlainString()
        {
            BaseConnectionProfile<JsonElement> source = BuildProfile("""{"hostname":"10.0.0.1"}""");

            ConnectionProfile result = ConnectionProfile.FromJsonConnectionProfile(source);

            Assert.Equal("10.0.0.1", result.Settings["hostname"]);
        }

        [Fact]
        public void FromJsonConnectionProfile_TrueSetting_IsLoweredToStringTrue()
        {
            BaseConnectionProfile<JsonElement> source = BuildProfile("""{"enable-drive":true}""");

            ConnectionProfile result = ConnectionProfile.FromJsonConnectionProfile(source);

            Assert.Equal("true", result.Settings["enable-drive"]);
        }

        [Fact]
        public void FromJsonConnectionProfile_FalseSetting_IsLoweredToStringFalse()
        {
            BaseConnectionProfile<JsonElement> source = BuildProfile("""{"enable-drive":false}""");

            ConnectionProfile result = ConnectionProfile.FromJsonConnectionProfile(source);

            Assert.Equal("false", result.Settings["enable-drive"]);
        }

        [Fact]
        public void FromJsonConnectionProfile_NullSetting_IsCopiedAsNull()
        {
            BaseConnectionProfile<JsonElement> source = BuildProfile("""{"password":null}""");

            ConnectionProfile result = ConnectionProfile.FromJsonConnectionProfile(source);

            Assert.Null(result.Settings["password"]);
        }

        [Fact]
        public void FromJsonConnectionProfile_NumberSetting_IsStringifiedViaToString()
        {
            BaseConnectionProfile<JsonElement> source = BuildProfile("""{"port":3389}""");

            ConnectionProfile result = ConnectionProfile.FromJsonConnectionProfile(source);

            Assert.Equal("3389", result.Settings["port"]);
        }

        [Fact]
        public void FromJsonConnectionProfile_MultipleSettings_AllArePresentInResult()
        {
            BaseConnectionProfile<JsonElement> source = BuildProfile(
                """{"hostname":"10.0.0.1","port":3389,"enable-drive":true,"password":null}""");

            ConnectionProfile result = ConnectionProfile.FromJsonConnectionProfile(source);

            Assert.Equal(4, result.Settings.Count);
            Assert.Equal("10.0.0.1", result.Settings["hostname"]);
            Assert.Equal("3389", result.Settings["port"]);
            Assert.Equal("true", result.Settings["enable-drive"]);
            Assert.Null(result.Settings["password"]);
        }

        private sealed class TestJsonConnectionProfile : BaseConnectionProfile<JsonElement>
        {
        }
    }
}
