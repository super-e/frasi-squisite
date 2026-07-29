using System.Text.Json;

namespace FrasiSquisite.Shared.Protocol;

public static class ProtocolJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web);
}
