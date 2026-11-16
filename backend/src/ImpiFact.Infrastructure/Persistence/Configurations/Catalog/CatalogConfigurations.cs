using ImpiFact.Domain.Catalog;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ImpiFact.Infrastructure.Persistence.Configurations.Catalog;

// Una configuración por tabla de catálogo (Modelo de datos detallado, sección 3.1). EF las encuentra por
// reflexión con ApplyConfigurationsFromAssembly; las que solo nombran la tabla tienen la forma base.

internal sealed class TypeDocumentConfiguration() : CatalogConfiguration<TypeDocument>("type_documents")
{
    protected override void ConfigureExtras(EntityTypeBuilder<TypeDocument> builder)
    {
        builder.Property(entry => entry.CufeAlgorithm).HasMaxLength(16);
        builder.Property(entry => entry.Prefix).HasMaxLength(10);
    }
}

internal sealed class TaxConfiguration() : CatalogConfiguration<Tax>("taxes")
{
    protected override void ConfigureExtras(EntityTypeBuilder<Tax> builder) =>
        builder.Property(entry => entry.Description).HasMaxLength(255);
}

internal sealed class TypeDocumentIdentificationConfiguration()
    : CatalogConfiguration<TypeDocumentIdentification>("type_document_identifications")
{
    protected override void ConfigureExtras(EntityTypeBuilder<TypeDocumentIdentification> builder) =>
        builder.Property(entry => entry.RipsCode).HasMaxLength(10);
}

internal sealed class TypeItemIdentificationConfiguration()
    : CatalogConfiguration<TypeItemIdentification>("type_item_identifications")
{
    protected override void ConfigureExtras(EntityTypeBuilder<TypeItemIdentification> builder) =>
        builder.Property(entry => entry.CodeAgency).HasMaxLength(10);
}

/// <summary>Código ISO de 5 caracteres e índice no único: el modelo no lo vuelve único por si aparece otro duplicado.</summary>
internal sealed class CountryConfiguration() : CatalogConfiguration<Country>("countries", CodeIndex.NonUnique, codeLength: 5);

internal sealed class DepartmentConfiguration() : CatalogConfiguration<Department>("departments")
{
    protected override void ConfigureExtras(EntityTypeBuilder<Department> builder) =>
        builder.References<Department, Country>(entry => entry.CountryId);
}

/// <summary>Código DANE de 10 caracteres, único por departamento.</summary>
internal sealed class MunicipalityConfiguration() : CatalogConfiguration<Municipality>("municipalities", CodeIndex.None, codeLength: 10)
{
    protected override void ConfigureExtras(EntityTypeBuilder<Municipality> builder)
    {
        builder.References<Municipality, Department>(entry => entry.DepartmentId);
        builder.HasIndex(entry => new { entry.DepartmentId, entry.Code }).IsUnique();
    }
}

internal sealed class TypeRegimeConfiguration() : CatalogConfiguration<TypeRegime>("type_regimes");

internal sealed class TypeOrganizationConfiguration() : CatalogConfiguration<TypeOrganization>("type_organizations");

internal sealed class TypeLiabilityConfiguration() : CatalogConfiguration<TypeLiability>("type_liabilities");

internal sealed class TypeEnvironmentConfiguration() : CatalogConfiguration<TypeEnvironment>("type_environments");

internal sealed class TypeCurrencyConfiguration() : CatalogConfiguration<TypeCurrency>("type_currencies");

/// <summary>Índice no único: el PHP repite MGM, ONZ, PD y SO.</summary>
internal sealed class UnitMeasureConfiguration() : CatalogConfiguration<UnitMeasure>("unit_measures", CodeIndex.NonUnique);

/// <summary>Índice no único: los códigos 10 y 11 se repiten para el documento soporte.</summary>
internal sealed class TypeOperationConfiguration() : CatalogConfiguration<TypeOperation>("type_operations", CodeIndex.NonUnique);

internal sealed class TypeDiscountConfiguration() : CatalogConfiguration<TypeDiscount>("type_discounts");

internal sealed class DiscountConfiguration() : CatalogConfiguration<Discount>("discounts");

internal sealed class ReferencePriceConfiguration() : CatalogConfiguration<ReferencePrice>("reference_prices");

internal sealed class PaymentFormConfiguration() : CatalogConfiguration<PaymentForm>("payment_forms");

internal sealed class PaymentMethodConfiguration() : CatalogConfiguration<PaymentMethod>("payment_methods");

internal sealed class TypeRejectionConfiguration() : CatalogConfiguration<TypeRejection>("type_rejections");

internal sealed class EventConfiguration() : CatalogConfiguration<Event>("events");

internal sealed class IncotermConfiguration() : CatalogConfiguration<Incoterm>("incoterms");

internal sealed class PrepaidPaymentTypeConfiguration() : CatalogConfiguration<PrepaidPaymentType>("prepaid_payment_types");

internal sealed class TypeGenerationTransmitionConfiguration()
    : CatalogConfiguration<TypeGenerationTransmition>("type_generation_transmitions");

internal sealed class TypeSpdConfiguration() : CatalogConfiguration<TypeSpd>("type_spds");

/// <summary>Índice no único: 185 pares de códigos solo difieren en mayúsculas (<c>aa</c>/<c>AA</c>).</summary>
internal sealed class LanguageConfiguration() : CatalogConfiguration<Language>("languages", CodeIndex.NonUnique);

internal sealed class CreditNoteDiscrepancyResponseConfiguration()
    : CatalogConfiguration<CreditNoteDiscrepancyResponse>("credit_note_discrepancy_responses");

internal sealed class DebitNoteDiscrepancyResponseConfiguration()
    : CatalogConfiguration<DebitNoteDiscrepancyResponse>("debit_note_discrepancy_responses");

internal sealed class CreditNoteDiscrepancyResponseSdConfiguration()
    : CatalogConfiguration<CreditNoteDiscrepancyResponseSd>("credit_note_discrepancy_responses_sd");
