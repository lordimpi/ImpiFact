using ImpiFact.Api.Endpoints;
using ImpiFact.Infrastructure.Identity;

namespace ImpiFact.Api.Identity;

/// <summary>Endpoints de Identity que no necesitan tablas.</summary>
internal sealed class IdentityEndpoints : IEndpointModule
{
    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        // JWKS (JSON Web Key Set): las llaves públicas que validan los JWT, para quien quiera validarlos por su
        // cuenta (Arquitectura detallada, sección 11). Solo partes públicas.
        endpoints.MapGet("/.well-known/jwks.json", (JwtSigningKeys keys) => TypedResults.Ok(new { keys = keys.PublicJsonWebKeys() }))
            .AllowAnonymous()
            .WithTags("Identity");
    }
}
