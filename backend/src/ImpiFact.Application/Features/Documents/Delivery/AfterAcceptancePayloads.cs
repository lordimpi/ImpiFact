namespace ImpiFact.Application.Features.Documents.Delivery;

/// <summary>
/// Mensaje <c>RenderPdf</c>: generar la representación gráfica (el PDF que ve el comprador) de un documento que la DIAN
/// ya aceptó. Se encola al aceptar el documento; al terminar, el handler del PDF encola <c>SendEmail</c>.
/// </summary>
public sealed record RenderPdfPayload(Guid DocumentId);

/// <summary>
/// Mensaje <c>SendEmail</c>: enviar al comprador el correo con el ZIP que lleva el AttachedDocument (el contenedor con el
/// documento firmado y la respuesta de la DIAN) y el PDF. Lo encola el handler del PDF cuando el PDF ya está guardado.
/// </summary>
public sealed record SendEmailPayload(Guid DocumentId);
