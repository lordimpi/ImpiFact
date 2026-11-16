using ImpiFact.Domain.Catalog;
using ImpiFact.Domain.Masters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ImpiFact.Infrastructure.Persistence.Configurations.Masters;

// Terceros y productos (Modelo de datos detallado, sección 3.4).

internal sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("customers");
        builder.Property(customer => customer.IdentificationNumber).HasMaxLength(20);
        builder.Property(customer => customer.Dv).HasMaxLength(1).IsFixedLength();
        builder.Property(customer => customer.Name).HasMaxLength(255);
        builder.Property(customer => customer.Phone).HasMaxLength(30);
        builder.Property(customer => customer.Address).HasMaxLength(255);
        builder.Property(customer => customer.Email).HasMaxLength(255);
        builder.Property(customer => customer.MerchantRegistration).HasMaxLength(50);
        builder.Property(customer => customer.IsActive).HasStoreDefault(true);
        builder.HasCompanyScopedKey();
        builder.BelongsToCompany();
        builder.References<Customer, TypeDocumentIdentification>(customer => customer.TypeDocumentIdentificationId);
        builder.References<Customer, Municipality>(customer => customer.MunicipalityId);
        builder.References<Customer, TypeOrganization>(customer => customer.TypeOrganizationId);
        builder.References<Customer, TypeRegime>(customer => customer.TypeRegimeId);
        builder.References<Customer, TypeLiability>(customer => customer.TypeLiabilityId);
        builder.HasIndex(customer => new { customer.CompanyId, customer.TypeDocumentIdentificationId, customer.IdentificationNumber }).IsUnique();
    }
}

internal sealed class ItemConfiguration : IEntityTypeConfiguration<Item>
{
    public void Configure(EntityTypeBuilder<Item> builder)
    {
        builder.ToTable("items");
        builder.Property(item => item.Code).HasMaxLength(100);
        builder.Property(item => item.Name).HasMaxLength(500);
        builder.Property(item => item.Description).HasMaxLength(1000);
        builder.Property(item => item.SaleUnitPrice).IsQuantity();
        builder.Property(item => item.IsActive).HasStoreDefault(true);
        builder.BelongsToCompany();
        builder.References<Item, UnitMeasure>(item => item.UnitMeasureId);
        builder.References<Item, TypeItemIdentification>(item => item.TypeItemIdentificationId);
        builder.References<Item, Tax>(item => item.TaxId);
        builder.HasIndex(item => new { item.CompanyId, item.Code }).IsUnique();
    }
}
