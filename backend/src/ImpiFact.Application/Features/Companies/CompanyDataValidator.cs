using System.Net.Mail;
using FluentValidation;
using ImpiFact.Application.Abstractions.Companies;
using ImpiFact.Domain.Catalog;

namespace ImpiFact.Application.Features.Companies;

/// <summary>
/// Reglas de <c>ConfigurationRequest.php</c> para los datos de la empresa, con sus mismos textos. Los campos se
/// informan con su nombre JSON. Los identificadores de catálogo se revisan contra la base (regla <c>exists</c> del
/// PHP); los largos máximos salen de las columnas del modelo.
/// </summary>
public abstract class CompanyDataValidator<T> : AbstractValidator<T>
    where T : CompanyData
{
    protected CompanyDataValidator(ICatalogLookup catalogs)
    {
        RuleFor(data => data.Nit)
            .Must(nit => Nit.IsDigits(nit) && nit!.Length <= Nit.MaxDigits)
            .WithMessage(PhpMessages.DigitsBetween("nit", 1, Nit.MaxDigits))
            .OverridePropertyName("nit");

        RuleFor(data => data.Dv)
            .Must((data, dv) => IsCheckDigitOf(data.Nit, dv))
            .WithMessage(PhpMessages.InvalidDv)
            .OverridePropertyName("dv");

        RequiredCatalog<TypeDocumentIdentification>(catalogs, data => data.TypeDocumentIdentificationId, "type_document_identification_id");
        RequiredCatalog<TypeOrganization>(catalogs, data => data.TypeOrganizationId, "type_organization_id");
        RequiredCatalog<TypeRegime>(catalogs, data => data.TypeRegimeId, "type_regime_id");
        RequiredCatalog<Municipality>(catalogs, data => data.MunicipalityId, "municipality_id");
        OptionalCatalog<Language>(catalogs, data => data.LanguageId, "language_id");
        OptionalCatalog<Tax>(catalogs, data => data.TaxId, "tax_id");
        OptionalCatalog<TypeOperation>(catalogs, data => data.TypeOperationId, "type_operation_id");
        OptionalCatalog<Country>(catalogs, data => data.CountryId, "country_id");
        OptionalCatalog<TypeCurrency>(catalogs, data => data.TypeCurrencyId, "type_currency_id");

        // Una responsabilidad como mínimo, en type_liability_id o en type_liability_ids (anexo A.8).
        RuleFor(data => data.TypeLiabilityId)
            .NotNull()
            .When(data => data.TypeLiabilityIds is not { Count: > 0 })
            .WithMessage(PhpMessages.Required("type_liability_id"))
            .OverridePropertyName("type_liability_id");
        RuleFor(data => data.TypeLiabilityId)
            .MustAsync((id, ct) => catalogs.ExistsAsync<TypeLiability>(id!.Value, ct))
            .When(data => data.TypeLiabilityId is not null)
            .WithMessage(PhpMessages.Exists("type_liability_id"))
            .OverridePropertyName("type_liability_id");
        RuleFor(data => data.TypeLiabilityIds)
            .Must(ids => ids!.Count > 0)
            .When(data => data.TypeLiabilityIds is not null)
            .WithMessage(PhpMessages.MinItems("type_liability_ids", 1))
            .OverridePropertyName("type_liability_ids");
        RuleForEach(data => data.TypeLiabilityIds)
            .MustAsync((id, ct) => catalogs.ExistsAsync<TypeLiability>(id, ct))
            .WithMessage(PhpMessages.Exists("type_liability_ids"))
            .OverridePropertyName("type_liability_ids");

        RequiredText(data => data.BusinessName, "business_name", 255);
        RequiredText(data => data.MerchantRegistration, "merchant_registration", 50);
        RequiredText(data => data.Address, "address", 255);

        RuleFor(data => data.Phone)
            .NotEmpty().WithMessage(PhpMessages.Required("phone"))
            .Must(phone => Nit.IsDigits(phone) && phone!.Length is >= 7 and <= 10)
            .WithMessage(PhpMessages.DigitsBetween("phone", 7, 10))
            .OverridePropertyName("phone");

        RuleFor(data => data.Email)
            .NotEmpty().WithMessage(PhpMessages.Required("email"))
            .Must(IsEmail).WithMessage(PhpMessages.Email("email"))
            .MaximumLength(255).WithMessage(PhpMessages.MaxLength("email", 255))
            .OverridePropertyName("email");
    }

    /// <summary>Un identificador de catálogo obligatorio que debe existir.</summary>
    protected void RequiredCatalog<TCatalog>(ICatalogLookup catalogs, Func<T, int?> field, string name)
        where TCatalog : CatalogEntry
    {
        RuleFor(data => field(data))
            .NotNull().WithMessage(PhpMessages.Required(name))
            .OverridePropertyName(name);
        OptionalCatalog<TCatalog>(catalogs, field, name);
    }

    /// <summary>Un identificador de catálogo opcional que, si llega, debe existir.</summary>
    protected void OptionalCatalog<TCatalog>(ICatalogLookup catalogs, Func<T, int?> field, string name)
        where TCatalog : CatalogEntry =>
        RuleFor(data => field(data))
            .MustAsync((id, ct) => catalogs.ExistsAsync<TCatalog>(id!.Value, ct))
            .When(data => field(data) is not null)
            .WithMessage(PhpMessages.Exists(name))
            .OverridePropertyName(name);

    private void RequiredText(Func<T, string?> field, string name, int maxLength) =>
        RuleFor(data => field(data))
            .NotEmpty().WithMessage(PhpMessages.Required(name))
            .MaximumLength(maxLength).WithMessage(PhpMessages.MaxLength(name, maxLength))
            .OverridePropertyName(name);

    private static bool IsCheckDigitOf(string? nit, string? dv) =>
        dv is { Length: 1 } && char.IsAsciiDigit(dv[0])
        && Nit.IsDigits(nit) && nit!.Length <= Nit.MaxDigits
        && Nit.CheckDigit(nit) == dv[0] - '0';

    private static bool IsEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return true;
        }

        try
        {
            var address = new MailAddress(email);
            return address.Address == email && email.Contains('.', StringComparison.Ordinal);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
