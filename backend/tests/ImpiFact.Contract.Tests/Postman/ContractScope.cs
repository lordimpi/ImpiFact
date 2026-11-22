using System.Globalization;
using System.Text;

namespace ImpiFact.Contract.Tests.Postman;

/// <summary>
/// Qué solicitudes de la colección se ejecutan en la fase 4 (Arquitectura detallada, sección 19.5): «solo se ejecutan
/// las peticiones de la primera entrega (carpetas 01 a 06, 09 y 10, sin documento soporte, contingencia ni lotes)» y
/// «nunca se ejecutan <c>payroll</c>, <c>payroll-adjust-note</c>, <c>eqdoc</c>, <c>send-email-employee</c> ni las
/// carpetas 11 a 14». Las exclusiones se buscan en la ruta y en los nombres de la solicitud y de sus carpetas, porque
/// también cubren su configuración (software y resoluciones de nómina, documento soporte, equivalentes y contingencia).
/// </summary>
public static class ContractScope
{
    public static readonly IReadOnlySet<string> PhaseFourFolders = new HashSet<string>(StringComparer.Ordinal)
    {
        "01", "02", "03", "04", "05", "06", "09", "10",
    };

    /// <summary>
    /// Requests that phase 7a already covers, by their name in the collection (odd/tasks/fase-7a-community.md): folder 07
    /// variants implemented in T2 and T3, the support-document configuration (folder 01D) and requests (folder 02)
    /// implemented in T4, and the contingency invoices of T5: the type 3 resolution (folder 01), the type 03 invoice (folder
    /// 07) and the «Contingencia tipo 4» folder of folder 02 (its resolution, the type 04 invoice and send_pendings). These
    /// names skip the exclusions below, which still hold for every other request.
    /// </summary>
    public static readonly IReadOnlySet<string> PhaseSevenRequests = new HashSet<string>(StringComparer.Ordinal)
    {
        "Factura AIU (administracion, imprevistos y utilidad)",
        "Factura de mandato",
        "Factura de transporte de carga",
        "Factura de exportacion",
        "Paso 1 - Registrar el software de documento soporte",
        "Paso 2 - Crear la resolucion de documento soporte",
        "Paso 3 - Crear la resolucion de nota de ajuste de documento soporte",
        "Documento soporte sencillo",
        "Documento soporte con descuento",
        "Documento soporte con descuento (variante 2)",
        "Documento soporte a proveedor extranjero (no residente)",
        "Nota de ajuste de documento soporte",
        "Documento soporte con impuesto",
        "Documento soporte exento con retencion del 11%",
        "Crear la resolucion de factura de contingencia",
        "Factura de contingencia (tipo 03)",
        "Enviar una factura de contingencia tipo 4",
        "Transmitir las facturas de contingencia pendientes",
        "Crear la resolucion de contingencia tipo 4",
        "Enviar un evento desde el AttachedDocument",
        "Enviar un evento indicando el CUFE",
        "Cargar un documento recibido por CUFE",
    };

    /// <summary>Palabra buscada (sin tildes y en minúsculas) y motivo de la exclusión.</summary>
    private static readonly (string Term, string Reason)[] Exclusions =
    [
        ("payroll", "nómina (Enterprise, fase 8)"),
        ("nomina", "nómina (Enterprise, fase 8)"),
        ("eqdoc", "documento equivalente (Enterprise, fase 8)"),
        ("equivalente", "documento equivalente (Enterprise, fase 8)"),
        ("send-email-employee", "correo de nómina (Enterprise, fase 8)"),
        ("support-document", "documento soporte (fase 7)"),
        ("supportdocument", "documento soporte (fase 7)"),
        ("sd-credit-note", "documento soporte (fase 7)"),
        ("documento soporte", "documento soporte (fase 7)"),
        ("contingency", "contingencia (fase 7)"),
        ("contingencia", "contingencia (fase 7)"),
        ("batch", "lotes (fase 7)"),
        ("lote", "lotes (fase 7)"),
    ];

    /// <summary>Nulo si la solicitud se ejecuta en la fase 4; si no, el motivo por el que queda fuera.</summary>
    public static string? OutOfScopeReason(PostmanRequest request)
    {
        if (PhaseSevenRequests.Contains(request.Name))
        {
            return null;
        }

        if (!PhaseFourFolders.Contains(request.TopFolder))
        {
            return $"carpeta {request.TopFolder} fuera de la fase 4";
        }

        var text = Normalize(string.Join(' ', [.. request.Folders.Skip(1), request.Name, request.ApiPath]));
        foreach (var (term, reason) in Exclusions)
        {
            if (text.Contains(term, StringComparison.Ordinal))
            {
                return reason;
            }
        }

        return null;
    }

    private static string Normalize(string text)
    {
        var decomposed = text.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }
}
