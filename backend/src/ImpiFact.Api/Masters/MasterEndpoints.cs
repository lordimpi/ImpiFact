using ImpiFact.Api.Endpoints;
using ImpiFact.Api.Http;
using ImpiFact.Api.OpenApi;
using ImpiFact.Application.Abstractions.Handlers;
using ImpiFact.Application.Abstractions.Identity;
using ImpiFact.Application.Features.Masters.Customers;
using ImpiFact.Application.Features.Masters.Products;
using Microsoft.AspNetCore.Mvc;

namespace ImpiFact.Api.Masters;

/// <summary>
/// Clientes y productos de la empresa activa (Arquitectura frontend, sección 7: <c>/clientes</c> y <c>/productos</c>, para
/// el propietario y el vendedor). Todas las rutas piden <c>documents:write</c>, el alcance de quien emite: lo tienen el
/// propietario, el vendedor y las API keys de emisión, y no el rol cliente del portal ni las llaves de solo consulta, que
/// con <c>documents:read</c> verían los datos de todos los clientes de la empresa. Toda lista pagina, ordena y filtra en el
/// servidor.
/// </summary>
internal sealed class MasterEndpoints : IEndpointModule
{
    private const string Listing =
        " Pagina por cursor: `limit` (20 si no llega, máximo 100) y `cursor` (el `next_cursor` de la página anterior, " +
        "pedido con el mismo `sort`; nulo en la última). `status` es `active` (por defecto), `inactive` o `all`. Un " +
        "parámetro no válido responde 422.";

    private const string Isolation = " Un id de otra empresa responde 404, igual que uno que no existe.";

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var customers = endpoints.MapGroup("/api/v1/customers").WithTags(ApiTags.Masters).RequireAuthorization(ApiScopes.DocumentsWrite);
        customers.MapGet("/", ListCustomers)
            .Produces<CustomerListResponse>()
            .WithOperationId("listCustomers")
            .WithSummary("Listar los clientes")
            .WithDescription(
                "Clientes de la empresa activa. `search` busca el texto dentro del número de identificación o del nombre, sin " +
                "distinguir mayúsculas. `sort` es `name` (por defecto) o `identification_number`; con `-` delante, de mayor a " +
                "menor." + Listing);
        customers.MapGet("/lookup", LookupAcquirer)
            .Produces<AcquirerLookupResponse>()
            .WithOperationId("lookupAcquirer")
            .WithSummary("Consultar un adquiriente en la DIAN")
            .WithDescription(
                "Pregunta en vivo a la DIAN (servicio GetAcquirer) qué nombre y qué correo de recepción de facturas tiene " +
                "registrados el comprador con `type` (id del catálogo de tipos de identificación; 6 es NIT) y `number` (sin " +
                "dígito de verificación), para llenar el formulario del cliente. Usa el certificado activo y el ambiente de " +
                "facturación de la empresa. Responde 200 con `found: true` y `name`/`email`, o con `found: false` si la DIAN " +
                "no lo tiene registrado. Sin certificado activo: 404 (`certificate.not_found`); vencido: 422 " +
                "(`certificate.expired`); la DIAN rechazó la consulta: 422 (`dian.fault` o `dian.rejected`); la DIAN no " +
                "respondió: 503 (`dian.unavailable`). No guarda nada.");
        customers.MapGet("/{id:guid}", GetCustomer)
            .Produces<CustomerResponse>()
            .WithOperationId("getCustomer")
            .WithSummary("Ver un cliente")
            .WithDescription("Datos del cliente `{id}`." + Isolation);
        customers.MapPost("/", CreateCustomer)
            .Produces<CustomerResponse>(StatusCodes.Status201Created)
            .WithRequestExamples(MasterExamples.Customers)
            .WithOperationId("createCustomer")
            .WithSummary("Crear un cliente")
            .WithDescription(
                "Crea un cliente con los campos del cliente (`customer`) de la emisión y las mismas reglas: identificación de " +
                "letras y números (hasta 15), `dv` obligatorio con NIT (`type_document_identification_id` 6) y siempre el que " +
                "calcula la DIAN, correo válido y catálogos existentes. Responde 201 con el cliente y su ubicación. Otro " +
                "cliente de la empresa con el mismo tipo y número de identificación, aunque esté desactivado: 409 " +
                "(`customer.duplicate`).");
        customers.MapPut("/{id:guid}", UpdateCustomer)
            .Produces<CustomerResponse>()
            .WithRequestExamples(MasterExamples.Customers)
            .WithOperationId("updateCustomer")
            .WithSummary("Editar un cliente")
            .WithDescription(
                "Reemplaza todos los datos del cliente `{id}` con las reglas del alta: lo que no llega queda vacío. " +
                "`is_active: true` reactiva un cliente desactivado. Tipo y número de identificación de otro cliente: 409 " +
                "(`customer.duplicate`)." + Isolation);
        customers.MapPost("/{id:guid}/deactivate", DeactivateCustomer)
            .Produces(StatusCodes.Status204NoContent)
            .WithOperationId("deactivateCustomer")
            .WithSummary("Desactivar un cliente")
            .WithDescription(
                "Desactiva el cliente `{id}` y responde 204 sin cuerpo; repetirlo responde lo mismo. Nunca se borra: los " +
                "documentos emitidos pueden referirse a él. Deja de salir en el listado por defecto (`status=active`) y se " +
                "reactiva con la edición (`is_active: true`)." + Isolation);

