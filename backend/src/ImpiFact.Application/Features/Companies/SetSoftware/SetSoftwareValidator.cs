using FluentValidation;
using FluentValidation.Results;

namespace ImpiFact.Application.Features.Companies.SetSoftware;

/// <summary>
/// Reglas de <c>ConfigurationSoftwareRequest.php</c>: el PIN es obligatorio cuando llega el identificador y es
/// numérico de 5 dígitos; la URL debe ser absoluta. Los largos máximos salen de las columnas del modelo.
/// </summary>
public sealed class SetSoftwareValidator : AbstractValidator<SetSoftwareCommand>
{
    private const int PinDigits = 5;

    public SetSoftwareValidator()
    {
        RuleFor(command => command).Custom((command, context) =>
        {
            foreach (var entry in command.Entries())
            {
                Check(context, entry);
            }
        });
        RuleFor(command => command.TestSetId)
            .MaximumLength(64).WithMessage(PhpMessages.MaxLength("test_set_id", 64))
            .OverridePropertyName("test_set_id");
    }

    private static void Check(ValidationContext<SetSoftwareCommand> context, SoftwareEntry entry)
    {
        if (entry.Identifier is { Length: > 64 })
        {
            context.AddFailure(new ValidationFailure(entry.IdField, PhpMessages.MaxLength(entry.IdField, 64)));
        }

        if (entry.Identifier is not null && entry.Pin is null)
        {
            context.AddFailure(new ValidationFailure(entry.PinField, PhpMessages.RequiredWith(entry.PinField, entry.IdField)));
        }

        if (entry.Pin is not null && !(entry.Pin.Length == PinDigits && entry.Pin.All(char.IsAsciiDigit)))
        {
            context.AddFailure(new ValidationFailure(entry.PinField, PhpMessages.Digits(entry.PinField, PinDigits)));
        }

        if (entry.Url is not null && !IsHttpUrl(entry.Url))
        {
            context.AddFailure(new ValidationFailure(entry.UrlField, PhpMessages.Url(entry.UrlField)));
        }
    }

    private static bool IsHttpUrl(string url) =>
        url.Length <= 255
        && Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
}
