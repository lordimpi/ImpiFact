namespace ImpiFact.Application.Abstractions.Companies;

/// <summary>
/// Consultas de solo lectura de la configuración de la empresa que muestra el panel: las resoluciones con su
/// consecutivo y lo que la DIAN (Dirección de Impuestos y Aduanas Nacionales) respondió al set de pruebas. Esos datos
/// viven en tablas del módulo Documents (<c>numbering_sequences</c>, <c>dian_submissions</c>), por eso el módulo
/// Company los pide por este puerto. Infrastructure lo implementa sin rastreo de cambios y con la empresa en el WHERE.
/// </summary>
public interface ICompanyConfigurationReads
{
    /// <summary>
    /// Todas las resoluciones de la empresa, activas e inactivas y de todos los ambientes, ordenadas por tipo de
    /// documento, primero las activas, luego por ambiente, prefijo e inicio del rango. Nunca trae la clave técnica.
    /// </summary>
    Task<IReadOnlyList<ResolutionRecord>> ListResolutionsAsync(Guid companyId, CancellationToken ct);

    /// <summary>
    /// Resultado del set de pruebas de habilitación según las respuestas de la DIAN guardadas (consultas
    /// GetStatusZip): verdadero si la DIAN validó al menos un documento enviado con el set o respondió que el set ya
    /// está aceptado (<see cref="Dian.DianTestSetAnswers.IsAlreadyAccepted"/>), falso si respondió y no validó
    /// ninguno, nulo si todavía no hay respuesta guardada.
    /// </summary>
    Task<bool?> FindTestSetResultAsync(Guid companyId, CancellationToken ct);
}

/// <summary>Una resolución de numeración tal como la lee el panel, sin la clave técnica.</summary>
/// <param name="DocumentTypeId">Tipo de documento (catálogo <c>type_documents</c>: 1 factura, 4 nota crédito, 5 nota débito…).</param>
/// <param name="TypeEnvironmentId">Ambiente de la DIAN: 1 producción, 2 habilitación.</param>
/// <param name="HasTechnicalKey">Verdadero si la resolución tiene clave técnica guardada.</param>
/// <param name="NextNumber">Próximo número del consecutivo; nulo si la resolución todavía no tiene consecutivo.</param>
public sealed record ResolutionRecord(
    Guid Id,
    int DocumentTypeId,
    int TypeEnvironmentId,
    string? Prefix,
    string? ResolutionNumber,
    DateOnly? ResolutionDate,
    bool HasTechnicalKey,
    long RangeFrom,
    long RangeTo,
    DateOnly? ValidFrom,
    DateOnly? ValidTo,
    bool IsActive,
    long? NextNumber);
