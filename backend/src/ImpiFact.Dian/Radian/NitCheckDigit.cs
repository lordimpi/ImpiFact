namespace ImpiFact.Dian.Radian;

/// <summary>
/// Check digit (DV) of a Colombian NIT with the DIAN algorithm, as ApiDian's <c>validarDigVerifDIAN</c>: each digit, from
/// the last one, is multiplied by a prime of the official series; the DV is 11 minus the remainder of the sum divided by
/// 11, except when the remainder is 0 or 1. The PHP recalculates the DV of the seller of a received invoice (php-rules
/// 16.7, item 2) instead of reading it from the XML.
/// </summary>
public static class NitCheckDigit
{
    private static readonly int[] Weights = [3, 7, 13, 17, 19, 23, 29, 37, 41, 43, 47, 53, 59, 67, 71];

    /// <summary>Check digit of <paramref name="nit"/>, which must have between 1 and 15 digits.</summary>
    public static int Compute(string nit)
    {
        ArgumentNullException.ThrowIfNull(nit);
        if (nit.Length is 0 || nit.Length > Weights.Length || !nit.All(char.IsAsciiDigit))
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
