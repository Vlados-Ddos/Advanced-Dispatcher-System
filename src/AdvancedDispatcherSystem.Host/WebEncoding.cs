using System.IO.Compression;
namespace AdvancedDispatcherSystem.Host;

public static class WebEncoding
{
    // A binary WebSocket message is a gzip-compressed UTF-8 JSON envelope.
    // Compression happens once per published revision, before fan-out.
    public static byte[] Encode<T>(T value)
    {
        var json = Json.Bytes(value); if (json.Length < 8192) return json;
        using var stream = new MemoryStream(json.Length / 4);
        using (var gzip = new GZipStream(stream, CompressionLevel.Fastest, true)) gzip.Write(json);
        return stream.ToArray();
    }
}
