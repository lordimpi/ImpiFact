using ImpiFact.Domain.Catalog;
using ImpiFact.Domain.Companies;
using ImpiFact.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ImpiFact.Infrastructure.Persistence.Configurations.Companies;

// Empresa y configuración (Modelo de datos detallado, sección 3.3).

internal sealed class CompanyConfiguration : IEntityTypeConfiguration<Company>
{
    public void Configure(EntityTypeBuilder<Company> builder)
    {
        builder.ToTable("companies");
        builder.Property(company => company.IdentificationNumber).HasMaxLength(20);
        builder.Property(company => company.Dv).IsFixed(1);
        builder.Property(company => company.Name).HasMaxLength(255);
        builder.Property(company => company.Email).HasMaxLength(255);
        builder.Property(company => company.Phone).HasMaxLength(30);
        builder.Property(company => company.Address).HasMaxLength(255);
        builder.Property(company => company.MerchantRegistration).HasMaxLength(50);
        builder.Property(company => company.GraphicRepresentationTemplate).HasStoreDefault(2);
        builder.Property(company => company.AllowSellerLogin).HasStoreDefault(false);
        builder.Property(company => company.IsActive).HasStoreDefault(true);
        builder.HasIndex(company => company.IdentificationNumber).IsUnique();

        builder.References<Company, Reseller>(company => company.ResellerId);
        builder.References<Company, Language>(company => company.LanguageId);
        builder.References<Company, Country>(company => company.CountryId);
        builder.References<Company, Municipality>(company => company.MunicipalityId);
        builder.References<Company, Tax>(company => company.TaxId);
        builder.References<Company, TypeCurrency>(company => company.TypeCurrencyId);
        builder.References<Company, TypeOperation>(company => company.TypeOperationId);
        builder.References<Company, TypeDocumentIdentification>(company => company.TypeDocumentIdentificationId);
        builder.References<Company, TypeOrganization>(company => company.TypeOrganizationId);
        builder.References<Company, TypeRegime>(company => company.TypeRegimeId);
    }
}

internal sealed class CompanyLiabilityConfiguration : IEntityTypeConfiguration<CompanyLiability>
{
    public void Configure(EntityTypeBuilder<CompanyLiability> builder)
    {
        builder.ToTable("company_liabilities");
        builder.HasKey(liability => new { liability.CompanyId, liability.TypeLiabilityId });
        builder.BelongsToCompany();
        builder.References<CompanyLiability, TypeLiability>(liability => liability.TypeLiabilityId);
    }
}

internal sealed class CompanyEnvironmentConfiguration : IEntityTypeConfiguration<CompanyEnvironment>
{
    public void Configure(EntityTypeBuilder<CompanyEnvironment> builder)
    {
        builder.ToTable("company_environments");
        builder.BelongsToCompany();
        builder.References<CompanyEnvironment, TypeEnvironment>(environment => environment.TypeEnvironmentId);
        builder.HasIndex(environment => new { environment.CompanyId, environment.DocumentKind }).IsUnique();
    }
}

internal sealed class CompanySoftwareConfiguration : IEntityTypeConfiguration<CompanySoftware>
{
    public void Configure(EntityTypeBuilder<CompanySoftware> builder)
    {
        builder.ToTable("company_software");
        builder.Property(software => software.SoftwareIdentifier).HasMaxLength(64);
        builder.Property(software => software.PinCipher).HasMaxLength(512);
        builder.Property(software => software.DianUrl).HasMaxLength(255);
        builder.Property(software => software.TestSetId).HasMaxLength(64);
        builder.BelongsToCompany();
        builder.References<CompanySoftware, TypeEnvironment>(software => software.TypeEnvironmentId);
        builder.HasIndex(software => new { software.CompanyId, software.DocumentKind, software.TypeEnvironmentId }).IsUnique();
    }
}

internal sealed class CertificateConfiguration : IEntityTypeConfiguration<Certificate>
{
    public void Configure(EntityTypeBuilder<Certificate> builder)
    {
        builder.ToTable("certificates");
        builder.Property(certificate => certificate.Name).HasMaxLength(255);
        builder.Property(certificate => certificate.SecretRef).HasMaxLength(512);
        builder.Property(certificate => certificate.Thumbprint).HasMaxLength(64);
        builder.Property(certificate => certificate.Subject).HasMaxLength(500);
        builder.Property(certificate => certificate.Issuer).HasMaxLength(500);
        builder.BelongsToCompany();
        builder.References<Certificate, User>(certificate => certificate.UploadedBy);

        // Un solo certificado activo por empresa. Cada motor escribe el filtro a su manera (MySQL, sin índices parciales,
        // con una columna generada): Modelo de datos detallado, sección 8, trampa 1.
        builder.HasIndex(certificate => certificate.CompanyId).HasDatabaseName("ux_certificates_active").IsUnique()
            .HasPortableFilter(PortableIndexFilterKind.WhereTrue, "is_active");
        builder.HasIndex(certificate => certificate.NotAfter).HasDatabaseName("ix_certificates_not_after");
    }
}

internal sealed class ResolutionConfiguration : IEntityTypeConfiguration<Resolution>
{
    public void Configure(EntityTypeBuilder<Resolution> builder)
    {
        builder.ToTable("resolutions", table => table.HasCheckConstraint("ck_resolutions_range", "range_from <= range_to"));
        builder.Property(resolution => resolution.Prefix).HasMaxLength(10);
        builder.Property(resolution => resolution.ResolutionNumber).HasMaxLength(50);
        builder.Property(resolution => resolution.TechnicalKey).HasMaxLength(128);
        builder.HasCompanyScopedKey();
        builder.BelongsToCompany();
        builder.References<Resolution, TypeDocument>(resolution => resolution.DocumentTypeId);
        builder.References<Resolution, TypeEnvironment>(resolution => resolution.TypeEnvironmentId);
        builder.HasIndex(resolution => new { resolution.CompanyId, resolution.DocumentTypeId, resolution.TypeEnvironmentId, resolution.IsActive });
    }
}

internal sealed class CompanyMailSettingsConfiguration : IEntityTypeConfiguration<CompanyMailSettings>
{
    public void Configure(EntityTypeBuilder<CompanyMailSettings> builder)
    {
        builder.ToTable("company_mail_settings");
        builder.HasKey(settings => settings.CompanyId);
        builder.Property(settings => settings.Host).HasMaxLength(255);
        builder.Property(settings => settings.Username).HasMaxLength(255);
        builder.Property(settings => settings.PasswordCipher).HasMaxLength(512);
        builder.Property(settings => settings.Encryption).HasMaxLength(10);
        builder.Property(settings => settings.FromAddress).HasMaxLength(255);
        builder.Property(settings => settings.FromName).HasMaxLength(255);
        builder.BelongsToCompany();
    }
}

internal sealed class CompanyLogoConfiguration : IEntityTypeConfiguration<CompanyLogo>
{
    public void Configure(EntityTypeBuilder<CompanyLogo> builder)
    {
        builder.ToTable("company_logos");
        builder.HasKey(logo => logo.CompanyId);
        builder.Property(logo => logo.StorageKey).HasMaxLength(500);
        builder.Property(logo => logo.ContentType).HasMaxLength(100);
        builder.BelongsToCompany();
    }
}
