using System.Buffers;
using System.Text;
using GLOKON.GuacWS.Server.Guac;

namespace GLOKON.GuacWS.Server.Tests.Guac
{
    public class GuacConnectionTest
    {
        private static ReadOnlySequence<byte> SingleSegment(string text)
        {
            return new ReadOnlySequence<byte>(Encoding.UTF8.GetBytes(text));
        }

        private static ReadOnlySequence<byte> MultiSegment(params string[] chunks)
        {
            var first = new BufferSegment(Encoding.UTF8.GetBytes(chunks[0]));
            BufferSegment last = first;

            for (int i = 1; i < chunks.Length; i++)
            {
                last = last.Append(Encoding.UTF8.GetBytes(chunks[i]));
            }

            return new ReadOnlySequence<byte>(first, 0, last, last.Memory.Length);
        }

        private static string ToText(ReadOnlySequence<byte> sequence)
        {
            return Encoding.UTF8.GetString(sequence.ToArray());
        }

        [Fact]
        public void TryReadGuacDMessage_NoDelimiter_ReturnsFalseAndLeavesBufferUntouched()
        {
            ReadOnlySequence<byte> buffer = SingleSegment("4.size,4.102");

            bool found = GuacConnection.TryReadGuacDMessage(ref buffer, out _);

            Assert.False(found);
            Assert.Equal("4.size,4.102", ToText(buffer));
        }

        [Fact]
        public void TryReadGuacDMessage_SingleInstruction_ReturnsWholeInstructionIncludingTerminator()
        {
            ReadOnlySequence<byte> buffer = SingleSegment("4.size,4.1024;");

            bool found = GuacConnection.TryReadGuacDMessage(ref buffer, out ReadOnlySequence<byte> message);

            Assert.True(found);
            Assert.Equal("4.size,4.1024;", ToText(message));
            Assert.Equal(0, buffer.Length);
        }

        [Fact]
        public void TryReadGuacDMessage_MultipleInstructions_ReadsOneAtATimeAndAdvancesBuffer()
        {
            ReadOnlySequence<byte> buffer = SingleSegment("3.foo;4.data;");

            bool foundFirst = GuacConnection.TryReadGuacDMessage(ref buffer, out ReadOnlySequence<byte> first);
            Assert.True(foundFirst);
            Assert.Equal("3.foo;", ToText(first));
            Assert.Equal("4.data;", ToText(buffer));

            bool foundSecond = GuacConnection.TryReadGuacDMessage(ref buffer, out ReadOnlySequence<byte> second);
            Assert.True(foundSecond);
            Assert.Equal("4.data;", ToText(second));
            Assert.Equal(0, buffer.Length);
        }

        [Fact]
        public void TryReadGuacDMessage_TrailingPartialInstruction_IsLeftInBufferForNextRead()
        {
            ReadOnlySequence<byte> buffer = SingleSegment("3.foo;4.par");

            bool found = GuacConnection.TryReadGuacDMessage(ref buffer, out ReadOnlySequence<byte> message);

            Assert.True(found);
            Assert.Equal("3.foo;", ToText(message));
            Assert.Equal("4.par", ToText(buffer));

            bool foundSecond = GuacConnection.TryReadGuacDMessage(ref buffer, out _);
            Assert.False(foundSecond);
        }

        [Fact]
        public void TryReadGuacDMessage_InstructionSplitAcrossSegments_IsReadAsOneMessage()
        {
            ReadOnlySequence<byte> buffer = MultiSegment("4.si", "ze,4.1024;");

            bool found = GuacConnection.TryReadGuacDMessage(ref buffer, out ReadOnlySequence<byte> message);

            Assert.True(found);
            Assert.False(message.IsSingleSegment);
            Assert.Equal("4.size,4.1024;", ToText(message));
        }

        [Fact]
        public void EndsOnInstructionBoundary_BufferEndingWithTerminator_ReturnsTrue()
        {
            ReadOnlySequence<byte> buffer = SingleSegment("4.size,4.1024;");

            Assert.True(GuacConnection.EndsOnInstructionBoundary(buffer));
        }

        [Fact]
        public void EndsOnInstructionBoundary_BufferEndingMidInstruction_ReturnsFalse()
        {
            // This is the scenario the ping-splicing bug hit: a chunk read from GuacD that
            // stops partway through an instruction because the read boundary landed there,
            // not because the instruction actually finished.
            ReadOnlySequence<byte> buffer = SingleSegment("4.size,4.1024;5.audio,3.f");

            Assert.False(GuacConnection.EndsOnInstructionBoundary(buffer));
        }

        [Fact]
        public void EndsOnInstructionBoundary_EmptyBuffer_ReturnsFalse()
        {
            ReadOnlySequence<byte> buffer = SingleSegment(string.Empty);

            Assert.False(GuacConnection.EndsOnInstructionBoundary(buffer));
        }

        [Fact]
        public void EndsOnInstructionBoundary_MultiSegmentBufferEndingWithTerminator_ReturnsTrue()
        {
            ReadOnlySequence<byte> buffer = MultiSegment("4.si", "ze,4.1024;");

            Assert.True(GuacConnection.EndsOnInstructionBoundary(buffer));
        }

        [Fact]
        public void EndsOnInstructionBoundary_MultiSegmentBufferEndingMidInstruction_ReturnsFalse()
        {
            ReadOnlySequence<byte> buffer = MultiSegment("4.size,4.1024;5.aud", "io,3.fal");

            Assert.False(GuacConnection.EndsOnInstructionBoundary(buffer));
        }

        private sealed class BufferSegment : ReadOnlySequenceSegment<byte>
        {
            public BufferSegment(byte[] data)
            {
                Memory = data;
            }

            public BufferSegment Append(byte[] data)
            {
                var segment = new BufferSegment(data)
                {
                    RunningIndex = RunningIndex + Memory.Length,
                };

                Next = segment;
                return segment;
            }
        }
    }
}
