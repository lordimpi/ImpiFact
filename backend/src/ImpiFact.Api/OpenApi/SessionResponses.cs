using ImpiFact.Api.Identity;
using ImpiFact.Application.Features.Identity.Sessions;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace ImpiFact.Api.OpenApi;

/// <summary>
/// Respuesta de <c>login</c> y <c>refresh</c> en el documento OpenAPI (la descripción estándar de una API HTTP). Una
/// integración recibe <see cref="SessionTokens"/>, con el <c>refresh_token</c> en el cuerpo; el panel (con el
/// encabezado <c>X-Client: panel</c>) recibe <see cref="PanelSessionTokens"/>, sin él, porque va en la cookie. La
/// respuesta 200 se describe como una de las dos (<c>oneOf</c>), cada una con nombre propio: el cliente generado del
/// panel la tipifica como la unión de ambas, y como las dos traen los mismos campos salvo <c>refresh_token</c>, el
/// panel la usa como <c>PanelSessionTokens</c> sin conversiones. Solo cambia el documento, no la ruta.
/// </summary>
internal static class SessionResponses
{
    public static RouteHandlerBuilder WithSessionResponses(this RouteHandlerBuilder route) => route
        .Produces<SessionTokens>()
        .AddOpenApiOperationTransformer(async (operation, context, ct) =>
        {
            if (context.Document is not { } document
                || operation.Responses?.TryGetValue("200", out var response) != true
                || response!.Content?.TryGetValue("application/json", out var json) != true
                || json!.Schema is not { } integration)
            {
                return;
            }

            var panel = await context.GetOrCreateSchemaAsync(typeof(PanelSessionTokens), cancellationToken: ct);
            document.AddComponent(nameof(PanelSessionTokens), panel);
            json.Schema = new OpenApiSchema
            {
                OneOf = [integration, new OpenApiSchemaReference(nameof(PanelSessionTokens), document)],
            };
        });

    /// <summary>Descripción de los dos esquemas de la sesión, para que quien lea el documento sepa cuál recibe.</summary>
    public static void DescribeSchema(OpenApiSchema schema, OpenApiSchemaTransformerContext context)
    {
        if (context.JsonTypeInfo.Type == typeof(SessionTokens))
        {
            schema.Description =
                "Sesión de una integración (sin el encabezado `X-Client: panel`): trae el `refresh_token` con el que se renueva.";
        }
        else if (context.JsonTypeInfo.Type == typeof(PanelSessionTokens))
        {
            schema.Description =
                "Sesión del panel (con el encabezado `X-Client: panel`): los mismos campos de `SessionTokens` menos el " +
                "`refresh_token`, que viaja en la cookie `" + PanelSession.CookieName + "`.";
        }
    }
}
