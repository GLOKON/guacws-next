using System.Text;
using GLOKON.GuacWS.Server.Guac;

namespace GLOKON.GuacWS.Server.Tests.Guac
{
    public class GlobalStoreTest
    {
        [Fact]
        public void PingData_BeforeAnyUpdate_IsEmpty()
        {
            var store = new GlobalStore();

            Assert.Empty(store.PingData);
        }

        [Fact]
        public void UpdatePing_EncodesTimestampAsGuacamolePingInstruction()
        {
            var store = new GlobalStore();

            store.UpdatePing(1700000000);

            string message = Encoding.UTF8.GetString(store.PingData);
            Assert.Equal("4.ping,10.1700000000;", message);
        }

        [Fact]
        public void UpdatePing_EndsOnInstructionBoundary()
        {
            // The activity monitor relies on ping messages always being complete
            // instructions, since GuacConnection only splices them in at instruction
            // boundaries - a partial/unterminated ping would corrupt the stream.
            var store = new GlobalStore();

            store.UpdatePing(42);

            Assert.EndsWith(";", Encoding.UTF8.GetString(store.PingData));
        }

        [Fact]
        public void UpdatePing_CalledAgain_ReplacesPreviousPingData()
        {
            var store = new GlobalStore();
            store.UpdatePing(1);
            byte[] firstPing = store.PingData;

            store.UpdatePing(2);

            Assert.NotEqual(Encoding.UTF8.GetString(firstPing), Encoding.UTF8.GetString(store.PingData));
            Assert.Equal("4.ping,1.2;", Encoding.UTF8.GetString(store.PingData));
        }
    }
}
