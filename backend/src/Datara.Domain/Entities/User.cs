using Datara.Domain.Enums;

namespace Datara.Domain.Entities;

/// <summary>A user account. Datara ships single-tenant seed data for the MVP.</summary>
public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Email { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    /// <summary>True when the user signs in with a managed identity provider (future).</summary>
    public bool IsExternalIdentity { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? LastLoginAt { get; set; }

    public ICollection<Dataset> Datasets { get; set; } = new List<Dataset>();
}