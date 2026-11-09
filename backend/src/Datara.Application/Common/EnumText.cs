using System.Text.Json;

namespace Datara.Application.Common;

/// <summary>
/// Renders enums the same way the JSON contract does.
///
/// The analysis service already emits lower-case tokens for overlapping concepts
/// ("high", "numeric"), so a Pascal-case <c>ToString()</c> here would make the API
/// contract inconsistent with itself and break round-tripping a filter value back
/// into an enum.
/// </summary>
public static class EnumText
{
    private static readonly JsonNamingPolicy Policy = JsonNamingPolicy.CamelCase;

    /// <summary>Lower-camel text for an enum value, for example "high" or "missingValues".</summary>
    public static string Of<TEnum>(TEnum value)
        where TEnum : struct, Enum =>
        Policy.ConvertName(value.ToString() ?? string.Empty);
}
