namespace ImpiFact.Application.Abstractions.Documents;

/// <summary>
/// Inicio de la URL de consulta que va en el código QR de un documento electrónico, por ambiente de la DIAN
/// (Dirección de Impuestos y Aduanas Nacionales). Se le agrega al final el CUFE o el CUDE (códigos únicos de factura y
/// de documento electrónico). Sale de la configuración de la plataforma, no del código.
/// </summary>
public interface IDianQrUrls
{
    /// <summary>URL para el ambiente: 1 producción, cualquier otro valor habilitación (como el PHP).</summary>
    string ForEnvironment(int typeEnvironmentId);
}
