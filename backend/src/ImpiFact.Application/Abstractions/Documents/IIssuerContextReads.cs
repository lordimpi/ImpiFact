using ImpiFact.Domain.Common;

namespace ImpiFact.Application.Abstractions.Documents;

/// <summary>
/// Lo que el módulo Documents necesita saber de la empresa emisora para emitir un documento: sus datos, su ambiente
/// DIAN (Dirección de Impuestos y Aduanas Nacionales), su software, su certificado activo y sus resoluciones de
/// numeración. Es un puerto de solo lectura porque Documents no puede usar las entidades del módulo Company (regla de
/// módulos, Arquitectura detallada, sección 1); lo implementa Infrastructure con consultas sin rastreo que ponen la
/// empresa en el WHERE.
/// </summary>
public interface IIssuerContextReads
{
    /// <summary>
    /// Contexto de emisión de la empresa para un tipo de documento: el ambiente actual de la familia
    /// <paramref name="kind"/>, el software de esa familia en ese ambiente y las resoluciones de ese tipo y ambiente.
    /// </summary>
    /// <returns>Nulo si la empresa no existe.</returns>
    Task<IssuerContext?> GetAsync(Guid companyId, int documentTypeId, DocumentKind kind, CancellationToken ct);
}

/// <summary>Contexto de emisión de una empresa.</summary>
/// <param name="LiabilityIds">Responsabilidades fiscales (<c>type_liabilities</c>) de la empresa, de menor a mayor id.</param>
/// <param name="TypeEnvironmentId">Ambiente DIAN actual de la familia: 1 producción, 2 habilitación (pruebas).</param>
/// <param name="Software">Software de la familia en ese ambiente; nulo si no está configurado.</param>
/// <param name="Certificate">Certificado de firma activo; nulo si la empresa no tiene uno.</param>
/// <param name="Resolutions">Resoluciones del tipo de documento en ese ambiente, activas o no.</param>
public sealed record IssuerContext(
    IssuerCompany Company,
    IReadOnlyList<int> LiabilityIds,
    int TypeEnvironmentId,
    IssuerSoftware? Software,
    IssuerCertificate? Certificate,
    IReadOnlyList<IssuerResolution> Resolutions);

/// <summary>Datos de la empresa emisora que van en el XML (como proveedor) y en el documento.</summary>
/// <param name="IdentificationNumber">NIT (Número de Identificación Tributaria) sin dígito de verificación.</param>
/// <param name="Dv">Dígito de verificación del NIT.</param>
/// <param name="Name">Razón social.</param>
public sealed record IssuerCompany(
    Guid Id,
    string IdentificationNumber,
    string Dv,
    string Name,
    string Email,
    string Phone,
    string Address,
    string? MerchantRegistration,
    int LanguageId,
    int CountryId,
    int MunicipalityId,
    int TaxId,
    int TypeCurrencyId,
    int TypeDocumentIdentificationId,
    int TypeOrganizationId,
    int TypeRegimeId,
    bool IsActive);

/// <summary>Software registrado ante la DIAN. El PIN llega cifrado; solo el caso de uso lo descifra para firmar.</summary>
/// <param name="TestSetId">Identificador del set de pruebas de habilitación, si lo tiene.</param>
public sealed record IssuerSoftware(string Identifier, string PinCipher, string? TestSetId);

/// <summary>Certificado de firma activo: la referencia al secreto (nunca el archivo) y el fin de su vigencia.</summary>
public sealed record IssuerCertificate(string SecretRef, DateTimeOffset NotAfter);

/// <summary>Resolución de numeración de la DIAN.</summary>
/// <param name="ResolutionNumber">Número de la resolución; vacío o nulo en las de notas.</param>
/// <param name="TechnicalKey">Clave técnica (entra en el CUFE, código único de la factura electrónica); nula en las de notas.</param>
/// <param name="ResolutionDate">Fecha de la resolución; nula en las de notas.</param>
public sealed record IssuerResolution(
    Guid Id,
    string? Prefix,
    string? ResolutionNumber,
    DateOnly? ResolutionDate,
    string? TechnicalKey,
    long From,
    long To,
    DateOnly? ValidFrom,
    DateOnly? ValidTo,
    bool IsActive);
