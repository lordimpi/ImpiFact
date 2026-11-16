using ImpiFact.Domain.Common;
using ImpiFact.Domain.Companies;

namespace ImpiFact.Application.Features.Companies;

/// <summary>
/// Ambientes de la DIAN (catálogo <c>type_environments</c>): 1 producción y 2 habilitación (pruebas). Cada familia
/// de documentos tiene el suyo (<c>company_environments</c>).
/// </summary>
internal static class Environments
{
    public const int Production = 1;

    public const int Habilitation = 2;

    public static readonly DocumentKind[] AllKinds =
    [
        DocumentKind.Invoice, DocumentKind.Payroll, DocumentKind.EquivalentDocument, DocumentKind.SupportDocument, DocumentKind.Events,
    ];

    public static bool IsValid(int typeEnvironmentId) => typeEnvironmentId is Production or Habilitation;

    /// <summary>
    /// Familia de un tipo de documento, como <c>getResolutionEnvironmentId</c> del PHP: 8 y 14 eventos, 9 y 10 nómina,
    /// 11 y 13 documento soporte, 15 a 26 documento equivalente y el resto factura y notas.
    /// </summary>
    public static DocumentKind KindOf(int typeDocumentId) => typeDocumentId switch
    {
        8 or 14 => DocumentKind.Events,
        9 or 10 => DocumentKind.Payroll,
        11 or 13 => DocumentKind.SupportDocument,
        >= 15 and <= 26 => DocumentKind.EquivalentDocument,
        _ => DocumentKind.Invoice,
    };

    /// <summary>Ambiente actual de una familia; habilitación si la empresa no tiene fila para ella (como el PHP).</summary>
    public static int CurrentFor(IEnumerable<CompanyEnvironment> environments, DocumentKind kind) =>
        environments.FirstOrDefault(environment => environment.DocumentKind == kind)?.TypeEnvironmentId ?? Habilitation;
}
