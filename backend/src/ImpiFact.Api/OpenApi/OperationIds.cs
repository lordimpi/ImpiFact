namespace ImpiFact.Api.OpenApi;

/// <summary>
/// El <c>operationId</c> de cada operación del documento OpenAPI (la descripción estándar de una API HTTP): un nombre
/// en camelCase que dice qué hace la operación (<c>createCompany</c>, <c>issueInvoice</c>), no cómo es su ruta. El
/// cliente del panel se genera de este documento (<c>web/projects/api</c>) y usa ese nombre para sus métodos, así que
/// cambiarlo rompe el panel: la prueba del documento fija la lista completa.
/// </summary>
/// <remarks>
/// Se pone con un transformador de la operación y no con <c>WithName</c>: <c>WithName</c> además registra un nombre de
/// endpoint para generar enlaces, que debe ser único entre todas las rutas, y algunas rutas heredadas comparten el mismo
/// código de registro que su gemela <c>/api/v1</c>. Las rutas heredadas no están en el documento, así que el
/// transformador nunca corre para ellas.
/// </remarks>
internal static class OperationIds
{
    public static TBuilder WithOperationId<TBuilder>(this TBuilder builder, string operationId)
        where TBuilder : IEndpointConventionBuilder =>
        builder.AddOpenApiOperationTransformer((operation, _, _) =>
        {
            operation.OperationId = operationId;
            return Task.CompletedTask;
        });
}
