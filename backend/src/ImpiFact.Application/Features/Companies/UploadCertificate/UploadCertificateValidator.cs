using FluentValidation;

namespace ImpiFact.Application.Features.Companies.UploadCertificate;

/// <summary>Certificado y contraseña obligatorios; el certificado debe venir en base64 válido.</summary>
public sealed class UploadCertificateValidator : AbstractValidator<UploadCertificateCommand>
{
    public const string InvalidBase64 = "La codificación base64 del certificado no es válida.";

    public UploadCertificateValidator()
    {
        RuleFor(command => command.Certificate)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage(PhpMessages.Required("certificate"))
            .Must(certificate => TryDecode(certificate) is not null).WithMessage(InvalidBase64)
            .OverridePropertyName("certificate");

        RuleFor(command => command.Password)
            .NotEmpty().WithMessage(PhpMessages.Required("password"))
            .OverridePropertyName("password");
    }

    /// <summary>Bytes del certificado, o nulo si el texto no es base64.</summary>
    public static byte[]? TryDecode(string? base64)
    {
        if (string.IsNullOrEmpty(base64))
        {
            return null;
        }

        var buffer = new byte[base64.Length];
        return Convert.TryFromBase64String(base64, buffer, out var written) && written > 0 ? buffer[..written] : null;
    }
}
