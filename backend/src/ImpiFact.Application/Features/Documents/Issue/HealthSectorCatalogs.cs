using ImpiFact.Dian.Ubl;

namespace ImpiFact.Application.Features.Documents.Issue;

/// <summary>
/// Reference tables of the health-sector invoice (php-rules 17). They are not DIAN tables of ApiDian Community (its only
/// health table is the identification types, which the XML no longer carries) but the Ministerio de Salud's: they live here,
/// versioned with their source, instead of in the database. The request id of each row is the number of its code
/// (<c>health_coverage_id</c> 16 is code <c>16</c>).
/// </summary>
/// <remarks>
/// Sources, read on 22-nov-2026: DIAN annex 1.8, table 13.1.5.1.1 (health operation types, in that order) and the Ministerio
/// de Salud's Documento Técnico 2 v001 (15-jul-2026, Resolución 948 de 2026), section 9; SISPRO reference tables
/// <c>modalidadPago</c> (updated 14-nov-2025; code 05 is disabled), <c>coberturaPlan</c> (25-aug-2026) and
/// <c>facturaSinContrato</c> (23-jul-2026), https://web.sispro.gov.co/WebPublico/Consultas/ConsultarDetalleReferenciaBasica.aspx;
/// identification types from ApiDian's <c>health_type_document_identifications.csv</c>.
/// </remarks>
internal static class HealthSectorCatalogs
{
    /// <summary>Health operation types (cbc:CustomizationID); id 5, SS-Recaudo, never carries the health extension.</summary>
    public static readonly IReadOnlyList<string> Operations =
        ["SS-CUFE", "SS-CUDE", "SS-POS", "SS-SNum", "SS-Recaudo", "SS-Reporte", "SS-SinAporte"];

    /// <summary>SS-Recaudo: an invoice to the patient for a copay or a moderating fee, without the extension (section 6.a).</summary>
    public const int CollectionOperationId = 5;

    /// <summary>SS-SinAporte: the extension is optional (section 8, «MODOS DE USO - 4»).</summary>
    public const int NoContributionOperationId = 7;

    /// <summary>SISPRO <c>modalidadPago</c>, the enabled rows (MODALIDAD_PAGO).</summary>
    public static readonly IReadOnlyList<string> PaymentMethods =
    [
        "Pago individual por caso / Conjunto integral de atenciones / Paquete / Canasta",
        "Pago global prospectivo",
        "Pago por capitacion",
        "Pago por evento",
    ];

    /// <summary>SISPRO <c>coberturaPlan</c> (COBERTURA_PLAN_BENEFICIOS).</summary>
    public static readonly IReadOnlyList<string> Coverages =
    [
        "Plan de beneficios en salud financiado con UPC",
        "Presupuesto maximo",
        "Prima EPS / EOC, no asegurados SOAT",
        "Cobertura Poliza SOAT",
        "Cobertura ARL",
        "Cobertura ADRES",
        "Cobertura Salud Publica",
        "Cobertura entidad territorial, recursos de oferta",
        "Urgencias poblacion migrante",
        "Plan complementario en salud",
        "Plan medicina prepagada",
        "Otras polizas en salud",
        "Cobertura Regimen Especial o Excepcion",
        "Cobertura Fondo Nacional de Salud de las Personas Privadas de la Libertad",
        "Particular",
        "Plan de beneficios en salud financiado con UPC Contributivo",
        "Plan de beneficios en salud financiado con UPC Subsidiado",
    ];

    /// <summary>SISPRO <c>facturaSinContrato</c> (FACTURA_SIN_CONTRATO).</summary>
    public static readonly IReadOnlyList<string> InvoicesWithoutContract =
    [
        "ATENCION DE URGENCIAS",
        "ATENCION A CARGO DE ADRES O DE ASEGURADORA SOAT, PLANES VOLUNTARIOS DE SALUD",
        "ATENCION EN SALUD POR FALLOS DE TUTELA/ORDENES JUDICIALES",
        "ATENCION EN SALUD POR PORTABILIDAD O EN LOS CASOS DE ASIGNACION MASIVA DE AFILIADOS - ARTICULO 2.5.3.4.7.9 DEL DECRETO 780/2016",
        "ATENCION EN SALUD EN CASOS EXCEPCIONALES POR COTIZACIONES O AUTORIZACIONES SIN CONTRATO ATENCIONES ADICIONALES EXCEPCIONALES",
        "GESTION RECUPERACION DE ORGANOS PARA TRASPLANTE EN CUMPLIMIENTO LEY ESTATUTARIA 1751 DE 2015",
        "ATENCION A PACIENTES PARTICULARES",
    ];

    /// <summary>
    /// Identification types of the patient (ApiDian <c>health_type_document_identifications</c>, 13 rows). The Documento
    /// Técnico 2 v001 moved the patient to the RIPS (section 6.b), so ImpiFact only checks the id.
    /// </summary>
    public const int IdentificationTypeCount = 13;

    /// <summary>The row of a table as the XML writes it: the two-digit code and its name.</summary>
    public static HealthCode Code(IReadOnlyList<string> table, int id) => new(id.ToString("00", System.Globalization.CultureInfo.InvariantCulture), table[id - 1]);

    public static bool Contains(IReadOnlyList<string> table, long id) => id >= 1 && id <= table.Count;
}
