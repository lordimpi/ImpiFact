using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Documents;
using ImpiFact.Application.Abstractions.Handlers;
using ImpiFact.Application.Abstractions.Results;
using ImpiFact.Application.Features.Documents.Email;
using ImpiFact.Application.Features.Documents.Pdf;
using ImpiFact.Domain.Documents;

namespace ImpiFact.Application.Features.Documents.Queries.Download;

/// <summary>
/// <c>GET /api/v1/documents/{id}/files/{kind}</c>: la revisión más reciente de un archivo guardado del documento.
/// <paramref name="Kind"/> es <c>xml</c>, <c>pdf</c>, <c>attached-document</c> o <c>zip</c>. El archivo lleva el nombre con
/// que lo publicaba el PHP (<c>FES-{número}.xml</c>, <c>Attachment-{número}.xml</c>, <c>ZipAttachm-{número}.zip</c>…).
/// </summary>
public sealed record DownloadDocumentFileByIdQuery(Guid Id, string Kind);

/// <summary>Un archivo para entregar: el nombre con que se descarga, su tipo de contenido y sus bytes.</summary>
public sealed record DocumentFileContent(string FileName, string ContentType, byte[] Content);

/// <summary>Qué archivo de un documento se pide.</summary>
internal enum DownloadKind
{
    SignedXml,
    Pdf,
    AttachedDocument,
    EmailZip,
}

internal sealed class DownloadDocumentFileByIdHandler(IDocumentLookups lookups, IDocumentFileReads files, ITenantContext tenant)
    : IHandler<DownloadDocumentFileByIdQuery, DocumentFileContent>
{
    public async Task<Result<DocumentFileContent>> HandleAsync(DownloadDocumentFileByIdQuery request, CancellationToken ct)
    {
        if (tenant.CompanyId is not { } companyId || await lookups.FindByIdAsync(companyId, request.Id, ct) is not { } document)
        {
            return DocumentErrors.NotFound;
        }

        DownloadKind? kind = request.Kind switch
        {
            "xml" => DownloadKind.SignedXml,
            "pdf" => DownloadKind.Pdf,
            "attached-document" => DownloadKind.AttachedDocument,
            "zip" => DownloadKind.EmailZip,
            _ => null,
        };
        if (kind is null)
        {
            return DocumentErrors.FileNotFound;
        }

        return await DocumentFileDelivery.ReadAsync(files, companyId, document, kind.Value, ct) is { } content
            ? content
            : DocumentErrors.FileNotFound;
    }
}

/// <summary>Lee el archivo pedido de un documento y le pone el nombre con que lo entregaba el PHP.</summary>
internal static class DocumentFileDelivery
{
    public static async Task<DocumentFileContent?> ReadAsync(
        IDocumentFileReads files, Guid companyId, DocumentRef document, DownloadKind kind, CancellationToken ct)
    {
        var pdfName = PdfFileNames.For(document.DocumentTypeId, document.Prefix, document.Number);
        var tag = pdfName[..pdfName.IndexOf('-', StringComparison.Ordinal)];
        switch (kind)
        {
            case DownloadKind.SignedXml:
                return await ReadAsync(files, companyId, document.Id, DocumentFileKind.SignedXml, $"{tag}-{document.FullNumber}.xml", "application/xml", ct);
            case DownloadKind.Pdf:
                return await ReadAsync(files, companyId, document.Id, DocumentFileKind.Pdf, pdfName, "application/pdf", ct);
            case DownloadKind.AttachedDocument:
                return await ReadAsync(
                    files, companyId, document.Id, DocumentFileKind.AttachedDocument, $"Attachment-{document.FullNumber}.xml", "application/xml", ct);
            default:
                var attached = await files.ReadLatestAsync(companyId, document.Id, DocumentFileKind.AttachedDocument, ct);
                var pdf = await files.ReadLatestAsync(companyId, document.Id, DocumentFileKind.Pdf, ct);
                if (attached is null || pdf is null)
                {
                    return null;
                }

                // El mismo ZIP que va en el correo, con el nombre del AttachedDocument dentro.
                var request = DocumentEmailRequest.Parse(await files.ReadLatestAsync(companyId, document.Id, DocumentFileKind.RequestJson, ct));
                var sentZip = await files.ReadLatestAsync(companyId, document.Id, DocumentFileKind.SentZip, ct);
                var name = DocumentEmailComposer.AttachedDocumentName(request.AttachedDocumentNamePrefix, sentZip, document.FullNumber);
                var zip = DocumentEmailComposer.Attachment(name, attached, pdf);
                return new DocumentFileContent($"ZipAttachm-{document.FullNumber}.zip", zip.ContentType, zip.Content);
        }
    }

    private static async Task<DocumentFileContent?> ReadAsync(
        IDocumentFileReads files, Guid companyId, Guid documentId, DocumentFileKind kind, string fileName, string contentType, CancellationToken ct) =>
        await files.ReadLatestAsync(companyId, documentId, kind, ct) is { } content ? new DocumentFileContent(fileName, contentType, content) : null;
}
