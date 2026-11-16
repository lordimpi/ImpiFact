namespace ImpiFact.Application.Features.Documents.Pdf;

/// <summary>
/// Falta o está dañado algo de lo guardado que el PDF necesita (por ejemplo, la solicitud JSON con que se emitió el
/// documento): volver a intentarlo no lo arregla.
/// </summary>
public sealed class PdfSourceException(string message) : Exception(message);
