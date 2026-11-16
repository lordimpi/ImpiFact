using ImpiFact.Domain.Companies;
using ImpiFact.Domain.Identity;
using ImpiFact.Domain.Masters;
using ImpiFact.Infrastructure.Persistence.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ImpiFact.Infrastructure.Persistence.Configurations.Identity;

// Plataforma e identidad (Modelo de datos detallado, sección 3.2).

internal sealed class ResellerConfiguration : IEntityTypeConfiguration<Reseller>
{
    public void Configure(EntityTypeBuilder<Reseller> builder)
    {
        builder.ToTable("resellers");
        builder.Property(reseller => reseller.IdentificationNumber).HasMaxLength(20);
        builder.Property(reseller => reseller.Dv).HasMaxLength(1).IsFixedLength();
        builder.Property(reseller => reseller.Name).HasMaxLength(255);
        builder.Property(reseller => reseller.Address).HasMaxLength(255);
        builder.Property(reseller => reseller.Phone).HasMaxLength(30);
        builder.Property(reseller => reseller.Email).HasMaxLength(255);
        builder.Property(reseller => reseller.ContactName).HasMaxLength(255);
        builder.Property(reseller => reseller.PlanLabel).HasMaxLength(100);
        builder.Property(reseller => reseller.IsActive).HasStoreDefault(true);
        builder.Property(reseller => reseller.Observation).HasMaxLength(500);
        builder.HasIndex(reseller => reseller.IdentificationNumber).IsUnique();
        builder.HasIndex(reseller => reseller.Email).IsUnique();
    }
}

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");
        builder.Property(user => user.Email).HasMaxLength(255);
        builder.Property(user => user.PasswordHash).HasMaxLength(500);
        builder.Property(user => user.Name).HasMaxLength(255);
        builder.Property(user => user.Phone).HasMaxLength(30);
        builder.Property(user => user.SecurityStamp).HasMaxLength(100);
        builder.References<User, Reseller>(user => user.ResellerId);
        builder.HasIndex(user => user.Email).IsUnique();
    }
}

internal sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("roles");
        builder.Property(role => role.Name).HasMaxLength(50);
        builder.HasIndex(role => role.Name).IsUnique();
        builder.HasData(PlatformSeed.Roles);
    }
}

internal sealed class UserRoleConfiguration : IEntityTypeConfiguration<UserRole>
{
    public void Configure(EntityTypeBuilder<UserRole> builder)
    {
        builder.ToTable("user_roles");
        builder.HasKey(userRole => new { userRole.UserId, userRole.RoleId });
        builder.References<UserRole, User>(userRole => userRole.UserId);
        builder.References<UserRole, Role>(userRole => userRole.RoleId);
    }
}

internal sealed class UserCompanyConfiguration : IEntityTypeConfiguration<UserCompany>
{
    public void Configure(EntityTypeBuilder<UserCompany> builder)
    {
        builder.ToTable("user_companies");
        builder.BelongsToCompany();
        builder.References<UserCompany, User>(link => link.UserId);
        builder.References<UserCompany, Role>(link => link.RoleId);
        builder.ReferencesInCompany<UserCompany, Customer>(nameof(UserCompany.CustomerId));
        builder.HasIndex(link => new { link.UserId, link.CompanyId, link.RoleId }).IsUnique();
    }
}

internal sealed class ApiKeyConfiguration : IEntityTypeConfiguration<ApiKey>
{
    public void Configure(EntityTypeBuilder<ApiKey> builder)
    {
        builder.ToTable("api_keys");
        builder.Property(key => key.Name).HasMaxLength(100);
        builder.Property(key => key.KeyPrefix).HasMaxLength(12);
        builder.Property(key => key.KeyHash).IsFixed(64);
        builder.Property(key => key.Scopes).HasMaxLength(500);
        builder.References<ApiKey, Company>(key => key.CompanyId);
        builder.References<ApiKey, User>(key => key.CreatedBy);
        builder.HasIndex(key => key.KeyHash).IsUnique();
        builder.HasIndex(key => new { key.CompanyId, key.RevokedAt });
    }
}

internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("refresh_tokens");
        builder.Property(token => token.TokenHash).IsFixed(64);
        builder.Property(token => token.CreatedIp).HasMaxLength(45);
        builder.Property(token => token.UserAgent).HasMaxLength(300);
        builder.References<RefreshToken, User>(token => token.UserId);
        builder.References<RefreshToken, RefreshToken>(token => token.ReplacedBy);
        builder.HasIndex(token => token.TokenHash).IsUnique();
    }
}
