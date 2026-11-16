namespace ImpiFact.Application.Abstractions.Companies;

/// <summary>
/// Dirección del servicio web de la DIAN (Dirección de Impuestos y Aduanas Nacionales) para cada ambiente. Sale de
/// la configuración de la plataforma; el software de una empresa la usa cuando la solicitud no trae una propia.
/// </summary>
public interface IDianServiceUrls
{
    /// <summary>URL del servicio para el ambiente: 1 producción, cualquier otro valor habilitación (como el PHP).</summary>
    string ForEnvironment(int typeEnvironmentId);
}
