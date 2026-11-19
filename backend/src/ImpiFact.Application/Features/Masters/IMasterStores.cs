using ImpiFact.Application.Features.Masters.Customers;
using ImpiFact.Application.Features.Masters.Products;
using ImpiFact.Domain.Masters;

namespace ImpiFact.Application.Features.Masters;

/// <summary>
/// Clientes de la empresa (tabla <c>customers</c>). Los listados van con SQL escrito a mano y las escrituras con EF Core
/// (Entity Framework, el mapeador de objetos a tablas); la implementación vive en Infrastructure. Toda operación recibe la
/// empresa: nunca toca los clientes de otra.
/// </summary>
/// <remarks>
/// El puerto vive en el módulo y no en <c>Application/Abstractions</c> porque solo lo usan estos casos de uso.
/// </remarks>
public interface ICustomerStore
{
    /// <summary>Una página del listado, por clave (columna de orden, id) y no por desplazamiento.</summary>
    Task<IReadOnlyList<CustomerResponse>> ListAsync(MasterPage page, CancellationToken ct);

    /// <summary>El cliente con su seguimiento de cambios, para leerlo o modificarlo; nulo si no existe en la empresa.</summary>
    Task<Customer?> FindAsync(Guid companyId, Guid id, CancellationToken ct);

    /// <summary>
    /// Verdadero si otro cliente de la empresa (activo o no, distinto de <paramref name="exceptId"/>) ya tiene ese tipo y
    /// número de identificación.
    /// </summary>
    Task<bool> IsIdentificationTakenAsync(Guid companyId, int typeDocumentIdentificationId, string identificationNumber, Guid? exceptId, CancellationToken ct);

    /// <summary>Agrega el cliente; se guarda con la transacción del caso de uso.</summary>
    void Add(Customer customer);
}

/// <summary>Productos y servicios de la empresa (tabla <c>items</c>), con las mismas reglas que <see cref="ICustomerStore"/>.</summary>
public interface IProductStore
{
    Task<IReadOnlyList<ProductResponse>> ListAsync(MasterPage page, CancellationToken ct);

    Task<Item?> FindAsync(Guid companyId, Guid id, CancellationToken ct);

    /// <summary>Verdadero si otro producto de la empresa (activo o no, distinto de <paramref name="exceptId"/>) ya tiene ese código.</summary>
    Task<bool> IsCodeTakenAsync(Guid companyId, string code, Guid? exceptId, CancellationToken ct);

    void Add(Item item);
}
