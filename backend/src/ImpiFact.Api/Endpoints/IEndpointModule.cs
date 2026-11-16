namespace ImpiFact.Api.Endpoints;

/// <summary>
/// Endpoints de un módulo (Arquitectura detallada, sección 9.3: Minimal API agrupada por módulo). Cada módulo se
/// registra en el contenedor de dependencias y la API los mapea todos al final del pipeline, después de la
/// autenticación, el contexto de tenant y la autorización.
/// </summary>
public interface IEndpointModule
{
    void MapEndpoints(IEndpointRouteBuilder endpoints);
}
