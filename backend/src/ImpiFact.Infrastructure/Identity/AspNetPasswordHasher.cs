using System.Security.Cryptography;
using ImpiFact.Application.Abstractions.Identity;
using Microsoft.AspNetCore.Identity;

namespace ImpiFact.Infrastructure.Identity;

/// <summary>
/// Adaptador de <see cref="IPasswordHasher"/> con el <see cref="PasswordHasher{TUser}"/> de ASP.NET Core Identity
/// (sin sus almacenes; supuesto A4 de la fase 2): PBKDF2 (una función que repite el cálculo muchas veces para que
/// probar contraseñas sea caro) con HMAC-SHA512, sal aleatoria y el número de iteraciones dentro del resumen.
/// </summary>
internal sealed class AspNetPasswordHasher : IPasswordHasher
{
    private static readonly PasswordHasher<PasswordOwner> Hasher = new();

    /// <summary>
    /// Resumen de una contraseña aleatoria que nadie conoce. Cuando el usuario no existe se verifica contra él, así
    /// la respuesta tarda lo mismo que con un usuario real.
    /// </summary>
    private static readonly Lazy<string> DecoyHash =
        new(() => Hasher.HashPassword(PasswordOwner.Instance, Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))));

    public string Hash(string password) => Hasher.HashPassword(PasswordOwner.Instance, password);

    public PasswordCheck Verify(string? passwordHash, string password)
    {
        var result = Hasher.VerifyHashedPassword(PasswordOwner.Instance, passwordHash ?? DecoyHash.Value, password);
        return passwordHash is null
            ? PasswordCheck.Failed
            : result switch
            {
                PasswordVerificationResult.Success => PasswordCheck.Success,
                PasswordVerificationResult.SuccessRehashNeeded => PasswordCheck.SuccessRehashNeeded,
                _ => PasswordCheck.Failed,
            };
    }

    /// <summary>El resumidor pide un usuario, pero no lo usa: basta un objeto vacío.</summary>
    private sealed class PasswordOwner
    {
        public static readonly PasswordOwner Instance = new();
    }
}
