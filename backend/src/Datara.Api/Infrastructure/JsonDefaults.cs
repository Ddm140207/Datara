using System.Text.Json.Serialization;

namespace Datara.Api.Infrastructure;

/// <summary>
/// Single JSON contract for the whole API: camelCase members, null omitted, enums
/// as their names so the contract stays readable and stable.
/// </summary>
public static class JsonDefaults
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };
}
