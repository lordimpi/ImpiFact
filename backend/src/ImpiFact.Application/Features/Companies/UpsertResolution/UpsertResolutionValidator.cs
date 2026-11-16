using System.Globalization;
using FluentValidation;
using ImpiFact.Application.Abstractions.Companies;
using ImpiFact.Domain.Catalog;

namespace ImpiFact.Application.Features.Companies.UpsertResolution;

/// <summary>
/// Reglas de <c>ConfigurationResolutionRequest.php</c>: prefijo de hasta 4 caracteres; rango obligatorio con
/// <c>to</c> mayor que <c>from</c>; para la factura (tipo 1) son obligatorios el número, la fecha, la clave técnica y
/// la vigencia, que termina después de empezar. Las fechas van en formato <c>Y-m-d</c>.
/// </summary>
public sealed class UpsertResolutionValidator : AbstractValidator<UpsertResolutionCommand>
{
    /// <summary>Tipo de documento «factura electrónica de venta».</summary>
    public const int InvoiceType = 1;

    public const int MaxPrefixLength = 4;

    public UpsertResolutionValidator(ICatalogLookup catalogs)
    {
        RuleFor(command => command.TypeDocumentId)
            .NotNull().WithMessage(PhpMessages.Required("type_document_id"))
            .MustAsync((id, ct) => catalogs.ExistsAsync<TypeDocument>(id!.Value, ct))
            .When(command => command.TypeDocumentId is not null)
            .WithMessage(PhpMessages.Exists("type_document_id"))
            .OverridePropertyName("type_document_id");

        RuleFor(command => command.Prefix)
            .MaximumLength(MaxPrefixLength).WithMessage(PhpMessages.MaxLength("prefix", MaxPrefixLength))
            .OverridePropertyName("prefix");

        RequiredForInvoice(command => command.Resolution, "resolution");
        RequiredForInvoice(command => command.TechnicalKey, "technical_key");
        RequiredForInvoice(command => command.ResolutionDate, "resolution_date");
        RequiredForInvoice(command => command.DateFrom, "date_from");
        RequiredForInvoice(command => command.DateTo, "date_to");
        Date(command => command.ResolutionDate, "resolution_date");
        Date(command => command.DateFrom, "date_from");
        Date(command => command.DateTo, "date_to");

        RuleFor(command => command.Resolution)
            .MaximumLength(50).WithMessage(PhpMessages.MaxLength("resolution", 50))
            .OverridePropertyName("resolution");
        RuleFor(command => command.TechnicalKey)
            .MaximumLength(128).WithMessage(PhpMessages.MaxLength("technical_key", 128))
            .OverridePropertyName("technical_key");

        RuleFor(command => command.DateTo)
            .Must((command, dateTo) => ParseDate(dateTo) > ParseDate(command.DateFrom))
            .When(command => ParseDate(command.DateFrom) is not null && ParseDate(command.DateTo) is not null)
            .WithMessage(PhpMessages.After("date_to", "date_from"))
            .OverridePropertyName("date_to");

        RuleFor(command => command.From)
            .NotNull().WithMessage(PhpMessages.Required("from"))
            .GreaterThanOrEqualTo(0).WithMessage(PhpMessages.MinNumber("from", 0))
            .OverridePropertyName("from");
        RuleFor(command => command.To)
            .NotNull().WithMessage(PhpMessages.Required("to"))
            .OverridePropertyName("to");
        RuleFor(command => command.To)
            .Must((command, to) => to >= command.From + 1)
            .When(command => command.From is not null && command.To is not null)
            .WithMessage(command => PhpMessages.MinNumber("to", command.From!.Value + 1))
            .OverridePropertyName("to");
    }

    /// <summary>Lee una fecha <c>Y-m-d</c>; nulo si no llega o no tiene ese formato.</summary>
    public static DateOnly? ParseDate(string? value) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;

    private void RequiredForInvoice(Func<UpsertResolutionCommand, string?> field, string name) =>
        RuleFor(command => field(command))
            .NotEmpty()
            .When(command => command.TypeDocumentId == InvoiceType)
            .WithMessage(PhpMessages.RequiredIf(name, "type_document_id", "1"))
            .OverridePropertyName(name);

    private void Date(Func<UpsertResolutionCommand, string?> field, string name) =>
        RuleFor(command => field(command))
            .Must(value => ParseDate(value) is not null)
            .When(command => !string.IsNullOrEmpty(field(command)))
            .WithMessage(PhpMessages.DateFormat(name))
            .OverridePropertyName(name);
}
