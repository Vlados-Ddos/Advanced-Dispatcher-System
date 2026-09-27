using System.Text.Json;
using System.Text.Json.Serialization;
namespace AdvancedDispatcherSystem.Host;

public static class Json
{
    public static readonly JsonSerializerOptions Options = new()
    {
        IncludeFields = true,
        PropertyNameCaseInsensitive = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        MaxDepth = 32
    };
    public static byte[] Bytes<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, Options);
    public static T Read<T>(byte[] value) => JsonSerializer.Deserialize<T>(value, Options);
}
