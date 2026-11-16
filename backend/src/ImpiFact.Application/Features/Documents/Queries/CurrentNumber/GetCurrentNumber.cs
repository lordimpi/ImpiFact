using System.Text.Json.Serialization;
using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Documents;
using ImpiFact.Application.Abstractions.Handlers;
using ImpiFact.Application.Abstractions.Results;
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

/// <remarks>
/// El PHP calculaba el máximo número emitido más uno (contando solo documentos aceptados, salvo que se pidiera lo
/// contrario). En ImpiFact el número sale del consecutivo de la resolución, que nunca reutiliza números; por eso el
/// tercer parámetro del PHP (<c>ignore_state_document_id</c>) se acepta pero no cambia el resultado.
/// </remarks>
internal sealed class GetCurrentNumberHandler(IIssuerContextReads issuers, IDocumentReads reads, ITenantContext tenant)
    : IHandler<GetCurrentNumberQuery, CurrentNumberResponse>
{
    internal static readonly Error ResolutionNotConfigured =
        new("resolution.not_configured", "La resolución no está configurada.", ErrorType.NotFound);

    // En esta primera entrega solo la factura y sus notas tienen consecutivo (alcance 1A).
    private static readonly int[] SupportedTypes = [1, 4, 5];

    public async Task<Result<CurrentNumberResponse>> HandleAsync(GetCurrentNumberQuery request, CancellationToken ct)
    {
        if (tenant.CompanyId is not { } companyId || !SupportedTypes.Contains(request.TypeDocumentId))
        {
            return ResolutionNotConfigured;
        }

        var issuer = await issuers.GetAsync(companyId, request.TypeDocumentId, DocumentKind.Invoice, ct);
        var prefix = request.Prefix is null or "null" ? null : request.Prefix;
        var resolution = issuer?.Resolutions
            .Where(candidate => candidate.IsActive)
            .FirstOrDefault(candidate => prefix is null || string.Equals(candidate.Prefix, prefix, StringComparison.Ordinal));
        if (resolution is null)
        {
            return ResolutionNotConfigured;
        }

        var next = await reads.PeekNextNumberAsync(companyId, NumberingScope.ForResolution(resolution.Id), ct) ?? resolution.From;
        next = Math.Max(next, resolution.From);
        return new CurrentNumberResponse(next, resolution.Prefix, resolution.To, next > resolution.To);
    }
}
