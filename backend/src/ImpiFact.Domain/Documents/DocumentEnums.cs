namespace ImpiFact.Domain.Documents;

// Enumeraciones del módulo de documentos; todas se guardan como smallint. Donde el modelo de datos no fija
// los valores, se indica que son provisionales.

/// <summary>Estado del documento (Modelo de datos detallado, sección 4).</summary>
public enum DocumentStatus : short
{
    /// <summary>Solicitud validada y guardada, aún sin XML.</summary>
    Received = 1,

    /// <summary>XML generado y firmado, mensaje en la bandeja de salida.</summary>
    Queued = 2,

    /// <summary>Enviado a la DIAN, con <c>track_id</c> y sin respuesta.</summary>
    Sent = 3,

    Accepted = 4,

    AcceptedWithWarnings = 5,

    Rejected = 6,

    /// <summary>Sin confirmar: respuesta «En proceso» o sin respuesta; se vuelve a consultar.</summary>
    Unconfirmed = 7,

    /// <summary>Error técnico tras agotar los reintentos.</summary>
    Failed = 8,

    /// <summary>Emitido en contingencia, falta transmitir.</summary>
    ContingencyPending = 9,

    /// <summary>Vista previa: solo existe en memoria, nunca se guarda.</summary>
    Draft = 10,
}

/// <summary>Estado del envío del documento por correo. Valores provisionales: el modelo solo fija el 0 por defecto.</summary>
public enum EmailStatus : short
{
    NotSent = 0,
    Sent = 1,
    Failed = 2,
}

/// <summary>Tipo de archivo de un documento (<c>document_files.kind</c>).</summary>
public enum DocumentFileKind : short
{
    SignedXml = 1,
    AttachedDocument = 2,
    Pdf = 3,
    RequestJson = 4,
    DianResponse = 5,
    SentZip = 6,
    ApplicationResponse = 7,
}

/// <summary>
/// Dónde está guardado un archivo (<c>document_files.storage_provider</c>), con los almacenes de
/// <c>IBlobStorage</c> (Arquitectura detallada, sección 2). Valores provisionales: el modelo no los fija.
/// </summary>
public enum StorageProvider : short
{
    Local = 1,
    AzureBlob = 2,
    S3 = 3,
    Gcs = 4,
}

/// <summary>Operación del servicio web de la DIAN en un intento (<c>dian_submissions.operation</c>).</summary>
public enum DianOperation : short
{
    SendBillSync = 1,
    SendTestSetAsync = 2,
    GetStatus = 3,
    GetStatusZip = 4,
    SendEvent = 5,
}

/// <summary>
/// Tipo de referencia de un documento (<c>document_references.kind</c>), con los nombres de los elementos UBL
/// (Universal Business Language, el formato XML de la factura). Valores provisionales: el modelo no los fija.
/// </summary>
public enum DocumentReferenceKind : short
{
    BillingReference = 1,
    OrderReference = 2,
    DespatchDocumentReference = 3,
    ReceiptDocumentReference = 4,
    AdditionalDocumentReference = 5,
}

/// <summary>Estado de un evento RADIAN (<c>document_events.status</c>). Valores provisionales: el modelo no los fija.</summary>
public enum DocumentEventStatus : short
{
    Pending = 1,
    Sent = 2,
    Accepted = 3,
    Rejected = 4,
}
