using Hadron.Erp.Api.Modules.Identity.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hadron.Erp.Api.Modules.Identity.Infrastructure;

internal sealed class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> b)
    {
        b.ToTable("users", "identity");
        b.Property(x => x.FirstName).HasMaxLength(100).IsRequired();
        b.Property(x => x.LastName).HasMaxLength(100).IsRequired();
        b.Property(x => x.Email).HasMaxLength(256).IsRequired();
        b.Property(x => x.NormalizedEmail).HasMaxLength(256).IsRequired();
        b.Property(x => x.PhoneNumber).HasMaxLength(32).IsRequired();
        b.Property(x => x.ProfilePhotoObjectKey).HasMaxLength(500);
        b.Property(x => x.ProfilePhotoContentType).HasMaxLength(100);
        b.Property(x => x.ProfilePhotoSha256).HasMaxLength(64);
        b.Property(x => x.AccessVersion).IsConcurrencyToken();
        b.HasIndex(x => x.NormalizedEmail).IsUnique();
        b.HasOne(x => x.OrganizationalUnit).WithMany().HasForeignKey(x => x.OrganizationalUnitId)
            .OnDelete(DeleteBehavior.Restrict);
        b.ToTable(t => t.HasCheckConstraint("ck_identity_users_activation_profile",
            "NOT \"IsActive\" OR (\"ProfilePhotoObjectKey\" IS NOT NULL AND \"ProfilePhotoContentType\" IS NOT NULL AND \"ProfilePhotoSize\" > 0)"));
    }
}

internal sealed class ApplicationRoleConfiguration : IEntityTypeConfiguration<ApplicationRole>
{
    public void Configure(EntityTypeBuilder<ApplicationRole> b)
    {
        b.ToTable("roles", "identity");
        b.Property(x => x.DescriptionFa).HasMaxLength(300);
        b.Property(x => x.DescriptionEn).HasMaxLength(300);
    }
}

internal sealed class OrganizationalUnitConfiguration : IEntityTypeConfiguration<OrganizationalUnit>
{
    public void Configure(EntityTypeBuilder<OrganizationalUnit> b)
    {
        b.ToTable("organizational_units", "organization");
        b.HasKey(x => x.Id);
        b.Property(x => x.NameFa).HasMaxLength(200).IsRequired();
        b.Property(x => x.NameEn).HasMaxLength(200);
        b.Property(x => x.Version).IsConcurrencyToken();
    }
}

internal sealed class UserPermissionOverrideConfiguration : IEntityTypeConfiguration<UserPermissionOverride>
{
    public void Configure(EntityTypeBuilder<UserPermissionOverride> b)
    {
        b.ToTable("user_permission_overrides", "identity");
        b.HasKey(x => x.Id);
        b.Property(x => x.PermissionCode).HasMaxLength(200).IsRequired();
        b.Property(x => x.Effect).HasMaxLength(10).IsRequired();
        b.HasIndex(x => new { x.UserId, x.PermissionCode }).IsUnique();
        b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.ToTable(t => t.HasCheckConstraint("ck_identity_user_permission_effect", "\"Effect\" IN ('GRANT','DENY')"));
    }
}

internal sealed class UserSessionConfiguration : IEntityTypeConfiguration<UserSession>
{
    public void Configure(EntityTypeBuilder<UserSession> b)
    {
        b.ToTable("user_sessions", "identity");
        b.HasKey(x => x.Id);
        b.Property(x => x.UserAgent).HasMaxLength(500);
        b.HasIndex(x => new { x.UserId, x.RevokedAtUtc });
        b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class UserDataScopeConfiguration : IEntityTypeConfiguration<UserDataScope>
{
    public void Configure(EntityTypeBuilder<UserDataScope> b)
    {
        b.ToTable("user_data_scopes", "identity");
        b.HasKey(x => x.Id);
        b.Property(x => x.ScopeType).HasMaxLength(50).IsRequired();
        b.Property(x => x.ScopeValue).HasMaxLength(200);
        b.HasIndex(x => new { x.UserId, x.ScopeType, x.ScopeValue }).IsUnique();
        b.HasIndex(x => x.UserId).IsUnique().HasFilter("\"ScopeType\" = 'ALL'");
        b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.ToTable(t => t.HasCheckConstraint("ck_identity_user_data_scope_v1",
            "(\"ScopeType\" = 'ALL' AND \"ScopeValue\" IS NULL) OR " +
            "(\"ScopeType\" = 'WAREHOUSE' AND \"ScopeValue\" ~* '^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$')"));
    }
}

internal sealed class SecurityAuditEventConfiguration : IEntityTypeConfiguration<SecurityAuditEvent>
{
    public void Configure(EntityTypeBuilder<SecurityAuditEvent> b)
    {
        b.ToTable("security_audit_events", "identity");
        b.HasKey(x => x.Id);
        b.Property(x => x.Action).HasMaxLength(200).IsRequired();
        b.Property(x => x.Detail).HasMaxLength(4000);
        b.Property(x => x.CorrelationId).HasMaxLength(200);
        b.Property(x => x.Source).HasMaxLength(50).IsRequired();
        b.HasIndex(x => x.CreatedAtUtc);
        b.HasIndex(x => x.TargetUserId);
    }
}

public static class IdentityTableMapping
{
    public static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<IdentityUserRole<Guid>>().ToTable("user_roles", "identity");
        modelBuilder.Entity<IdentityUserClaim<Guid>>().ToTable("user_claims", "identity");
        modelBuilder.Entity<IdentityUserLogin<Guid>>().ToTable("user_logins", "identity");
        modelBuilder.Entity<IdentityUserToken<Guid>>().ToTable("user_tokens", "identity");
        modelBuilder.Entity<IdentityRoleClaim<Guid>>().ToTable("role_claims", "identity");
    }
}
