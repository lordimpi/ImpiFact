using FluentValidation;
using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Handlers;
using ImpiFact.Application.Abstractions.Results;

namespace ImpiFact.Application.Features.Masters.Products;

/// <summary><c>GET /api/v1/products</c>: productos de la empresa activa, página por página.</summary>
/// <param name="Search">Texto que contiene el código o el nombre, sin distinguir mayúsculas.</param>
/// <param name="Status"><c>active</c> (por defecto), <c>inactive</c> o <c>all</c>.</param>
/// <param name="Sort"><c>name</c> (por defecto) o <c>code</c>; con <c>-</c> delante, de mayor a menor.</param>
/// <param name="Cursor">El <c>next_cursor</c> de la página anterior, pedido con el mismo <paramref name="Sort"/>.</param>
/// <param name="Limit">Productos por página; 20 si no se indica, máximo 100.</param>
public sealed record ListProductsQuery(string? Search, string? Status, string? Sort, string? Cursor, int? Limit);

/// <summary><c>GET /api/v1/products/{id}</c>.</summary>
public sealed record GetProductQuery(Guid Id);

internal static class ProductSorts
{
    public const string Name = "name";

    public const string Code = "code";

    public static readonly IReadOnlyCollection<string> Fields = [Name, Code];
}

internal sealed class ListProductsQueryValidator : AbstractValidator<ListProductsQuery>
{
    public ListProductsQueryValidator() =>
        MasterListRules.Apply(
            this, query => query.Search, query => query.Status, query => query.Sort, query => query.Cursor, query => query.Limit,
            ProductSorts.Fields, ProductSorts.Name);
}

/// <summary>Pide una fila más de las que muestra: si llega, hay página siguiente.</summary>
internal sealed class ListProductsHandler(IProductStore products, ITenantContext tenant) : IHandler<ListProductsQuery, ProductListResponse>
{
    public async Task<Result<ProductListResponse>> HandleAsync(ListProductsQuery request, CancellationToken ct)
    {
        if (tenant.CompanyId is not { } companyId)
        {
            return MasterErrors.ProductNotFound;
        }

        var page = MasterListing.Page(
            companyId, request.Search, request.Status, request.Sort, request.Cursor, request.Limit, ProductSorts.Fields, ProductSorts.Name);
        var rows = await products.ListAsync(page, ct);
        var (items, next) = MasterListing.Cut(rows, page, row => new MasterKey(page.SortField == ProductSorts.Name ? row.Name : row.Code, row.Id));
        return new ProductListResponse { Items = items, NextCursor = next };
    }
}

/// <summary>Un producto de la empresa activa; uno de otra empresa responde 404, igual que uno que no existe.</summary>
internal sealed class GetProductHandler(IProductStore products, ITenantContext tenant) : IHandler<GetProductQuery, ProductResponse>
{
    public async Task<Result<ProductResponse>> HandleAsync(GetProductQuery request, CancellationToken ct) =>
        tenant.CompanyId is { } companyId && await products.FindAsync(companyId, request.Id, ct) is { } item
            ? Result<ProductResponse>.Ok(ProductResponse.From(item))
            : MasterErrors.ProductNotFound;
}
