using System.Security.Cryptography;
using System.Text;
using System.Xml;
using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Documents;
using ImpiFact.Application.Abstractions.Messaging;
using ImpiFact.Application.Abstractions.Pdf;
using ImpiFact.Application.Abstractions.Persistence;
using ImpiFact.Application.Abstractions.Protection;
using ImpiFact.Application.Features.Documents.Email;
using ImpiFact.Application.Features.Documents.Pdf;
using ImpiFact.Application.Features.Documents.Queries;
using ImpiFact.Dian.Soap;
using ImpiFact.Domain.Common;
using ImpiFact.Domain.Documents;
using Microsoft.Extensions.Logging;

namespace ImpiFact.Application.Features.Documents.Maintenance;

/// <summary>
/// Handler del mensaje <c>RegeneratePdf</c> en el Worker: genera de nuevo el PDF de un documento aceptado con
/// <see cref="DocumentPdfModelBuilder"/> y el mismo <see cref="IPdfRenderer"/> de <c>RenderPdf</c>, y lo guarda como una
/// revisión nueva del archivo (la descarga y el correo toman la más reciente). A diferencia de <c>RenderPdf</c>, no
/// encola el correo.
/// </summary>
/// <remarks>
/// Un mensaje repetido guarda otra revisión con el mismo contenido, que es inofensivo. Un documento que no existe, que la
/// DIAN no aceptó o al que le falta lo que el PDF necesita es una falla permanente.
/// </remarks>
internal sealed class RegeneratePdfOutboxHandler(
    DocumentPdfModelBuilder builder,
    IPdfRenderer renderer,
    IDocumentLookups lookups,
    IDocumentFiles files,
    IUnitOfWork unitOfWork,
    ILogger<RegeneratePdfOutboxHandler> logger) : IOutboxMessageHandler
{
    public string MessageType => OutboxMessageTypes.RegeneratePdf;

    public async Task HandleAsync(OutboxMessageContext message, CancellationToken ct)
    {
        var (companyId, document) = await MaintenanceDocuments.AcceptedAsync(lookups, message, message.GetPayload<RegeneratePdfPayload>().DocumentId, ct);
        DocumentPdfModel model;
        try
        {
            model = await builder.BuildAsync(companyId, document.Id, ct)
                ?? throw new PermanentOutboxFailureException($"Document {document.Id} does not exist.");
        }
        catch (PdfSourceException exception)
        {
            throw new PermanentOutboxFailureException(exception.Message, exception);
        }

        var pdf = await renderer.RenderAsync(model, ct);
        var revision = await lookups.LatestFileRevisionAsync(companyId, document.Id, DocumentFileKind.Pdf, ct) + 1;
        await files.AddAsync(new NewDocumentFile(companyId, document.Id, DocumentFileKind.Pdf, pdf, "application/pdf", revision), ct);
        await unitOfWork.SaveChangesAsync(ct);
        logger.LogInformation("The PDF of document {DocumentId} was regenerated as revision {Revision}.", document.Id, revision);
    }
}

/// <summary>
/// Handler del mensaje <c>RegenerateAttachedDocument</c> en el Worker: arma y firma de nuevo el AttachedDocument
/// (contenedor electrónico) con <see cref="AttachedDocumentFactory"/>, el XML firmado y el ApplicationResponse (respuesta
/// firmada de la DIAN) más recientes guardados, y el certificado activo de la empresa; lo guarda como una revisión nueva.
/// </summary>
/// <remarks>
/// Sin ApplicationResponse, sin XML firmado, sin certificado activo o con un contenedor que no se puede armar, la falla es
/// permanente: reintentar no lo arregla. El certificado se abre solo en memoria y se libera al firmar.
/// </remarks>
internal sealed class RegenerateAttachedDocumentOutboxHandler(
    IDocumentLookups lookups,
    IDocumentDelivery documents,
    IDocumentFileReads fileReads,
    IDocumentFiles files,
    IIssuerContextReads issuers,
    ICertificateStore certificates,
    IUnitOfWork unitOfWork,
    IClock clock,
    ILogger<RegenerateAttachedDocumentOutboxHandler> logger) : IOutboxMessageHandler
{
    private static readonly UTF8Encoding Utf8 = new(false);

    public string MessageType => OutboxMessageTypes.RegenerateAttachedDocument;

    public async Task HandleAsync(OutboxMessageContext message, CancellationToken ct)
    {
        var payload = message.GetPayload<RegenerateAttachedDocumentPayload>();
        var (companyId, reference) = await MaintenanceDocuments.AcceptedAsync(lookups, message, payload.DocumentId, ct);
        var document = await documents.FindForUpdateAsync(companyId, reference.Id, ct)
            ?? throw new PermanentOutboxFailureException($"Document {reference.Id} does not exist.");
        var signedXml = await fileReads.ReadLatestAsync(companyId, document.Id, DocumentFileKind.SignedXml, ct)
            ?? throw new PermanentOutboxFailureException($"Document {document.Id} has no signed XML.");
        var applicationResponse = await fileReads.ReadLatestAsync(companyId, document.Id, DocumentFileKind.ApplicationResponse, ct);
        if (applicationResponse is not { Length: > 0 })
        {
            throw new PermanentOutboxFailureException($"Document {document.Id} has no application response.");
        }

        var secretRef = (await issuers.GetAsync(companyId, document.DocumentTypeId, DocumentKind.Invoice, ct))?.Certificate?.SecretRef
            ?? throw new PermanentOutboxFailureException($"Company {companyId} has no active certificate.");

        string attachedDocument;
        using (var certificate = await certificates.LoadAsync(companyId, secretRef, ct))
        {
            // El CUFE sale del documento: el resultado guardado solo aporta el ApplicationResponse.
            var result = new DianResult(true, null, null, null, [], null, null, applicationResponse, null);
            try
            {
                attachedDocument = AttachedDocumentFactory.BuildSigned(document, Utf8.GetString(signedXml), result, certificate, clock.UtcNow);
            }
            catch (Exception exception) when (exception is ArgumentException or XmlException or CryptographicException)
            {
                throw new PermanentOutboxFailureException(
                    $"The attached document of document {document.Id} could not be built: {exception.GetType().Name}: {exception.Message}", exception);
            }
        }

        var revision = await lookups.LatestFileRevisionAsync(companyId, document.Id, DocumentFileKind.AttachedDocument, ct) + 1;
        await files.AddAsync(
            new NewDocumentFile(companyId, document.Id, DocumentFileKind.AttachedDocument, Utf8.GetBytes(attachedDocument), "application/xml", revision),
            ct);
        await unitOfWork.SaveChangesAsync(ct);
        logger.LogInformation("The attached document of document {DocumentId} was regenerated as revision {Revision}.", document.Id, revision);
    }
}

/// <summary>El documento aceptado de un mensaje de mantenimiento, o la falla permanente que corresponde.</summary>
internal static class MaintenanceDocuments
{
    public static async Task<(Guid CompanyId, DocumentRef Document)> AcceptedAsync(
        IDocumentLookups lookups, OutboxMessageContext message, Guid documentId, CancellationToken ct)
    {
        var companyId = message.CompanyId
            ?? throw new PermanentOutboxFailureException($"Outbox message {message.MessageId} has no company.");
        var document = await lookups.FindByIdAsync(companyId, documentId, ct)
            ?? throw new PermanentOutboxFailureException($"Document {documentId} does not exist.");
        if (!LegacyDocumentState.Validated.Contains(document.Status))
        {
            throw new PermanentOutboxFailureException($"Document {document.Id} is {document.Status}; only accepted documents are regenerated.");
        }

        return (companyId, document);
    }
}
