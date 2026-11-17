using System.Text.RegularExpressions;
using FluentValidation;
using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Documents;
using ImpiFact.Application.Abstractions.Handlers;
using ImpiFact.Application.Abstractions.Results;
using ImpiFact.Application.Features.Documents.Email;
using ImpiFact.Application.Features.Documents.Pdf;
using ImpiFact.Domain.Documents;

namespace ImpiFact.Application.Features.Documents.Queries.Download;

/// <summary>
/// <c>GET download/{nit}/{file}</c> del PHP: un archivo guardado de un documento por el nombre con que lo publicaba el
/// PHP. El NIT (número de identificación tributaria) debe ser el de la empresa de la credencial; el PHP dejaba esta
/// ruta pública.
/// </summary>
/// <remarks>
/// Nombres que se reconocen, con <c>{número}</c> el prefijo y el número del documento (por ejemplo <c>SETP990000001</c>):
/// <c>FES-{número}.xml</c> y <c>FES-{número}.pdf</c> (factura; <c>NCS-</c> nota crédito, <c>NDS-</c> nota débito) para
/// el XML firmado y el PDF, <c>Attachment-{número}.xml</c> para el AttachedDocument (contenedor electrónico) y
/// <c>ZipAttachm-{número}.zip</c> para el ZIP del correo (AttachedDocument y PDF). Cualquier otro nombre, o un archivo
/// que no exista, responde 404 con el texto del PHP. Siempre se entrega la revisión más reciente.
/// </remarks>
public sealed record DownloadDocumentFileQuery(string Nit, string File);

/// <summary>
/// <c>GET /api/v1/documents/{id}/files/{kind}</c>: el mismo archivo por el id del documento. <paramref name="Kind"/> es
/// <c>xml</c>, <c>pdf</c>, <c>attached-document</c> o <c>zip</c>.
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

internal sealed partial class DownloadDocumentFileHandler(
    IDocumentReads reads, IDocumentLookups lookups, IDocumentFileReads files, ITenantContext tenant)
    : IHandler<DownloadDocumentFileQuery, DocumentFileContent>
{
    private static readonly int[] InvoiceTypes = [1];
    private static readonly int[] CreditNoteTypes = [4];
    private static readonly int[] DebitNoteTypes = [5];
    private static readonly int[] AllTypes = [1, 4, 5];

    public async Task<Result<DocumentFileContent>> HandleAsync(DownloadDocumentFileQuery request, CancellationToken ct)
    {
        var own = await DocumentAccess.OwnCompanyAsync(reads, tenant, request.Nit, ct);
        if (!own.IsSuccess)
        {
            return own.Error;
        }

        var notFound = DocumentErrors.FileNotFoundNamed(request.File);
        if (Parse(request.File) is not { } parsed
            || await lookups.FindByFullNumberAsync(own.Value.Id, parsed.FullNumber, parsed.Types, ct) is not { } document)
        {
            return notFound;
        }

        var content = await DocumentFileDelivery.ReadAsync(files, own.Value.Id, document, parsed.Kind, ct);
        return content is null ? notFound : content with { FileName = request.File };
    }

    /// <summary>Tipo de archivo, número completo y tipos de documento posibles de un nombre del PHP; nulo si no se reconoce.</summary>
    private static (DownloadKind Kind, string FullNumber, int[] Types)? Parse(string file)
    {
        var match = FileNamePattern().Match(file);
        if (!match.Success)
        {
            return null;
        }

        var fullNumber = match.Groups["number"].Value;
        return (match.Groups["tag"].Value, match.Groups["ext"].Value) switch
        {
            ("FES", "xml") => (DownloadKind.SignedXml, fullNumber, InvoiceTypes),
            ("NCS", "xml") => (DownloadKind.SignedXml, fullNumber, CreditNoteTypes),
            ("NDS", "xml") => (DownloadKind.SignedXml, fullNumber, DebitNoteTypes),
            ("FES", "pdf") => (DownloadKind.Pdf, fullNumber, InvoiceTypes),
            ("NCS", "pdf") => (DownloadKind.Pdf, fullNumber, CreditNoteTypes),
            ("NDS", "pdf") => (DownloadKind.Pdf, fullNumber, DebitNoteTypes),
            ("Attachment", "xml") => (DownloadKind.AttachedDocument, fullNumber, AllTypes),
            ("ZipAttachm", "zip") => (DownloadKind.EmailZip, fullNumber, AllTypes),
            _ => null,
        };
    }

    [GeneratedRegex("^(?<tag>FES|NCS|NDS|Attachment|ZipAttachm)-(?<number>[A-Za-z0-9]{1,40})\\.(?<ext>xml|pdf|zip)$", RegexOptions.CultureInvariant)]
    private static partial Regex FileNamePattern();
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

internal sealed class DownloadDocumentFileQueryValidator : AbstractValidator<DownloadDocumentFileQuery>
{
    public DownloadDocumentFileQueryValidator()
    {
        RuleFor(query => query.File)
            .Must(file => !string.IsNullOrWhiteSpace(file)).WithMessage(QueryMessages.Required("file"))
            .OverridePropertyName("file");
    }
}
