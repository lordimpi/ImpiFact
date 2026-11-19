using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ImpiFact.Application.Abstractions.Handlers;
using ImpiFact.Application.Abstractions.Results;
using ImpiFact.Application.Features.Identity.PasswordReset;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace ImpiFact.Integration.Tests.Identity;

/// <summary>
/// Si el guardado de una solicitud de recuperación choca con otra simultánea, la ruta responde el mismo 202 de
/// siempre: un 409 solo puede salir para un correo registrado y revelaría que la cuenta existe.
/// </summary>
[TestFixture]
public sealed class PasswordResetConflictTests
{
    private const string Email = "conflicto@example.com";

    [Test]
    public async Task A_request_that_conflicts_while_saving_answers_the_usual_202_and_logs_no_email()
    {
        using var baseFactory = new IdentityApiFactory();
        using var factory = baseFactory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddScoped<IHandler<RequestPasswordResetCommand, PasswordResetRequested>, ConflictingHandler>()));

        using var response = await factory.CreateClient().PostAsJsonAsync("/api/v1/auth/password-reset/request", new { email = Email });
        var body = await response.Content.ReadAsStringAsync();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Accepted));
            Assert.That(
                JsonDocument.Parse(body).RootElement.GetProperty("message").GetString(),
                Is.EqualTo(PasswordResetRequested.Instance.Message));
            Assert.That(baseFactory.Logs.Entries, Has.Some.Contains("conflicted with a simultaneous request"));
            Assert.That(baseFactory.Logs.Entries, Has.None.Contains(Email));
        });
    }

    // Devuelve el mismo error que el decorador de transacción cuando el guardado choca con otra solicitud.
    private sealed class ConflictingHandler : IHandler<RequestPasswordResetCommand, PasswordResetRequested>
    {
        public Task<Result<PasswordResetRequested>> HandleAsync(RequestPasswordResetCommand request, CancellationToken ct) =>
            Task.FromResult(Result<PasswordResetRequested>.Fail(new Error(
                "persistence.conflict", "Otra solicitud cambió los mismos datos al mismo tiempo. Intente de nuevo.", ErrorType.Conflict)));
    }
}
