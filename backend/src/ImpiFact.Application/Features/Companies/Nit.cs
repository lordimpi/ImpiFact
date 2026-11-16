namespace ImpiFact.Application.Features.Companies;

/// <summary>
/// NIT (Número de Identificación Tributaria) colombiano. El dígito de verificación (DV) se calcula con el
/// algoritmo de la DIAN, igual que <c>validarDigVerifDIAN</c> del PHP: cada dígito, desde el último, se multiplica
/// por un primo de la serie oficial; el DV es 11 menos el residuo de la suma entre 11, salvo cuando el residuo es 0 o 1.
/// </summary>
public static class Nit
{
    private static readonly int[] Weights = [3, 7, 13, 17, 19, 23, 29, 37, 41, 43, 47, 53, 59, 67, 71];

    /// <summary>Máximo de dígitos del NIT que acepta el PHP (<c>digits_between:1,15</c>).</summary>
    public const int MaxDigits = 15;

    public static bool IsDigits(string? value) => !string.IsNullOrEmpty(value) && value.All(char.IsAsciiDigit);

    /// <summary>Dígito de verificación del NIT; el NIT debe tener solo dígitos y a lo sumo 15.</summary>
    public static int CheckDigit(string nit)
    {
        if (!IsDigits(nit) || nit.Length > MaxDigits)
        {
            throw new ArgumentException("The NIT must have between 1 and 15 digits.", nameof(nit));
        }

        var sum = 0;
        for (var position = 0; position < nit.Length; position++)
        {
            sum += (nit[nit.Length - 1 - position] - '0') * Weights[position];
        }

        var remainder = sum % 11;
        return remainder is 0 or 1 ? remainder : 11 - remainder;
    }
}
