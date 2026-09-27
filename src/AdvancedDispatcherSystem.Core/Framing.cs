using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace AdvancedDispatcherSystem.Core
{
    public static class Framing
    {
        public static async Task<byte[]> Read(Stream stream, CancellationToken cancel, int maximum = Protocol.MaxFrameBytes)
        {
            var header = new byte[4];
            await Exactly(stream, header, cancel).ConfigureAwait(false);
            int length = header[0] | header[1] << 8 | header[2] << 16 | header[3] << 24;
            if (length <= 0 || length > maximum || length > Protocol.MaxFrameBytes) throw new InvalidDataException("FRAME_SIZE");
            var payload = new byte[length];
            await Exactly(stream, payload, cancel).ConfigureAwait(false);
            return payload;
        }
        private static async Task Exactly(Stream stream, byte[] bytes, CancellationToken cancel)
        {
            int offset = 0;
            while (offset < bytes.Length)
            {
                int count = await stream.ReadAsync(bytes, offset, bytes.Length - offset, cancel).ConfigureAwait(false);
                if (count == 0) throw new EndOfStreamException();
                offset += count;
            }
        }
        public static Task Write(Stream stream, byte[] bytes, CancellationToken cancel) => Write(stream, bytes, bytes.Length, cancel);
        public static async Task Write(Stream stream, byte[] bytes, int length, CancellationToken cancel)
        {
            if (length <= 0 || length > Protocol.MaxFrameBytes || length > bytes.Length) throw new InvalidDataException("FRAME_SIZE");
            var header = new[] { (byte)length, (byte)(length >> 8), (byte)(length >> 16), (byte)(length >> 24) };
            await stream.WriteAsync(header, 0, 4, cancel).ConfigureAwait(false);
            await stream.WriteAsync(bytes, 0, length, cancel).ConfigureAwait(false);
            await stream.FlushAsync(cancel).ConfigureAwait(false);
        }
    }
}
