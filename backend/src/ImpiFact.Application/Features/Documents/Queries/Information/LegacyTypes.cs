namespace ImpiFact.Application.Features.Documents.Queries.Information;

/// <summary>
/// Tipos de documento y nombres que devuelven <c>information</c> y <c>total_documents</c>, en el orden y con los
/// textos del PHP (<c>ResumeController.php</c>), incluidas las palabras sin tilde.
/// </summary>
internal static class LegacyTypes
{
    public const string Found = "NIT Encontrado";

    /// <summary>Tipos de <c>information</c>: factura (1), nota crédito (4), nota débito (5) y documento equivalente POS (15).</summary>
    public static IReadOnlyList<(int Id, string Name)> Information { get; } =
    [
        (1, "Factura de Venta Nacional"),
        (4, "Nota Credito"),
        (5, "Nota Debito"),
        (15, "Documento Equivalente POS"),
    ];

    /// <summary>
    /// Tipos de <c>total_documents</c>: los de <c>information</c> más el documento soporte (11), su nota de ajuste
    /// (13) y las notas crédito (26) y débito (25) del documento POS (punto de venta). El PHP pone la nota crédito
    /// POS antes que la débito.
    /// </summary>
    public static IReadOnlyList<(int Id, string Name)> Totals { get; } =
    [
        (1, "Factura de Venta Nacional"),
        (4, "Nota Credito"),
        (5, "Nota Debito"),
        (11, "Documento Soporte a No Obligados a Facturar"),
        (13, "Notas de Ajuste al Documento Soporte a No Obligados a Facturar"),
        (15, "Documento Equivalente POS"),
        (26, "Nota Credito a Documento POS"),
        (25, "Nota Debito a Documento POS"),
    ];

    /// <summary>Documentos por tipo y página en <c>information/{nit}/page/{page}/page</c>.</summary>
    public const int PageSize = 100;
}
