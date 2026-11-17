using System.Text.Json.Serialization;
using ImpiFact.Domain.Masters;

namespace ImpiFact.Application.Features.Masters.Customers;

// Respuestas de /api/v1/customers. Nunca se expone la entidad (Arquitectura detallada, sección 4.2); los nombres JSON van
// en snake_case y son los mismos del cliente (customer) del contrato de emisión, así el panel copia el cliente tal cual.

/// <summary>Un cliente de la empresa.</summary>
public sealed record CustomerResponse
{
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    /// <summary>Tipo de identificación (catálogo <c>type_document_identifications</c>; 6 es NIT).</summary>
    [JsonPropertyName("type_document_identification_id")]
    public int TypeDocumentIdentificationId { get; init; }

    [JsonPropertyName("identification_number")]
    public string IdentificationNumber { get; init; } = "";

    /// <summary>Dígito de verificación del NIT; nulo en otros tipos de identificación.</summary>
    [JsonPropertyName("dv")]
    public string? Dv { get; init; }

    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("phone")]
    public string? Phone { get; init; }

    [JsonPropertyName("address")]
    public string? Address { get; init; }

    [JsonPropertyName("email")]
    public string? Email { get; init; }

    [JsonPropertyName("municipality_id")]
    public int? MunicipalityId { get; init; }

    [JsonPropertyName("type_organization_id")]
    public int? TypeOrganizationId { get; init; }

    [JsonPropertyName("type_regime_id")]
    public int? TypeRegimeId { get; init; }

    [JsonPropertyName("type_liability_id")]
    public int? TypeLiabilityId { get; init; }

    [JsonPropertyName("merchant_registration")]
    public string? MerchantRegistration { get; init; }

    /// <summary>Falso si se desactivó: no aparece en el listado por defecto ni se sugiere al emitir.</summary>
    [JsonPropertyName("is_active")]
    public bool IsActive { get; init; }

    internal static CustomerResponse From(Customer customer) => new()
    {
        Id = customer.Id,
        TypeDocumentIdentificationId = customer.TypeDocumentIdentificationId,
        IdentificationNumber = customer.IdentificationNumber,
        Dv = customer.Dv,
        Name = customer.Name,
        Phone = customer.Phone,
        Address = customer.Address,
        Email = customer.Email,
        MunicipalityId = customer.MunicipalityId,
        TypeOrganizationId = customer.TypeOrganizationId,
        TypeRegimeId = customer.TypeRegimeId,
        TypeLiabilityId = customer.TypeLiabilityId,
        MerchantRegistration = customer.MerchantRegistration,
        IsActive = customer.IsActive,
    };
}

/// <summary>Una página de clientes en el orden pedido.</summary>
public sealed record CustomerListResponse
{
    [JsonPropertyName("items")]
    public IReadOnlyList<CustomerResponse> Items { get; init; } = [];

    /// <summary>Texto opaco para pedir la página siguiente (<c>?cursor=</c>, con el mismo <c>sort</c>); nulo en la última.</summary>
    [JsonPropertyName("next_cursor")]
    public string? NextCursor { get; init; }
}

/// <summary>Lo que la DIAN tiene registrado de un adquiriente (comprador), para llenar el formulario del cliente.</summary>
public sealed record AcquirerLookupResponse
{
    /// <summary>Verdadero si la DIAN tiene registrado al adquiriente (su código 200); falso si no (404).</summary>
    [JsonPropertyName("found")]
    public bool Found { get; init; }

    /// <summary>Código que respondió la DIAN, tal cual.</summary>
    [JsonPropertyName("status_code")]
    public string? StatusCode { get; init; }

    /// <summary>Mensaje de la DIAN, tal cual.</summary>
    [JsonPropertyName("message")]
    public string? Message { get; init; }

    /// <summary>Nombre o razón social registrado; nulo si no se encontró.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    /// <summary>Correo de recepción de facturas registrado; nulo si no se encontró.</summary>
    [JsonPropertyName("email")]
    public string? Email { get; init; }
}
