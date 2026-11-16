using FluentValidation;
using ImpiFact.Application.Abstractions.Identity;
using ImpiFact.Application.Features.Identity.ApiKeys;
using ImpiFact.Application.Features.Identity.Sessions;
using ImpiFact.Application.Features.Identity.Users;

namespace ImpiFact.Application.Tests.Features.Identity;

/// <summary>Validadores de Identity: campos con el nombre JSON de la solicitud y mensajes en español.</summary>
[TestFixture]
public sealed class IdentityValidatorTests
{
    [Test]
    public void Login_requires_email_and_password()
    {
        var errors = Errors(new LoginValidator(), new LoginCommand("", "", null, null, null));

        Assert.That(errors, Is.EquivalentTo(new[] { ("email", "El correo es obligatorio."), ("password", "La contraseña es obligatoria.") }));
    }

    [Test]
    public void Refresh_requires_the_token()
    {
        var errors = Errors(new RefreshTokenValidator(), new RefreshTokenCommand("", null, null, null));

        Assert.That(errors, Is.EqualTo(new[] { ("refresh_token", "El token de renovación es obligatorio.") }));
    }

    [Test]
    public void Create_api_key_checks_name_scopes_and_a_future_expiry()
    {
        var clock = new FakeClock();
        var errors = Errors(new CreateApiKeyValidator(clock), new CreateApiKeyCommand("", ["documents:delete"], clock.UtcNow));
        var empty = Errors(new CreateApiKeyValidator(clock), new CreateApiKeyCommand("ERP", [], null));
        var valid = Errors(new CreateApiKeyValidator(clock), new CreateApiKeyCommand("ERP", [ApiScopes.DocumentsRead], clock.UtcNow.AddDays(1)));

        Assert.Multiple(() =>
        {
            Assert.That(errors, Is.EquivalentTo(new[]
            {
                ("name", "El nombre es obligatorio."),
                ("scopes[0]", "El alcance «documents:delete» no existe."),
                ("expires_at", "La fecha de vencimiento debe ser futura."),
            }));
            Assert.That(empty, Is.EqualTo(new[] { ("scopes", "Indique al menos un alcance.") }));
            Assert.That(valid, Is.Empty);
        });
    }

    [Test]
    public void Create_user_checks_email_password_length_name_and_role()
    {
        var errors = Errors(new CreateUserValidator(), new CreateUserCommand("no-es-correo", "corta", "", null, "jefe"));

        Assert.That(errors, Is.EquivalentTo(new[]
        {
            ("email", "El correo no es válido."),
            ("password", "La contraseña debe tener al menos 10 caracteres."),
            ("name", "El nombre es obligatorio."),
            ("role", "El rol «jefe» no existe."),
        }));
    }

    [Test]
    public void Revoke_api_key_requires_an_id() =>
        Assert.That(Errors(new RevokeApiKeyValidator(), new RevokeApiKeyCommand(Guid.Empty)),
            Is.EqualTo(new[] { ("id", "El id de la API key es obligatorio.") }));

    private static List<(string Field, string Message)> Errors<T>(IValidator<T> validator, T request) =>
        [.. validator.Validate(request).Errors.Select(error => (error.PropertyName, error.ErrorMessage))];
}
