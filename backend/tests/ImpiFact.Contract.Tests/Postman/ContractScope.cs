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
    /// Requests outside those folders that phase 7a already covers, by their name in the collection: folder 07 variants
    /// implemented in T2 (odd/tasks/fase-7a-community.md).
    /// </summary>
    public static readonly IReadOnlySet<string> PhaseSevenRequests = new HashSet<string>(StringComparer.Ordinal)
    {
        "Factura AIU (administracion, imprevistos y utilidad)",
        "Factura de mandato",
        "Factura de transporte de carga",
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
        if (!PhaseFourFolders.Contains(request.TopFolder) && !PhaseSevenRequests.Contains(request.Name))
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
