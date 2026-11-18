using ImpiFact.Application.Abstractions.Identity;

namespace ImpiFact.Infrastructure.Identity;

/// <summary>
/// Tokens para restablecer la contraseña con las mismas piezas que los tokens de renovación (<see cref="CredentialHash"/>):
/// 32 bytes aleatorios en Base64 URL y su resumen SHA-256.
/// </summary>
internal sealed class PasswordResetTokenFactory : IPasswordResetTokenFactory
{
    public IssuedPasswordResetToken Create()
    {
        var value = CredentialHash.NewOpaqueValue();
        return new IssuedPasswordResetToken(value, Hash(value));
    }

    public string Hash(string token) => CredentialHash.Sha256Hex(token);
}
