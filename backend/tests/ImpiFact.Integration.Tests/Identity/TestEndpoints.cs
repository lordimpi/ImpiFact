using ImpiFact.Api.Endpoints;
using ImpiFact.Api.Http;
using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Identity;
using ImpiFact.Application.Abstractions.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ImpiFact.Integration.Tests.Identity;

/// <summary>
/// Endpoints que solo existen en las pruebas. Pasan por el mismo pipeline que los reales (autenticación,
/// contexto de tenant, autorización y mapeo de errores), así se prueba la composición de la API y no una copia.
/// </summary>
internal sealed class TestEndpoints : IEndpointModule
{
    /// <summary>Lo que responde <c>/test/whoami</c>: el contexto de tenant tal como lo dejó el middleware.</summary>
    public sealed record WhoAmI(Guid? CompanyId, Guid? UserId, string Actor);

    public const string InternalDetail = "internal-detail-that-must-not-leak";

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/test");

        // Sin política propia: la política por defecto exige estar autenticado.
        group.MapGet("/whoami", (ITenantContext tenant) =>
            new WhoAmI(tenant.CompanyId, tenant.UserId, tenant.Actor.ToString()));

        group.MapGet("/documents-write", () => "ok").RequireAuthorization(ApiScopes.DocumentsWrite);
        group.MapGet("/documents-read", () => "ok").RequireAuthorization(ApiScopes.DocumentsRead);
        group.MapGet("/config-write", () => "ok").RequireAuthorization(ApiScopes.ConfigWrite);
        group.MapGet("/platform", () => "ok").RequireAuthorization(ApiScopes.Platform);

        group.MapGet("/ok", () => Result<string>.Ok("hola").ToHttpResult()).AllowAnonymous();
        group.MapGet("/errors/{type}", (ErrorType type) => Result<string>.Fail(ErrorFor(type)).ToHttpResult()).AllowAnonymous();
        group.MapGet("/throw", string () => throw new InvalidOperationException(InternalDetail)).AllowAnonymous();
    }

    public static Error ErrorFor(ErrorType type) => type switch
    {
        ErrorType.Validation => Error.Validation(
            [new FieldError("nit", "El NIT es obligatorio."), new FieldError("nit", "El NIT solo admite dígitos."), new FieldError("dv", "El DV es obligatorio.")]),
        ErrorType.DianRejected => new Error("dian.rejected", "La DIAN rechazó el documento.", type)
        {
            Fields = [new FieldError("FAD06", "El CUFE no coincide.")],
        },
        ErrorType.Unexpected => new Error("test.unexpected", InternalDetail, type),
        _ => new Error($"test.{type.ToString().ToLowerInvariant()}", $"Mensaje de prueba para {type}.", type),
    };
}
