using System.Text.Json.Serialization;

namespace ImpiFact.Application.Features.Companies.Reads;

// Lecturas de la configuración de la empresa para el panel (Arquitectura frontend, sección 16, punto 12). Todas actúan
// sobre la empresa de la credencial: el NIT (número de identificación tributaria) de la ruta debe ser el suyo, igual
// que en las escrituras. Ninguna respuesta lleva el PIN del software, la clave técnica de una resolución ni nada del
// certificado más allá de si sirve.

/// <summary><c>GET /api/v1/companies/{nit}</c>: los datos de la empresa con la forma que acepta la actualización.</summary>
public sealed record GetCompanyQuery(string Nit);

/// <summary><c>GET /api/v1/companies/{nit}/environments</c>: el ambiente de la DIAN de cada familia de documentos.</summary>
public sealed record GetCompanyEnvironmentsQuery(string Nit);

/// <summary><c>GET /api/v1/companies/{nit}/software</c>: el software registrado de cada familia y ambiente, sin el PIN.</summary>
public sealed record GetCompanySoftwareQuery(string Nit);

/// <summary><c>GET /api/v1/companies/{nit}/resolutions</c>: todas las resoluciones con su consecutivo.</summary>
public sealed record ListCompanyResolutionsQuery(string Nit);

/// <summary><c>GET /api/v1/companies/{nit}/setup-status</c>: lo que el asistente de paso a producción revisa.</summary>
public sealed record GetCompanySetupStatusQuery(string Nit);

/// <summary>Ambientes de la empresa, uno por familia de documentos.</summary>
public sealed record CompanyEnvironmentsResponse
{
    [JsonPropertyName("environments")]
    public IReadOnlyList<EnvironmentResponse> Environments { get; init; } = [];
}

/// <summary>Software registrado de la empresa.</summary>
public sealed record CompanySoftwareResponse
{
    [JsonPropertyName("software")]
    public IReadOnlyList<ConfiguredSoftwareResponse> Software { get; init; } = [];
}

/// <summary>Software de una familia en un ambiente. Dice si hay PIN guardado, pero nunca lo devuelve.</summary>
public sealed record ConfiguredSoftwareResponse
{
    /// <summary>1 factura y notas, 2 nómina, 3 documento equivalente, 4 documento soporte, 5 eventos.</summary>
    [JsonPropertyName("document_kind")]
    public int DocumentKind { get; init; }

    /// <summary>1 producción, 2 habilitación.</summary>
    [JsonPropertyName("type_environment_id")]
    public int TypeEnvironmentId { get; init; }

    [JsonPropertyName("identifier")]
    public string Identifier { get; init; } = "";

    [JsonPropertyName("url")]
    public string Url { get; init; } = "";

    [JsonPropertyName("test_set_id")]
    public string? TestSetId { get; init; }

    [JsonPropertyName("has_pin")]
    public bool HasPin { get; init; }
}

/// <summary>Resoluciones de la empresa.</summary>
public sealed record CompanyResolutionsResponse
{
    [JsonPropertyName("resolutions")]
    public IReadOnlyList<ResolutionStatusResponse> Resolutions { get; init; } = [];
}

/// <summary>Una resolución con su consecutivo: cuál es el próximo número y cuántos quedan. Sin la clave técnica.</summary>
public sealed record ResolutionStatusResponse
{
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    /// <summary><c>invoice</c>, <c>credit_note</c> o <c>debit_note</c>; nulo para los demás tipos de documento.</summary>
    [JsonPropertyName("kind")]
    public string? Kind { get; init; }

    [JsonPropertyName("type_document_id")]
    public int TypeDocumentId { get; init; }

    [JsonPropertyName("type_environment_id")]
    public int TypeEnvironmentId { get; init; }

    [JsonPropertyName("prefix")]
    public string? Prefix { get; init; }

    [JsonPropertyName("resolution")]
    public string? Resolution { get; init; }

    [JsonPropertyName("resolution_date")]
    public DateOnly? ResolutionDate { get; init; }

    [JsonPropertyName("from")]
    public long From { get; init; }

    [JsonPropertyName("to")]
    public long To { get; init; }

    [JsonPropertyName("date_from")]
    public DateOnly? DateFrom { get; init; }

    [JsonPropertyName("date_to")]
    public DateOnly? DateTo { get; init; }

    [JsonPropertyName("is_active")]
    public bool IsActive { get; init; }

    /// <summary>Verdadero si tiene clave técnica guardada (la factura la necesita para el CUFE); la clave no se devuelve.</summary>
    [JsonPropertyName("has_technical_key")]
    public bool HasTechnicalKey { get; init; }

    /// <summary>Número que tomará el próximo documento; el inicio del rango si todavía no se emitió ninguno.</summary>
    [JsonPropertyName("next_number")]
    public long NextNumber { get; init; }

    /// <summary>Números que quedan por usar en el rango (0 si se agotó).</summary>
    [JsonPropertyName("remaining")]
    public long Remaining { get; init; }

    /// <summary>Días del calendario de Colombia hasta el fin de la vigencia (negativos si ya pasó); nulo sin vigencia.</summary>
    [JsonPropertyName("days_left")]
    public int? DaysLeft { get; init; }
}

/// <summary>Lo que el asistente de paso a producción necesita saber de la empresa, en verdadero o falso.</summary>
public sealed record CompanySetupStatusResponse
{
    /// <summary>Los datos obligatorios de la empresa están llenos (razón social, correo, teléfono, dirección, matrícula y una responsabilidad).</summary>
    [JsonPropertyName("company_complete")]
    public bool CompanyComplete { get; init; }

    /// <summary>Hay un certificado activo y le queda al menos un día (el día en que vence ya no sirve para firmar).</summary>
    [JsonPropertyName("certificate_valid")]
    public bool CertificateValid { get; init; }

    /// <summary>Hay software de factura en el ambiente actual de la factura.</summary>
    [JsonPropertyName("software_configured")]
    public bool SoftwareConfigured { get; init; }

    /// <summary>Ese software tiene el <c>test_set_id</c> del set de pruebas.</summary>
    [JsonPropertyName("test_set_id_configured")]
    public bool TestSetIdConfigured { get; init; }

    /// <summary>
    /// Verdadero si la DIAN validó un documento del set de pruebas o respondió que el set ya está aceptado («Set de
    /// prueba con identificador … se encuentra Aceptado»), falso si respondió sin validar ninguno, nulo si no hay
    /// respuestas guardadas. La aceptación final del set la muestra el portal de la DIAN.
    /// </summary>
    [JsonPropertyName("test_set_accepted")]
    public bool? TestSetAccepted { get; init; }

    [JsonPropertyName("environments")]
    public IReadOnlyList<EnvironmentResponse> Environments { get; init; } = [];

    [JsonPropertyName("resolutions")]
    public IReadOnlyList<ResolutionReadiness> Resolutions { get; init; } = [];
}

/// <summary>Si hay una resolución con la que se puede emitir hoy un tipo de documento.</summary>
public sealed record ResolutionReadiness
{
    [JsonPropertyName("kind")]
    public string Kind { get; init; } = "";

    [JsonPropertyName("type_document_id")]
    public int TypeDocumentId { get; init; }

    /// <summary>
    /// Hay una resolución activa del tipo en el ambiente actual de la factura, vigente hoy y con números por usar; la de
    /// factura además tiene clave técnica y fechas de vigencia.
    /// </summary>
    [JsonPropertyName("valid")]
    public bool Valid { get; init; }
}
