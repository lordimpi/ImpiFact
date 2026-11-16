namespace ImpiFact.Golden.Tests;

/// <summary>
/// Configuración fija que tenía ApiDian cuando se capturaron las fixtures golden. Refleja
/// tools/golden-capture/capture.py (COMPANY, SOFTWARE_ID, SOFTWARE_PIN, TECHNICAL_KEY y la resolución
/// SETP) más los valores por defecto de ApiDian de los que depende ese script. Mantenga ambos sincronizados.
/// </summary>
public static class GoldenCaptureContext
{
    public const string CompanyNit = "900000001";
    public const string CompanyDv = "2";
    public const string SoftwareId = "3f9d8c2e-5b1a-4e7f-9c3d-2a6b8e1f0d47";
    public const string SoftwarePin = "12345";
    public const string TechnicalKey = "fc8eac422eba16e22ffd8c6f94b3f40a6e38162c";

    public const int CompanyIdentificationTypeId = 6;
    public const int CompanyOrganizationTypeId = 1;
    public const int CompanyRegimeId = 1;
    public const int CompanyLiabilityId = 14;
    public const string CompanyName = "EMPRESA DE PRUEBA GOLDEN SAS";
    public const string CompanyMerchantRegistration = "0000000-00";
    public const int CompanyMunicipalityId = 149;
    public const string CompanyAddress = "CALLE 1 # 1-01";
    public const string CompanyPhone = "3000000000";
    public const string CompanyEmail = "golden@example.com";

    public const string ResolutionNumber = "18760000001";
    public const string ResolutionPrefix = "SETP";
    public static readonly DateOnly ResolutionDateFrom = new(2019, 1, 19);
    public static readonly DateOnly ResolutionDateTo = new(2030, 1, 19);
    public const long ResolutionFrom = 990000000;
    public const long ResolutionTo = 995000000;

    // Valores por defecto de ApiDian que capture.py no sobrescribe.

    /// <summary>type_environment_id 2: pruebas (habilitación).</summary>
    public const int EnvironmentId = 2;

    /// <summary>type_currency_id 35: COP.</summary>
    public const int CompanyCurrencyId = 35;

    /// <summary>country_id 46: Colombia, para la empresa y los clientes.</summary>
    public const int CountryId = 46;

    /// <summary>language_id 79: es.</summary>
    public const int LanguageId = 79;

    /// <summary>tax_id 1: IVA, el esquema tributario de la empresa y de los clientes.</summary>
    public const int PartyTaxId = 1;

    /// <summary>type_operation_id 10: operación estándar.</summary>
    public const int OperationTypeId = 10;
}
