using GLOKON.GuacWS.Server.Guac;

namespace GLOKON.GuacWS.Server.Tests.Guac
{
    public class GuacProtocolTest
    {
        [Fact]
        public void FormatProtocolMessage_NoArgs_ProducesOpCodeOnly()
        {
            string result = GuacProtocol.FormatProtocolMessage("select");

            Assert.Equal("6.select;", result);
        }

        [Fact]
        public void FormatProtocolMessage_NullArgsArray_ProducesOpCodeOnly()
        {
            string result = GuacProtocol.FormatProtocolMessage("select", null);

            Assert.Equal("6.select;", result);
        }

        [Fact]
        public void FormatProtocolMessage_SingleArg_IsLengthPrefixed()
        {
            string result = GuacProtocol.FormatProtocolMessage("connect", "rdp");

            Assert.Equal("7.connect,3.rdp;", result);
        }

        [Fact]
        public void FormatProtocolMessage_MultipleArgs_AreCommaSeparatedAndLengthPrefixed()
        {
            string result = GuacProtocol.FormatProtocolMessage("size", "1024", "768", "96");

            Assert.Equal("4.size,4.1024,3.768,2.96;", result);
        }

        [Fact]
        public void FormatProtocolMessage_EmptyArg_IsFormattedAsZeroLength()
        {
            string result = GuacProtocol.FormatProtocolMessage("connect", "");

            Assert.Equal("7.connect,0.;", result);
        }

        [Fact]
        public void FormatProtocolMessage_NullArg_IsTreatedAsEmptyString()
        {
            string result = GuacProtocol.FormatProtocolMessage("connect", null, "value");

            Assert.Equal("7.connect,0.,5.value;", result);
        }

        [Fact]
        public void FormatProtocolMessage_ArgContainingDelimiters_LengthCoversWholeValue()
        {
            // Guacamole's length-prefix framing is what lets a value legally contain
            // ',' and ';' without being mistaken for instruction structure.
            string result = GuacProtocol.FormatProtocolMessage("connect", "a,b;c");

            Assert.Equal("7.connect,5.a,b;c;", result);
        }

        [Theory]
        [InlineData("18.abcdefghijklmnop", "abcdefghijklmnop")]
        [InlineData("0.", "")]
        [InlineData("3.foo", "foo")]
        public void GetData_ExtractsValueAfterLengthPrefix(string parameter, string expected)
        {
            string result = GuacProtocol.GetData(parameter);

            Assert.Equal(expected, result);
        }

        [Fact]
        public void FormatProtocolMessage_ThenGetData_RoundTrips()
        {
            string formatted = GuacProtocol.FormatProtocolMessage("size", "1920");
            string arg = formatted.Split(',')[1].TrimEnd(';');

            Assert.Equal("1920", GuacProtocol.GetData(arg));
        }
    }
}
