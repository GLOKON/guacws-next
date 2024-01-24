using System.Buffers;
using System.Linq;
using System;

namespace GLOKON.GuacWS.Server.Extensions
{
    internal static class ReadOnlySequenceExtensions
    {
        public static SequencePosition? LastPositionOf(this ReadOnlySequence<byte> source, byte delimiter)
        {
            var reader = new SequenceReader<byte>(source);

            var delimiterFound = false;
            // Keep reading until we've consumed all delimiters
            while (reader.TryAdvanceTo(delimiter, true))
            {
                delimiterFound = true;
            }

            if (!delimiterFound)
            {
                return null;
            }

            // If we got this far, we've consumed bytes up to,
            // and including, the last byte of the delimiter,
            // so we can use that to get the position of the delimiter
            return reader.Sequence.GetPosition(reader.Consumed);
        }
    }
}