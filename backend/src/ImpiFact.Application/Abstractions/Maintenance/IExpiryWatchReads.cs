namespace ImpiFact.Application.Abstractions.Maintenance;

/// <summary>
/// Lecturas de todas las empresas para los vigilantes diarios de vencimientos (certificados de firma y resoluciones
/// de numeración). Es un puerto porque Maintenance no puede usar las entidades de Companies ni de Documents; lo
/// implementa Infrastructure con consultas sin rastreo que cruzan empresas a propósito, porque las corre el sistema.
/// </summary>
public interface IExpiryWatchReads
{
    /// <summary>
    /// Certificados activos de cualquier empresa que todavía no vencen (<c>not_after</c> después de
    /// <paramref name="now"/>) y vencen a más tardar en <paramref name="until"/>.
    /// </summary>
    Task<IReadOnlyList<WatchedCertificate>> ListExpiringCertificatesAsync(DateTimeOffset now, DateTimeOffset until, CancellationToken ct);

    /// <summary>Resoluciones activas de cualquier empresa, con el próximo número de su consecutivo.</summary>
    Task<IReadOnlyList<WatchedResolution>> ListActiveResolutionsAsync(CancellationToken ct);
}

/// <summary>Certificado de firma activo de una empresa.</summary>
/// <param name="Name">Nombre del archivo con que se subió, para que el usuario lo reconozca.</param>
/// <param name="NotAfter">Fin de la vigencia del certificado.</param>
public sealed record WatchedCertificate(Guid CompanyId, Guid CertificateId, string Name, DateTimeOffset NotAfter);

/// <summary>Resolución de numeración activa de una empresa.</summary>
/// <param name="NextNumber">
/// Próximo número del consecutivo de la resolución; nulo si todavía no se emitió ningún documento con ella.
/// </param>
public sealed record WatchedResolution(
    Guid CompanyId,
    Guid ResolutionId,
    int DocumentTypeId,
    string? Prefix,
    string? ResolutionNumber,
    long RangeFrom,
    long RangeTo,
    long? NextNumber,
    DateOnly? ValidTo);
