using ImpiFact.Domain.Catalog;

namespace ImpiFact.Application.Abstractions.Companies;

/// <summary>
/// Revisa que un identificador exista en un catálogo DIAN (las listas de códigos de la Dirección de Impuestos y
/// Aduanas Nacionales: idiomas, municipios, regímenes…). Así un valor inexistente se rechaza como dato inválido en
/// lugar de fallar al guardar por la llave foránea.
/// </summary>
public interface ICatalogLookup
{
    Task<bool> ExistsAsync<TCatalog>(int id, CancellationToken ct)
        where TCatalog : CatalogEntry;
}
