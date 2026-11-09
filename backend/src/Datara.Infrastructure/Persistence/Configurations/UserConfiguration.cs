using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Datara.Infrastructure.Persistence.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");
        builder.HasKey(u => u.Id);

        builder.Property(u => u.Id).HasColumnName("id");
        builder.Property(u => u.Email).HasColumnName("email").HasMaxLength(320).IsRequired();
        builder.Property(u => u.DisplayName).HasColumnName("display_name").HasMaxLength(200).IsRequired();
        builder.Property(u => u.IsExternalIdentity).HasColumnName("is_external_identity").HasDefaultValue(false);
        builder.Property(u => u.CreatedAt).HasColumnName("created_at");
        builder.Property(u => u.LastLoginAt).HasColumnName("last_login_at");

        builder.HasIndex(u => u.Email).IsUnique().HasDatabaseName("ix_users_email");
    }
}
