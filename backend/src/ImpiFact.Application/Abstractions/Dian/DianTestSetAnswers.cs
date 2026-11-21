using System.Text.RegularExpressions;

namespace ImpiFact.Application.Abstractions.Dian;

/// <summary>
/// Respuestas de la DIAN (la autoridad tributaria de Colombia) al set de pruebas de habilitación que no dependen de un
/// documento. La comparten la lectura de una llamada (<c>DianAnswer</c>) y el estado del paso a producción, que la
/// calcula Infrastructure con las respuestas guardadas en <c>dian_submissions</c>.
/// </summary>
public static partial class DianTestSetAnswers
{
    /// <summary>
    /// Si la descripción de una respuesta de GetStatusZip dice que el set de pruebas ya está aceptado: «Set de prueba
    /// con identificador {TestSetId} se encuentra Aceptado» (la DIAN la manda con código <c>2</c> e <c>IsValid</c>
    /// falso, sin reglas). Después de aprobar el set, la DIAN ya no valida los documentos que se le envían: la
    /// respuesta es final y el documento queda rechazado con esa descripción, en vez de seguir «en proceso» hasta el
    /// plazo de las consultas. Se reconoce por la descripción y no por el código, porque el código <c>2</c> no dice
    /// por sí solo en qué estado está el set: con otra descripción (por ejemplo, «se encuentra en proceso», o «Batch en
    /// proceso de validación.» sin código) el set todavía no decide y se sigue consultando. La comparación no distingue
    /// mayúsculas, acepta «prueba» o «pruebas» y cualquier espacio entre las palabras. Los nulos no cuentan.
    /// </summary>
    public static bool IsAlreadyAccepted(string? description) =>
        description is not null && AcceptedPattern().IsMatch(description);

    /// <summary>
    /// Si la descripción de una respuesta de GetStatusZip dice que la DIAN todavía procesa: «Batch en proceso de
    /// validación.» o «Set de prueba con identificador {TestSetId} se encuentra en proceso». La DIAN las manda con
    /// <c>IsValid</c> falso, pero no son un rechazo: el set todavía no tiene resultado. La comparación no distingue
    /// mayúsculas. Los nulos no cuentan.
    /// </summary>
    public static bool IsInProcess(string? description) =>
        description is not null && InProcessPattern().IsMatch(description);

    [GeneratedRegex(@"\ben\s+proceso\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex InProcessPattern();

    [GeneratedRegex(@"\bSet\s+de\s+pruebas?\b.*\bse\s+encuentra\s+aceptad[oa]\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex AcceptedPattern();
}