        var products = endpoints.MapGroup("/api/v1/products").WithTags(ApiTags.Masters).RequireAuthorization(ApiScopes.DocumentsWrite);
        products.MapGet("/", ListProducts)
            .Produces<ProductListResponse>()
            .WithOperationId("listProducts")
            .WithSummary("Listar los productos")
            .WithDescription(
                "Productos y servicios de la empresa activa. `search` busca el texto dentro del código o del nombre, sin " +
                "distinguir mayúsculas. `sort` es `name` (por defecto) o `code`; con `-` delante, de mayor a menor." + Listing);
        products.MapGet("/{id:guid}", GetProduct)
            .Produces<ProductResponse>()
            .WithOperationId("getProduct")
            .WithSummary("Ver un producto")
            .WithDescription("Datos del producto `{id}`." + Isolation);
        products.MapPost("/", CreateProduct)
            .Produces<ProductResponse>(StatusCodes.Status201Created)
            .WithRequestExamples(MasterExamples.Products)
            .WithOperationId("createProduct")
            .WithSummary("Crear un producto")
            .WithDescription(
                "Crea un producto: `code` (único en la empresa), `name` (lo que va en la línea de la emisión), " +
                "`unit_measure_id`, `unit_price` (sin impuestos, hasta 6 decimales) y, si lleva impuesto, `tax_id` con " +
                "`tax_percent` (obligatorio salvo con los impuestos 2 y 10) o `tax_per_unit_amount` (obligatorio con el 10). " +
                "`type_item_identification_id` es 4 (código propio) si no llega. Responde 201 con el producto. Código " +
                "repetido, aunque el otro esté desactivado: 409 (`product.duplicate`).");
        products.MapPut("/{id:guid}", UpdateProduct)
            .Produces<ProductResponse>()
            .WithRequestExamples(MasterExamples.Products)
            .WithOperationId("updateProduct")
            .WithSummary("Editar un producto")
            .WithDescription(
                "Reemplaza todos los datos del producto `{id}` con las reglas del alta: lo que no llega queda vacío. " +
                "`is_active: true` reactiva un producto desactivado. Código de otro producto: 409 (`product.duplicate`)." +
                Isolation);
        products.MapPost("/{id:guid}/deactivate", DeactivateProduct)
            .Produces(StatusCodes.Status204NoContent)
            .WithOperationId("deactivateProduct")
            .WithSummary("Desactivar un producto")
            .WithDescription(
                "Desactiva el producto `{id}` y responde 204 sin cuerpo; repetirlo responde lo mismo. Nunca se borra. Se " +
                "reactiva con la edición (`is_active: true`)." + Isolation);
    }

    private static async Task<IResult> ListCustomers(
        [FromQuery(Name = "search")] string? search,
        [FromQuery(Name = "status")] string? status,
        [FromQuery(Name = "sort")] string? sort,
        [FromQuery(Name = "cursor")] string? cursor,
        [FromQuery(Name = "limit")] int? limit,
        IHandler<ListCustomersQuery, CustomerListResponse> handler,
        CancellationToken ct) =>
        (await handler.HandleAsync(new ListCustomersQuery(search, status, sort, cursor, limit), ct)).ToHttpResult();

    private static async Task<IResult> LookupAcquirer(
        [FromQuery(Name = "type")] int? type,
        [FromQuery(Name = "number")] string? number,
        IHandler<LookupAcquirerQuery, AcquirerLookupResponse> handler,
        CancellationToken ct) =>
        (await handler.HandleAsync(new LookupAcquirerQuery(type, number), ct)).ToHttpResult();

    private static async Task<IResult> GetCustomer(Guid id, IHandler<GetCustomerQuery, CustomerResponse> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new GetCustomerQuery(id), ct)).ToHttpResult();

    private static async Task<IResult> CreateCustomer(
        CreateCustomerCommand body, IHandler<CreateCustomerCommand, CustomerResponse> handler, CancellationToken ct) =>
        (await handler.HandleAsync(body, ct)).ToHttpResult(created => TypedResults.Created($"/api/v1/customers/{created.Id}", created));

    private static async Task<IResult> UpdateCustomer(
        Guid id, UpdateCustomerCommand body, IHandler<UpdateCustomerCommand, CustomerResponse> handler, CancellationToken ct) =>
        (await handler.HandleAsync(body with { Id = id }, ct)).ToHttpResult();

    private static async Task<IResult> DeactivateCustomer(
        Guid id, IHandler<DeactivateCustomerCommand, CustomerResponse> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new DeactivateCustomerCommand(id), ct)).ToHttpResult(_ => TypedResults.NoContent());

    private static async Task<IResult> ListProducts(
        [FromQuery(Name = "search")] string? search,
        [FromQuery(Name = "status")] string? status,
        [FromQuery(Name = "sort")] string? sort,
        [FromQuery(Name = "cursor")] string? cursor,
        [FromQuery(Name = "limit")] int? limit,
        IHandler<ListProductsQuery, ProductListResponse> handler,
        CancellationToken ct) =>
        (await handler.HandleAsync(new ListProductsQuery(search, status, sort, cursor, limit), ct)).ToHttpResult();

    private static async Task<IResult> GetProduct(Guid id, IHandler<GetProductQuery, ProductResponse> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new GetProductQuery(id), ct)).ToHttpResult();

    private static async Task<IResult> CreateProduct(
        CreateProductCommand body, IHandler<CreateProductCommand, ProductResponse> handler, CancellationToken ct) =>
        (await handler.HandleAsync(body, ct)).ToHttpResult(created => TypedResults.Created($"/api/v1/products/{created.Id}", created));

    private static async Task<IResult> UpdateProduct(
        Guid id, UpdateProductCommand body, IHandler<UpdateProductCommand, ProductResponse> handler, CancellationToken ct) =>
        (await handler.HandleAsync(body with { Id = id }, ct)).ToHttpResult();

    private static async Task<IResult> DeactivateProduct(
        Guid id, IHandler<DeactivateProductCommand, ProductResponse> handler, CancellationToken ct) =>
        (await handler.HandleAsync(new DeactivateProductCommand(id), ct)).ToHttpResult(_ => TypedResults.NoContent());
}
