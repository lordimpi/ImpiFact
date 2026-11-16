using System.Globalization;

namespace ImpiFact.Domain.Documents;

// Reglas de numeración de documentos (Modelo de datos detallado, sección 5; php-rules, sección 3, regla 3).

/// <summary>
/// Claves de consecutivo (<c>numbering_sequences.scope_key</c>): cada clave es un contador independiente dentro
/// de la empresa.
/// </summary>
public static class NumberingScope
{
    /// <summary>Consecutivo de una resolución de numeración: <c>R:{resolution_id}</c>.</summary>
    public static string ForResolution(Guid resolutionId) => "R:" + resolutionId.ToString("D", CultureInfo.InvariantCulture);

    /// <summary>
    /// Consecutivo de un tipo de documento sin resolución (por ejemplo, nómina):
    /// <c>T:{document_type_id}:{ambiente}:{prefijo}:{año}</c>. Sin prefijo, ese tramo queda vacío.
    /// </summary>
    public static string ForDocumentType(int documentTypeId, int typeEnvironmentId, string? prefix, int year) =>
        string.Create(CultureInfo.InvariantCulture, $"T:{documentTypeId}:{typeEnvironmentId}:{prefix}:{year}");
}

/// <summary>
/// Rango y vigencia de una resolución de numeración de la DIAN, para saber si un número se puede usar en una
/// fecha de emisión. Los límites del rango y los días de la vigencia son inclusivos, como la regla
/// <c>between</c> del PHP. Las resoluciones sin fechas de vigencia (por ejemplo, las de notas) solo revisan el rango.
/// </summary>
public sealed record NumberingRange(long From, long To, DateOnly? ValidFrom, DateOnly? ValidTo, bool IsActive)
{
    /// <summary>Revisa, en este orden, que la resolución esté activa, que la fecha esté en la vigencia y el número en el rango.</summary>
    public NumberingCheck Check(long number, DateOnly issueDate)
    {
        if (!IsActive)
        {
            return NumberingCheck.Inactive;
        }

        if (ValidFrom is { } validFrom && issueDate < validFrom)
        {
            return NumberingCheck.NotYetValid;
        }

        if (ValidTo is { } validTo && issueDate > validTo)
        {
            return NumberingCheck.Expired;
        }

        return number >= From && number <= To ? NumberingCheck.Valid : NumberingCheck.OutOfRange;
    }
}

/// <summary>Resultado de <see cref="NumberingRange.Check"/>.</summary>
public enum NumberingCheck
{
    Valid,

    /// <summary>La resolución está inactiva.</summary>
    Inactive,

    /// <summary>El número está fuera del rango <c>from..to</c>.</summary>
    OutOfRange,

    /// <summary>La fecha de emisión es anterior al inicio de la vigencia.</summary>
    NotYetValid,

    /// <summary>La fecha de emisión es posterior al fin de la vigencia.</summary>
    Expired,
}
