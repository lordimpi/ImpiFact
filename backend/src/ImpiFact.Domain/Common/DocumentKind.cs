namespace ImpiFact.Domain.Common;

/// <summary>
/// Familia de documentos electrónicos (Modelo de datos detallado, sección 3.3). Separa el ambiente, el software
/// y las cuotas de plan de cada familia; se guarda como <c>smallint</c>.
/// </summary>
public enum DocumentKind : short
{
    /// <summary>Factura electrónica y sus notas crédito y débito.</summary>
    Invoice = 1,

    /// <summary>Nómina electrónica.</summary>
    Payroll = 2,

    /// <summary>Documento equivalente electrónico.</summary>
    EquivalentDocument = 3,

    /// <summary>Documento soporte en adquisiciones a no obligados a facturar.</summary>
    SupportDocument = 4,

    /// <summary>Eventos RADIAN (registro de la factura electrónica como título valor).</summary>
    Events = 5,
}

/// <summary>
/// Quién originó una acción: el mismo orden que <c>ActorType</c> de la capa de aplicación, para que el valor se
/// copie sin traducir. Se guarda como <c>smallint</c> en <c>documents.created_by_type</c> y <c>audit_logs.actor_type</c>.
/// </summary>
public enum ActorKind : short
{
    Anonymous = 0,
    User = 1,
    ApiKey = 2,
    Platform = 3,
    System = 4,
}
