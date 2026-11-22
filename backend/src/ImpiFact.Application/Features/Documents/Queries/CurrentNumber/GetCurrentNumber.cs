using System.Text.Json.Serialization;
using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Documents;
using ImpiFact.Application.Abstractions.Handlers;
using ImpiFact.Application.Abstractions.Results;
using ImpiFact.Application.Features.Documents.Issue;
using ImpiFact.Domain.Common;
using ImpiFact.Domain.Documents;

namespace ImpiFact.Application.Features.Documents.Queries.CurrentNumber;

/// <summary>
/// Próximo número de una resolución (el <c>invoice/current_number/{type}/{prefix?}</c> del PHP). Sin prefijo (o con el
/// texto «null», como lo mandaba el PHP) se usa la primera resolución activa del tipo.
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
/// La regla del próximo número de <c>GET /api/v1/documents/current-number/{type}</c> (php-rules 5.4.3).
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

internal sealed class GetCurrentNumberHandler(IIssuerContextReads issuers, IDocumentReads reads, ITenantContext tenant)
    : IHandler<GetCurrentNumberQuery, CurrentNumberResponse>
{
    public Task<Result<CurrentNumberResponse>> HandleAsync(GetCurrentNumberQuery request, CancellationToken ct) =>
        NextNumberRule.FindAsync(issuers, reads, tenant.CompanyId, request.TypeDocumentId, request.Prefix, ct);
}
