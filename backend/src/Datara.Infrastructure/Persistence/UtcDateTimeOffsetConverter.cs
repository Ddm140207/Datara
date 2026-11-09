using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Datara.Infrastructure.Persistence;

/// <summary>Normalises a <see cref="DateTimeOffset"/> to UTC on the way to the database.</summary>
internal sealed class UtcDateTimeOffsetConverter : ValueConverter<DateTimeOffset, DateTimeOffset>
{
    public UtcDateTimeOffsetConverter()
        : base(
            v => v.ToUniversalTime(),
            v => v.Offset == TimeSpan.Zero ? v : v.ToUniversalTime())
    {
    }
}

/// <summary>Nullable counterpart of <see cref="UtcDateTimeOffsetConverter"/>.</summary>
internal sealed class NullableUtcDateTimeOffsetConverter : ValueConverter<DateTimeOffset?, DateTimeOffset?>
{
    public NullableUtcDateTimeOffsetConverter()
        : base(
            v => v.HasValue ? v.Value.ToUniversalTime() : null,
            v => v.HasValue && v.Value.Offset != TimeSpan.Zero ? v.Value.ToUniversalTime() : v)
    {
    }
}
