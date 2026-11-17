using FluentValidation;
using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Handlers;
using ImpiFact.Application.Abstractions.Results;

namespace ImpiFact.Application.Features.Masters.Customers;

/// <summary><c>GET /api/v1/customers</c>: clientes de la empresa activa, página por página.</summary>
/// <param name="Search">Texto que contiene el número de identificación o el nombre, sin distinguir mayúsculas.</param>
/// <param name="Status"><c>active</c> (por defecto), <c>inactive</c> o <c>all</c>.</param>
/// <param name="Sort"><c>name</c> (por defecto) o <c>identification_number</c>; con <c>-</c> delante, de mayor a menor.</param>
/// <param name="Cursor">El <c>next_cursor</c> de la página anterior, pedido con el mismo <paramref name="Sort"/>.</param>
/// <param name="Limit">Clientes por página; 20 si no se indica, máximo 100.</param>
public sealed record ListCustomersQuery(string? Search, string? Status, string? Sort, string? Cursor, int? Limit);

/// <summary><c>GET /api/v1/customers/{id}</c>.</summary>
public sealed record GetCustomerQuery(Guid Id);

internal static class CustomerSorts
{
    public const string Name = "name";

    public const string IdentificationNumber = "identification_number";

    public static readonly IReadOnlyCollection<string> Fields = [Name, IdentificationNumber];
}

internal sealed class ListCustomersQueryValidator : AbstractValidator<ListCustomersQuery>
{
    public ListCustomersQueryValidator() =>
        MasterListRules.Apply(
            this, query => query.Search, query => query.Status, query => query.Sort, query => query.Cursor, query => query.Limit,
            CustomerSorts.Fields, CustomerSorts.Name);
}

/// <summary>
/// Pide una fila más de las que muestra: si llega, hay página siguiente y su cursor es la posición del último cliente
/// mostrado. El decorador de empresa ya cortó las solicitudes sin empresa activa.
/// </summary>
internal sealed class ListCustomersHandler(ICustomerStore customers, ITenantContext tenant) : IHandler<ListCustomersQuery, CustomerListResponse>
{
    public async Task<Result<CustomerListResponse>> HandleAsync(ListCustomersQuery request, CancellationToken ct)
    {
        if (tenant.CompanyId is not { } companyId)
        {
            return MasterErrors.CustomerNotFound;
        }

        var page = MasterListing.Page(
            companyId, request.Search, request.Status, request.Sort, request.Cursor, request.Limit, CustomerSorts.Fields, CustomerSorts.Name);
        var rows = await customers.ListAsync(page, ct);
        var (items, next) = MasterListing.Cut(
            rows, page, row => new MasterKey(page.SortField == CustomerSorts.Name ? row.Name : row.IdentificationNumber, row.Id));
        return new CustomerListResponse { Items = items, NextCursor = next };
    }
}

/// <summary>Un cliente de la empresa activa; uno de otra empresa responde 404, igual que uno que no existe.</summary>
internal sealed class GetCustomerHandler(ICustomerStore customers, ITenantContext tenant) : IHandler<GetCustomerQuery, CustomerResponse>
{
    public async Task<Result<CustomerResponse>> HandleAsync(GetCustomerQuery request, CancellationToken ct) =>
        tenant.CompanyId is { } companyId && await customers.FindAsync(companyId, request.Id, ct) is { } customer
            ? Result<CustomerResponse>.Ok(CustomerResponse.From(customer))
            : MasterErrors.CustomerNotFound;
}
