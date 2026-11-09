using System.Text.Json;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Datara.Infrastructure.Persistence.Configurations;

/// <summary>
/// Value comparer for jsonb-mapped collections.
///
/// EF's default comparer for <see cref="List{T}"/> compares element references, which
/// means a nested edit is invisible to change tracking and the column is written
/// twice or not at all. Comparing the serialised form makes the comparison match
/// what actually lands in the database.
/// </summary>
internal static class JsonValueComparer
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    /// <param name="T">Element type, not the collection type.</param>
    public static ValueComparer<List<T>> ForElements<T>() => new(
        (a, b) => Serialise(a) == Serialise(b),
        v => Serialise(v).GetHashCode(StringComparison.Ordinal),
        v => JsonSerializer.Deserialize<List<T>>(Serialise(v), Options) ?? new List<T>());

    private static string Serialise<T>(List<T>? value) =>
        JsonSerializer.Serialize(value, Options);
}
