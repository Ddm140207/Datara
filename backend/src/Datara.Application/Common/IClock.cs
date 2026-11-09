using Datara.Domain.Entities;

namespace Datara.Application.Common;

/// <summary>
/// Abstraction over the system clock so time-dependent logic stays testable.
/// </summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

/// <summary>Default <see cref="IClock"/> backed by the system clock.</summary>
public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}