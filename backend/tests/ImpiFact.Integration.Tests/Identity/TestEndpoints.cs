using ImpiFact.Api.Endpoints;
using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace ImpiFact.Integration.Tests.Identity;

/// <summary>
/// Endpoints que solo existen en las pruebas. Pasan por el mismo pipeline que los reales (autenticación,
/// contexto de tenant y autorización), así se prueba la composición de la API y no una copia.
/// </summary>
internal sealed class TestEndpoints : IEndpointModule
{
    /// <summary>Lo que responde <c>/test/whoami</c>: el contexto de tenant tal como lo dejó el middleware.</summary>
    public sealed record WhoAmI(Guid? CompanyId, Guid? UserId, string Actor);

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
    }
}
