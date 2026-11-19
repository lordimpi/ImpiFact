using System.Text.Json.Nodes;
using ImpiFact.Api.OpenApi;

namespace ImpiFact.Api.Masters;

/// <summary>
/// Cuerpos de ejemplo de clientes y productos que Scalar (el visor de la API en <c>/scalar</c>) muestra precargados.
/// Pasan los validadores actuales: el NIT 900123456 tiene dígito de verificación 8 y los ids existen en los catálogos de
/// la DIAN (Dirección de Impuestos y Aduanas Nacionales).
/// </summary>
internal static class MasterExamples
{
    public static RequestExample[] Customers { get; } =
    [
        new("persona-juridica", "Cliente con NIT (persona jurídica)", Parse("""
            {
              "type_document_identification_id": 6,
              "identification_number": "900123456",
              "dv": "8",
              "name": "CLIENTE DE PRUEBA SAS",
              "type_organization_id": 1,
              "type_regime_id": 2,
              "type_liability_id": 14,
              "municipality_id": 1006,
              "address": "CARRERA 7 # 71-21",
              "phone": "6017654321",
              "email": "facturacion@cliente-prueba.com",
              "merchant_registration": "0000000-00"
            }
            """)),
        new("persona-natural", "Cliente con cédula de ciudadanía (persona natural)", Parse("""
            {
              "type_document_identification_id": 3,
              "identification_number": "1020304050",
              "name": "MARIA PEREZ",
              "type_organization_id": 2,
              "type_regime_id": 2,
              "municipality_id": 1006,
              "email": "maria.perez@example.com"
            }
            """)),
    ];

    public static RequestExample[] Products { get; } =
    [
        new("iva-19", "Producto con IVA del 19 %", Parse("""
            {
              "code": "PROD-001",
              "name": "Servicio de soporte mensual",
              "description": "Soporte técnico remoto, 20 horas al mes",
              "unit_measure_id": 70,
              "unit_price": 150000,
              "tax_id": 1,
              "tax_percent": 19
            }
            """)),
        new("sin-impuesto", "Producto sin impuesto", Parse("""
            {
              "code": "PROD-002",
              "name": "Libro impreso",
              "unit_measure_id": 70,
              "unit_price": 45000
            }
            """)),
    ];

    private static JsonNode Parse(string json) =>
        JsonNode.Parse(json) ?? throw new InvalidOperationException("An example body is empty.");
}
