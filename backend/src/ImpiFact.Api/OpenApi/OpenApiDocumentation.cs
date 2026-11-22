using ImpiFact.Api.Identity;
using ImpiFact.Application.Abstractions.Identity;
using ImpiFact.Application.Features.Documents.Issue;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using Scalar.AspNetCore;

namespace ImpiFact.Api.OpenApi;

/// <summary>
/// Documento OpenAPI (la descripción estándar de una API HTTP, en <c>/openapi/v1.json</c>) y la página de Scalar (el
/// visor interactivo en <c>/scalar</c>) en español. Agrega la introducción con el flujo de integración, el esquema de
/// autenticación Bearer (para que Scalar muestre dónde escribir la credencial), la descripción de cada sección y, en
/// cada operación, el permiso que pide y valores de ejemplo para sus parámetros (<see cref="OpenApiExamples"/>; los
/// cuerpos de ejemplo los agrega cada ruta con <c>WithRequestExamples</c>). Se documentan las rutas <c>/api/v1</c>; las
/// heredadas del PHP se borraron en la fase 7a (T9b2).
/// </summary>
internal static class OpenApiDocumentation
{
    public const string Title = "ImpiFact API";

    public const string Version = "v1";

    public static IServiceCollection AddImpiFactOpenApi(this IServiceCollection services) =>
        services.AddOpenApi(options =>
        {
            options.AddDocumentTransformer((document, _, _) =>
            {
                Describe(document);
                return Task.CompletedTask;
            });
            options.AddOperationTransformer((operation, context, _) =>
            {
                DescribePermission(operation, context);
                OpenApiExamples.DescribeParameters(operation, context.Description.RelativePath);
                return Task.CompletedTask;
            });
            options.AddSchemaTransformer((schema, context, _) =>
            {
                DescribeLegacyScalar(schema, context);
                SessionResponses.DescribeSchema(schema, context);
                return Task.CompletedTask;
            });
        });

    /// <summary>Ruta del módulo de JavaScript con la configuración extra de Scalar.</summary>
    public const string ScalarConfigurationPath = "/scalar-config.js";

    // El tema «halloween» existe en el visor de Scalar, pero la enumeración ScalarTheme de la librería de .NET no lo
    // trae; por eso se pasa en este módulo, que Scalar importa y mezcla con el resto de su configuración.
    private const string ScalarConfigurationModule = "export default { theme: 'halloween' };\n";

    public static IEndpointConventionBuilder MapImpiFactApiReference(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(ScalarConfigurationPath, () => Results.Text(ScalarConfigurationModule, "text/javascript"))
            .AllowAnonymous()
            .ExcludeFromDescription();

        return endpoints.MapScalarApiReference(options => options
            .WithTitle(Title)
            .AddPreferredSecuritySchemes([AuthenticationSchemes.Bearer])
            .WithDefaultHttpClient(ScalarTarget.Shell, ScalarClient.Curl)
            .WithJavaScriptConfiguration(ScalarConfigurationPath));
    }

    private static void Describe(OpenApiDocument document)
    {
        document.Info ??= new OpenApiInfo();
        document.Info.Title = Title;
        document.Info.Version = Version;
        document.Info.Description = OpenApiIntroduction.Markdown;
        EnsureBearerScheme(document);

        var tags = new HashSet<OpenApiTag>();
        foreach (var (name, description) in ApiTags.Descriptions)
        {
            tags.Add(new OpenApiTag { Name = name, Description = description });
        }

        // Una sección que alguna ruta use y no tenga descripción se conserva tal como vino, para no perderla.
        foreach (var tag in document.Tags ?? new HashSet<OpenApiTag>())
        {
            if (!ApiTags.Descriptions.Any(described => described.Name == tag.Name))
            {
                tags.Add(tag);
            }
        }

        document.Tags = tags;
    }

    /// <summary>
    /// Un solo esquema <c>Authorization: Bearer</c> cubre las API keys (<c>apk_...</c>) y los JWT (JSON Web Token) del panel, igual que
    /// la autenticación de la API (<see cref="BearerAuthenticationExtensions"/>).
    /// </summary>
    private static void EnsureBearerScheme(OpenApiDocument document)
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes[AuthenticationSchemes.Bearer] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "API key (apk_...) o JWT",
            Description =
                "Escriba la credencial sin la palabra Bearer: una API key (empieza con `apk_`) o el `access_token` que " +
                "entrega `POST /api/v1/auth/login`.",
        };
    }

    /// <summary>
    /// Toda ruta pide credencial salvo las marcadas como anónimas (la política por defecto exige estar autenticado).
    /// A las que la piden se les agrega el requisito Bearer y, al final de su descripción, el permiso que exige su
    /// política. Una operación sin descripción se deja sin ella, para que la prueba del documento la detecte.
    /// </summary>
    private static void DescribePermission(OpenApiOperation operation, OpenApiOperationTransformerContext context)
    {
        var metadata = context.Description.ActionDescriptor.EndpointMetadata;
        var anonymous = metadata.OfType<IAllowAnonymous>().Any();
        if (!anonymous && context.Document is { } document)
        {
            EnsureBearerScheme(document);
            operation.Security ??= [];
            operation.Security.Add(new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference(AuthenticationSchemes.Bearer, document)] = [],
            });
        }

        if (string.IsNullOrWhiteSpace(operation.Description))
        {
            return;
        }

        var policies = metadata.OfType<IAuthorizeData>().Select(data => data.Policy).OfType<string>().Distinct().ToList();
        var permission = anonymous ? "no pide credencial." : policies.Count == 0
            ? "cualquier credencial válida."
            : string.Join(" y ", policies.Select(PermissionOf)) + ".";
        operation.Description = $"{operation.Description}\n\n**Permiso:** {permission}";
    }

    /// <summary>
    /// Los valores simples del contrato heredado (<see cref="LegacyScalar"/>) llegan como texto, número o booleano; su
    /// conversor propio deja el esquema vacío, así que se describe a mano.
    /// </summary>
    private static void DescribeLegacyScalar(OpenApiSchema schema, OpenApiSchemaTransformerContext context)
    {
        var underlying = Nullable.GetUnderlyingType(context.JsonTypeInfo.Type);
        if ((underlying ?? context.JsonTypeInfo.Type) == typeof(LegacyScalar))
        {
            schema.Type = JsonSchemaType.String | JsonSchemaType.Number | JsonSchemaType.Boolean
                | (underlying is null ? 0 : JsonSchemaType.Null);
            schema.Description = "Texto, número o booleano: el contrato heredado acepta cualquiera de las tres formas.";
        }
    }

    private static string PermissionOf(string policy) => policy switch
    {
        ApiScopes.Platform => $"alcance `{ApiScopes.Platform}` con una llave de plataforma (sin empresa)",
        IdentityPolicies.ManageCredentials =>
            $"`{ApiScopes.ConfigWrite}` con una credencial de empresa, o `{ApiScopes.Platform}` con una credencial de plataforma",
        _ => $"alcance `{policy}`",
    };
}
