using System.Text.Json.Serialization;
using FluentValidation;
using FluentValidation.Results;
using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Documents;
using ImpiFact.Application.Abstractions.Handlers;
using ImpiFact.Application.Abstractions.Results;
using ImpiFact.Application.Features.Documents.Issue;
using ImpiFact.Domain.Common;
using ImpiFact.Domain.Documents;

namespace ImpiFact.Application.Features.Documents.Queries.CurrentNumber;

/// <summary>
/// Próximo número de una resolución (contrato del PHP <c>invoice/current_number/{type}/{prefix?}</c>). Sin prefijo
/// (o con el texto «null», como lo manda el PHP) se usa la primera resolución activa del tipo.
/// </summary>
public sealed record GetCurrentNumberQuery(int TypeDocumentId, string? Prefix);

/// <summary>
/// Respuesta con los campos del PHP (<c>number</c>, <c>success</c>, <c>prefix</c>) y un aviso que el PHP no daba:
/// <c>range_exceeded</c> es verdadero si el próximo número ya está fuera del rango de la resolución.
/// </summary>
public sealed record CurrentNumberResponse(
    [property: JsonPropertyName("number")] long Number,
    [property: JsonPropertyName("prefix")] string? Prefix,
    [property: JsonPropertyName("range_to")] long RangeTo,
    [property: JsonPropertyName("range_exceeded")] bool RangeExceeded)
{
    [JsonPropertyName("success")]
    public bool Success => true;
}

/// <summary>
/// Cuerpo de <c>POST /api/ubl2.1/next-consecutive</c> del PHP (<c>NextConsecutiveRequest</c>): el tipo de documento y
/// el prefijo, los dos obligatorios. Llegan como <see cref="LegacyScalar"/> porque el contrato admite número o texto.
/// </summary>
public sealed record GetNextConsecutiveQuery
{
    [JsonPropertyName("type_document_id")]
    public LegacyScalar? TypeDocumentId { get; init; }

    [JsonPropertyName("prefix")]
    public LegacyScalar? Prefix { get; init; }
}

/// <summary>Respuesta de <c>next-consecutive</c> con los campos del PHP: <c>success</c>, <c>type_document_id</c>, <c>prefix</c> y <c>number</c>.</summary>
public sealed record NextConsecutiveResponse(
    [property: JsonPropertyName("type_document_id")] int TypeDocumentId,
    [property: JsonPropertyName("prefix")] string? Prefix,
    [property: JsonPropertyName("number")] long Number)
{
    [JsonPropertyName("success")]
    public bool Success => true;
}

/// <summary>
/// Reglas de <c>NextConsecutiveRequest.php</c> con los mensajes de Laravel: <c>type_document_id</c> obligatorio y entre
/// los tipos 1 a 26, <c>prefix</c> obligatorio.
/// </summary>
internal sealed class GetNextConsecutiveQueryValidator : AbstractValidator<GetNextConsecutiveQuery>
{
    private const int MaxDocumentTypeId = 26;

    public GetNextConsecutiveQueryValidator() =>
        RuleFor(query => query).Custom((query, context) =>
        {
            void Fail(string field, string message) => context.AddFailure(new ValidationFailure(field, message));

            if (LegacyScalar.IsMissing(query.TypeDocumentId))
            {
                Fail("type_document_id", IssueMessages.Required("type_document_id"));
            }
            else if (!query.TypeDocumentId!.Value.TryInteger(out var type) || type is < 1 or > MaxDocumentTypeId)
            {
                Fail("type_document_id", IssueMessages.Invalid("type_document_id"));
            }

            if (query.Prefix is not { Text: { Length: > 0 } prefix } || string.IsNullOrWhiteSpace(prefix))
            {
                Fail("prefix", IssueMessages.Required("prefix"));
            }
        });
}

/// <summary>
/// La regla del próximo número, compartida por <c>current_number</c> y <c>next-consecutive</c> (php-rules 5.4.3).
/// </summary>
/// <remarks>
/// El PHP calculaba el máximo número emitido más uno (contando solo documentos aceptados, salvo que se pidiera lo
/// contrario). En ImpiFact el número sale del consecutivo de la resolución, que nunca reutiliza números: se lee sin
/// reservarlo, así que dos consultas seguidas responden el mismo número.
/// </remarks>
internal static class NextNumberRule
{
    internal static readonly Error ResolutionNotConfigured =
        new("resolution.not_configured", "La resolución no está configurada.", ErrorType.NotFound);

    // En esta primera entrega solo la factura y sus notas tienen consecutivo (alcance 1A).
    private static readonly int[] SupportedTypes = [1, 4, 5];

    public static async Task<Result<CurrentNumberResponse>> FindAsync(
        IIssuerContextReads issuers, IDocumentReads reads, Guid? companyId, int typeDocumentId, string? prefix, CancellationToken ct)
    {
        if (companyId is not { } company || !SupportedTypes.Contains(typeDocumentId))
        {
            return ResolutionNotConfigured;
        }

        var issuer = await issuers.GetAsync(company, typeDocumentId, DocumentFamilies.Of(typeDocumentId), ct);
        var wanted = prefix is null or "null" ? null : prefix;
        var resolution = issuer?.Resolutions
            .Where(candidate => candidate.IsActive)
            .FirstOrDefault(candidate => wanted is null || string.Equals(candidate.Prefix, wanted, StringComparison.Ordinal));
        if (resolution is null)
        {
            return ResolutionNotConfigured;
        }

        var next = await reads.PeekNextNumberAsync(company, NumberingScope.ForResolution(resolution.Id), ct) ?? resolution.From;
        next = Math.Max(next, resolution.From);
        return new CurrentNumberResponse(next, resolution.Prefix, resolution.To, next > resolution.To);
    }
}

/// <remarks>
/// El tercer parámetro del PHP (<c>ignore_state_document_id</c>) se acepta pero no cambia el resultado (ver
/// <see cref="NextNumberRule"/>).
/// </remarks>
internal sealed class GetCurrentNumberHandler(IIssuerContextReads issuers, IDocumentReads reads, ITenantContext tenant)
    : IHandler<GetCurrentNumberQuery, CurrentNumberResponse>
{
    public Task<Result<CurrentNumberResponse>> HandleAsync(GetCurrentNumberQuery request, CancellationToken ct) =>
        NextNumberRule.FindAsync(issuers, reads, tenant.CompanyId, request.TypeDocumentId, request.Prefix, ct);
}

/// <summary>
/// <c>POST /api/ubl2.1/next-consecutive</c>: el mismo número de <c>current_number</c> para el tipo y el prefijo, con la
/// forma de respuesta del PHP. Solo consulta: no reserva el número.
/// </summary>
internal sealed class GetNextConsecutiveHandler(IIssuerContextReads issuers, IDocumentReads reads, ITenantContext tenant)
    : IHandler<GetNextConsecutiveQuery, NextConsecutiveResponse>
{
    public async Task<Result<NextConsecutiveResponse>> HandleAsync(GetNextConsecutiveQuery request, CancellationToken ct)
    {
        var type = (int)request.TypeDocumentId!.Value.Integer;
        var found = await NextNumberRule.FindAsync(issuers, reads, tenant.CompanyId, type, request.Prefix!.Value.Text, ct);
        if (!found.IsSuccess)
        {
            return found.Error;
        }

        return new NextConsecutiveResponse(type, found.Value.Prefix, found.Value.Number);
    }
}
