using System.Globalization;
using FluentValidation;

namespace ImpiFact.Application.Features.Documents.Queries.Information;

// Validación de las rutas heredadas. El PHP pasaba las fechas tal cual al SQL y fallaba con un error 500 si no eran
// fechas; aquí se responde 422 con el campo que está mal. Los nombres de campo son los de la ruta (desde, hasta, page).

internal sealed class GetInformationQueryValidator : AbstractValidator<GetInformationQuery>
{
    public GetInformationQueryValidator()
    {
        RuleFor(query => query.From).Must(LegacyDates.IsAbsentOrDate).OverridePropertyName("desde").WithMessage(QueryMessages.Date("desde"));
        RuleFor(query => query.To).Must(LegacyDates.IsAbsentOrDate).OverridePropertyName("hasta").WithMessage(QueryMessages.Date("hasta"));
    }
}

internal sealed class GetTotalDocumentsQueryValidator : AbstractValidator<GetTotalDocumentsQuery>
{
    public GetTotalDocumentsQueryValidator()
    {
        RuleFor(query => query.From).Must(LegacyDates.IsAbsentOrDate).OverridePropertyName("desde").WithMessage(QueryMessages.Date("desde"));
        RuleFor(query => query.To).Must(LegacyDates.IsAbsentOrDate).OverridePropertyName("hasta").WithMessage(QueryMessages.Date("hasta"));
    }
}

internal sealed class GetInformationPageQueryValidator : AbstractValidator<GetInformationPageQuery>
{
    public GetInformationPageQueryValidator()
    {
        RuleFor(query => query.Page)
            .Must(page => int.TryParse(page, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _))
            .OverridePropertyName("page")
            .WithMessage(QueryMessages.Integer("page"));
    }
}

internal static class LegacyDates
{
    /// <summary>Una fecha ausente o vacía es válida: toma el valor por defecto del PHP.</summary>
    public static bool IsAbsentOrDate(string? value) => string.IsNullOrEmpty(value) || ColombiaDates.TryParse(value, out _);
}
