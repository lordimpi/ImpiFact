using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Billing;
using ImpiFact.Application.Abstractions.Documents;
using ImpiFact.Application.Abstractions.Handlers;
using ImpiFact.Application.Abstractions.Messaging;
using ImpiFact.Application.Abstractions.Protection;
using ImpiFact.Application.Abstractions.Results;
using ImpiFact.Application.Features.Documents.Totals;
using ImpiFact.Dian.Signing;
using ImpiFact.Dian.Ubl;
using ImpiFact.Domain.Common;
using ImpiFact.Domain.Documents;

namespace ImpiFact.Application.Features.Documents.Issue;

/// <summary>Puertos que usa la emisión; los tres handlers los reciben por inyección y los pasan juntos.</summary>
internal sealed record IssuePorts(
    ITenantContext Tenant,
    IIssuerContextReads Issuers,
    IDocumentCatalogs Catalogs,
    INumberingSequences Numbering,
    IDocumentStore Store,
    IIdempotencyKeys IdempotencyKeys,
    IDocumentFiles Files,
    IOutbox Outbox,
    ICertificateStore Certificates,
    ISecretProtector Secrets,
    IDianQrUrls QrUrls,
    IPlanGate Plans,
    IClock Clock);

/// <summary>Mensaje <c>SendDocumentToDian</c> de la bandeja de salida (supuesto B7: el envío lo hará el Worker).</summary>
/// <param name="TestSetId">Set de pruebas de habilitación cuando el documento llegó por la ruta <c>/{testSetId}</c>.</param>
public sealed record SendDocumentToDianPayload(Guid DocumentId, string? TestSetId);

/// <summary>
/// Emisión de una factura o nota de punta a punta (Arquitectura detallada, sección 7, pasos 1 a 6, con el supuesto
/// B7 de la fase 3: todo dentro de la misma solicitud y de la transacción del decorador). En orden: idempotencia,
/// empresa activa, resolución del tipo, ambiente y prefijo, certificado vigente, software, plan de la empresa
/// (<see cref="IPlanGate"/>), catálogos, totales con las reglas de la DIAN (antes de tomar el número), en las notas la
/// factura referenciada aceptada, número (el del cliente o el siguiente del consecutivo, revisado con
/// <see cref="NumberingRange.Check"/>), XML UBL (Universal Business Language, el formato XML de la DIAN) con su
/// CUFE o CUDE (códigos únicos de factura y de documento electrónico), firma, archivos, documento en
/// <see cref="DocumentStatus.Queued"/> y mensaje <c>SendDocumentToDian</c>. El caso de uso nunca guarda: lo hace el
/// decorador de transacción, que revierte todo (también el número) si algo falla.
/// </summary>
/// <remarks>
/// Repetir una solicitud ya atendida (misma <c>Idempotency-Key</c>, o mismo número con el mismo cuerpo) responde con
/// el documento existente y no emite otro. El PIN del software, la contraseña y los bytes del certificado nunca se
/// registran ni salen en la respuesta.
/// </remarks>
internal abstract class IssueDocumentHandler<TCommand>(IssuePorts ports, IssueSpec spec) : IHandler<TCommand, IssueDocumentResponse>
    where TCommand : IIssueDocumentCommand
{
    /// <summary>Largo de la columna <c>idempotency_keys.key</c>.</summary>
    public const int MaxIdempotencyKeyLength = 100;

    /// <summary>Números ocupados que el consecutivo salta antes de rendirse (ver <see cref="IssueErrors.SequenceBlocked"/>).</summary>
    public const int MaxSkippedNumbers = 50;

    /// <summary>Tiempo que una llave de idempotencia responde por su documento (Arquitectura detallada, sección 8).</summary>
    public static readonly TimeSpan IdempotencyWindow = TimeSpan.FromHours(24);

    private static readonly UTF8Encoding Utf8 = new(false);

    public async Task<Result<IssueDocumentResponse>> HandleAsync(TCommand request, CancellationToken ct)
    {
        var companyId = ports.Tenant.CompanyId ?? throw new InvalidOperationException("The tenant decorator guarantees a company.");
        var body = request.Body;
        var key = string.IsNullOrWhiteSpace(request.IdempotencyKey) ? null : request.IdempotencyKey.Trim();
        if (key is { Length: > MaxIdempotencyKeyLength })
        {
            return IssueErrors.IdempotencyKeyTooLong;
        }

        var storedRequest = IssueDocumentJson.ForStorage(request.RawBody);
        var requestHash = Convert.ToHexStringLower(SHA256.HashData(storedRequest));

        var issuer = await ports.Issuers.GetAsync(companyId, spec.DocumentTypeId, DocumentKind.Invoice, ct);
        if (issuer is null || !issuer.Company.IsActive)
        {
            return IssueErrors.CompanyInactive;
        }

        if (key is not null && await ports.IdempotencyKeys.FindAsync(companyId, key, ct) is { } previous)
        {
            return previous.RequestSha256 == requestHash && previous.DocumentId is { } previousId
                && await ports.Store.FindByIdAsync(companyId, previousId, ct) is { } existing
                ? await ReplayAsync(existing, issuer, ct)
                : IssueErrors.IdempotencyKeyReused;
        }

        var resolution = IssueChecks.SelectResolution(spec, issuer, body);
        if (!resolution.IsSuccess)
        {
            return resolution.Error;
        }

        var setup = CheckSetup(issuer);
        if (!setup.IsSuccess)
        {
            return setup.Error;
        }

        // Como el PHP: el plan se revisa justo después del certificado, y el set de pruebas (testSetStore) no lo revisa.
        // Facturas y notas (tipos 1 a 5) son la familia «factura» de las cuotas del plan.
        if (request.TestSetId is null && await ports.Plans.CheckIssueAsync(companyId, DocumentKind.Invoice, ct) is { } refused)
        {
            return refused;
        }

        var catalogs = await IssueChecks.LoadCatalogsAsync(spec, ports.Catalogs, issuer, body, ct);
        if (!catalogs.IsSuccess)
        {
            return catalogs.Error;
        }

        var totals = DianTotalsValidator.Validate(LegacyDocumentMapper.MapTotalsDocument(spec, body));
        if (!totals.IsSuccess)
        {
            return totals.Error;
        }

        var now = ports.Clock.UtcNow;
        var (issueDate, issueTime) = IssueChecks.IssueMoment(body, now);

        var reference = await FindReferenceAsync(companyId, body, request.TestSetId, ct);
        if (!reference.IsSuccess)
        {
            return reference.Error;
        }

        var number = await TakeNumberAsync(companyId, issuer, resolution.Value, body, issueDate, requestHash, ct);
        if (!number.IsSuccess)
        {
            return number.Error;
        }

        if (number.Value.Existing is { } repeated)
        {
            return await ReplayAsync(repeated, issuer, ct);
        }

        var frame = new IssueFrame(
            spec,
            issuer,
            resolution.Value,
            number.Value.Number,
            issueDate,
            issueTime,
            ports.Secrets.Unprotect(SecretPurposes.SoftwarePin, issuer.Software!.PinCipher),
            ports.QrUrls.ForEnvironment(issuer.TypeEnvironmentId));
        var mapper = new LegacyDocumentMapper(catalogs.Value);
        var (unsignedXml, uuid) = spec.Kind == IssueKind.Invoice
            ? Unpack(InvoiceUblBuilder.Build(mapper.MapInvoice(frame, body)))
            : Unpack(NoteUblBuilder.Build(mapper.MapNote(frame, body)));

        string signedXml;
        using (var certificate = await ports.Certificates.LoadAsync(companyId, issuer.Certificate!.SecretRef, ct))
        {
            signedXml = XmlSigner.Sign(unsignedXml, certificate, now);
        }

        var graph = DocumentGraphBuilder.Build(frame, body, uuid, reference.Value, ports.Tenant);
        var document = graph.Document;
        ports.Store.Add(graph);
        await ports.Files.AddAsync(new NewDocumentFile(companyId, document.Id, DocumentFileKind.RequestJson, storedRequest, "application/json"), ct);
        await ports.Files.AddAsync(new NewDocumentFile(companyId, document.Id, DocumentFileKind.SignedXml, Utf8.GetBytes(signedXml), "application/xml"), ct);
        document.MoveTo(DocumentStatus.Queued);
        await ports.Outbox.EnqueueAsync(
            OutboxMessageTypes.SendDocumentToDian, new SendDocumentToDianPayload(document.Id, request.TestSetId), companyId, null, ct);
        if (key is not null)
        {
            ports.IdempotencyKeys.Add(companyId, key, requestHash, document.Id, now + IdempotencyWindow);
        }

        var typeName = catalogs.Value[new CatalogKey(DianCatalog.DocumentType, spec.DocumentTypeId)].Name;
        var stored = new StoredDocument(
            document.Id, document.DocumentTypeId, document.TypeEnvironmentId, document.ResolutionId, document.Prefix, document.Number,
            document.FullNumber, document.Status, uuid, document.UuidAlgorithm, requestHash);
        return Respond(typeName, stored, issuer, signedXml, unsignedXml, replayed: false);
    }

    /// <summary>
    /// Supuesto B8 y la regla de la fase 4: la nota se enlaza con el documento de la empresa que tenga el CUFE de
    /// <c>billing_reference.uuid</c>, si existe, y ese documento debe estar aceptado por la DIAN
    /// (<see cref="DocumentStatus.Accepted"/> o <see cref="DocumentStatus.AcceptedWithWarnings"/>). Un CUFE que la
    /// empresa no tiene (una factura emitida fuera de ImpiFact) se acepta sin enlace, como en el PHP, que no buscaba la
    /// factura. El set de pruebas no exige la aceptación: sus facturas se aceptan de forma asíncrona y el PHP no lo
    /// revisaba.
    /// </summary>
    /// <returns>El id del documento referenciado, o nulo si la solicitud no es una nota o no lo encuentra.</returns>
    private async Task<Result<Guid?>> FindReferenceAsync(Guid companyId, IssueDocumentBody body, string? testSetId, CancellationToken ct)
    {
        if (!spec.IsNote || body.BillingReference?.Uuid is not { } referenced
            || await ports.Store.FindIdByCufeAsync(companyId, PhpText.Clean(referenced.Text), ct) is not { } referenceId)
        {
            return Result<Guid?>.Ok(null);
        }

        if (testSetId is not null)
        {
            return referenceId;
        }

        var original = await ports.Store.FindByIdAsync(companyId, referenceId, ct);
        return original?.Status is DocumentStatus.Accepted or DocumentStatus.AcceptedWithWarnings
            ? referenceId
            : IssueErrors.ReferenceNotAccepted;
    }

    private static (string Xml, string Uuid) Unpack(InvoiceUblResult result) => (result.Xml, result.Cufe);

    private static (string Xml, string Uuid) Unpack(NoteUblResult result) => (result.Xml, result.Cude);

    private Result<bool> CheckSetup(IssuerContext issuer)
    {
        if (issuer.Certificate is not { } certificate)
        {
            return IssueErrors.CertificateMissing;
        }

        // Como el PHP: vence el mismo día de su fecha de vencimiento (días restantes 0 o menos).
        if (CertificateDaysLeft(certificate) <= 0)
        {
            return IssueErrors.CertificateExpired;
        }

        return issuer.Software is null ? IssueErrors.SoftwareMissing : true;
    }

    /// <summary>Prefijo de los nombres de archivo del PHP por tipo de documento (inventario de reglas 3.8).</summary>
    private static string LegacyFileTag(IssueKind kind) => kind switch
    {
        IssueKind.CreditNote => "NCS",
        IssueKind.DebitNote => "NDS",
        _ => "FES",
    };

    private int CertificateDaysLeft(IssuerCertificate certificate) =>
        ColombiaTime.DaysLeft(ColombiaTime.Today(certificate.NotAfter), ports.Clock.UtcNow);

    /// <summary>
    /// Supuesto B2. Con <c>number</c>: debe estar en el rango y la vigencia (se revisa antes de reservarlo, así un
    /// número absurdo nunca mueve el consecutivo) y libre; si ya lo usa un documento emitido con la misma solicitud, se
    /// devuelve ese documento. Sin <c>number</c>: el siguiente del consecutivo, también revisado; si el consecutivo cae
    /// en números ya usados (documentos con números por encima de él, u otra resolución con el mismo prefijo), los
    /// salta, porque ya están ocupados y saltarlos no deja huecos nuevos.
    /// </summary>
    private async Task<Result<TakenNumber>> TakeNumberAsync(
        Guid companyId, IssuerContext issuer, IssuerResolution resolution, IssueDocumentBody body, DateOnly issueDate, string requestHash,
        CancellationToken ct)
    {
        var range = new NumberingRange(resolution.From, resolution.To, resolution.ValidFrom, resolution.ValidTo, resolution.IsActive);
        var scopeKey = NumberingScope.ForResolution(resolution.Id);
        var prefix = resolution.Prefix ?? "";

        if (body.Number is { } requested && requested.Text.Length > 0)
        {
            var number = requested.Integer;
            if (Describe(range.Check(number, issueDate), resolution) is { } invalid)
            {
                return invalid;
            }

            var key = new DocumentNumberKey(spec.DocumentTypeId, issuer.TypeEnvironmentId, prefix, number);
            switch (await ports.Numbering.ReserveAsync(companyId, scopeKey, key, ct))
            {
                case NumberReservation.SequenceNotFound:
                    return IssueErrors.SequenceMissing;
                case NumberReservation.AlreadyUsed:
                    var existing = await ports.Store.FindByNumberAsync(companyId, key, ct);
                    return existing is not null && existing.RequestSha256 == requestHash
                        ? new TakenNumber(number, existing)
                        : IssueErrors.NumberInUse(prefix + number.ToString(CultureInfo.InvariantCulture));
                default:
                    return new TakenNumber(number, null);
            }
        }

        for (var attempt = 0; attempt <= MaxSkippedNumbers; attempt++)
        {
            if (await ports.Numbering.TakeNextAsync(companyId, scopeKey, ct) is not { } next)
            {
                return IssueErrors.SequenceMissing;
            }

            var outcome = range.Check(next, issueDate);
            if (outcome == NumberingCheck.OutOfRange)
            {
                return IssueErrors.RangeExhausted;
            }

            if (Describe(outcome, resolution) is { } invalid)
            {
                return invalid;
            }

            var key = new DocumentNumberKey(spec.DocumentTypeId, issuer.TypeEnvironmentId, prefix, next);
            if (await ports.Store.FindByNumberAsync(companyId, key, ct) is null)
            {
                return new TakenNumber(next, null);
            }
        }

        return IssueErrors.SequenceBlocked(prefix);
    }

    /// <summary>Traduce el resultado de <see cref="NumberingRange.Check"/> a los mensajes de validación del PHP.</summary>
    private static Error? Describe(NumberingCheck check, IssuerResolution resolution) => check switch
    {
        NumberingCheck.Valid => null,
        NumberingCheck.Inactive => Error.Validation([new FieldError("type_document_id", IssueMessages.ResolutionNotConfigured)]),
        NumberingCheck.OutOfRange => Error.Validation([new FieldError("number", IssueMessages.Between("number", resolution.From, resolution.To))]),
        NumberingCheck.NotYetValid => Error.Validation([new FieldError("date", IssueMessages.AfterOrEqual("date", ColombiaTime.Format(resolution.ValidFrom!.Value)))]),
        NumberingCheck.Expired => Error.Validation([new FieldError("date", IssueMessages.BeforeOrEqual("date", ColombiaTime.Format(resolution.ValidTo!.Value)))]),
        _ => throw new ArgumentOutOfRangeException(nameof(check), check, "Unknown numbering check."),
    };

    /// <summary>Responde por un documento ya emitido con el XML firmado que se guardó.</summary>
    private async Task<Result<IssueDocumentResponse>> ReplayAsync(StoredDocument existing, IssuerContext issuer, CancellationToken ct)
    {
        var signed = await ports.Store.ReadFileAsync(issuer.Company.Id, existing.Id, DocumentFileKind.SignedXml, ct);
        var types = await ports.Catalogs.FindAsync([new CatalogKey(DianCatalog.DocumentType, existing.DocumentTypeId)], ct);
        var typeName = types.Values.FirstOrDefault()?.Name ?? "";
        return Respond(typeName, existing, issuer, signed is null ? null : Utf8.GetString(signed), unsignedXml: null, replayed: true);
    }

    private IssueDocumentResponse Respond(
        string typeName, StoredDocument document, IssuerContext issuer, string? signedXml, string? unsignedXml, bool replayed)
    {
        var uuid = document.Cufe ?? "";
        var resolution = issuer.Resolutions.FirstOrDefault(row => row.Id == document.ResolutionId);
        return new IssueDocumentResponse
        {
            Success = spec.IsNote ? null : true,
            Message = $"{typeName} #{document.FullNumber} generada con éxito",
            UrlInvoiceXml = $"{LegacyFileTag(spec.Kind)}-{document.FullNumber}.xml",
            UrlInvoicePdf = $"{LegacyFileTag(spec.Kind)}-{document.FullNumber}.pdf",
            UrlInvoiceAttached = $"Attachment-{document.FullNumber}.xml",
            Cufe = spec.IsNote ? null : uuid,
            Cude = spec.IsNote ? uuid : null,
            QrStr = ports.QrUrls.ForEnvironment(document.TypeEnvironmentId) + uuid,
            CertificateDaysLeft = issuer.Certificate is { } certificate ? CertificateDaysLeft(certificate) : 0,
            ResolutionDaysLeft = !spec.IsNote && resolution?.ValidTo is { } validTo ? ColombiaTime.DaysLeft(validTo, ports.Clock.UtcNow) : null,
            InvoiceXml = signedXml is null ? null : Convert.ToBase64String(Utf8.GetBytes(signedXml)),
            UnsignedInvoiceXml = unsignedXml is null ? null : Convert.ToBase64String(Utf8.GetBytes(unsignedXml)),
            Document = new IssuedDocumentInfo
            {
                Id = document.Id,
                DocumentTypeId = document.DocumentTypeId,
                TypeEnvironmentId = document.TypeEnvironmentId,
                Prefix = document.Prefix,
                Number = document.Number,
                FullNumber = document.FullNumber,
                Status = document.Status.ToString(),
                Uuid = document.Cufe,
                UuidAlgorithm = document.UuidAlgorithm,
                Replayed = replayed,
            },
        };
    }

    private sealed record TakenNumber(long Number, StoredDocument? Existing);
}

/// <summary>
/// Revisiones de la emisión que no toman número ni guardan nada: las usan la emisión y la vista previa del PDF
/// (<c>POST /api/v1/documents/invoices/preview</c>), así las dos rechazan lo mismo con los mismos mensajes.
/// </summary>
internal static class IssueChecks
{
    /// <summary>
    /// Resolución del tipo en el ambiente actual (la lectura ya filtró ambos), activa y con el prefijo que traiga la
    /// solicitud. Como el PHP, el número de resolución solo sirve para elegir entre varias, y es obligatorio cuando
    /// quedan varias. Una resolución de factura sin clave técnica o sin vigencia no sirve para emitir.
    /// </summary>
    public static Result<IssuerResolution> SelectResolution(IssueSpec spec, IssuerContext issuer, IssueDocumentBody body)
    {
        var candidates = issuer.Resolutions.Where(resolution => resolution.IsActive).ToList();
        if (body.Prefix is { } prefix && prefix.Text.Length > 0)
        {
            candidates = candidates.Where(resolution => (resolution.Prefix ?? "") == prefix.Text).ToList();
        }

        if (candidates.Count > 1 && body.ResolutionNumber is { } number && number.Text.Length > 0)
        {
            candidates = candidates.Where(resolution => (resolution.ResolutionNumber ?? "") == number.Text).ToList();
        }

        if (candidates.Count == 0
            || (spec.Kind == IssueKind.Invoice && candidates[0] is { TechnicalKey: null } or { ValidFrom: null } or { ValidTo: null }))
        {
            return Error.Validation([new FieldError("type_document_id", IssueMessages.ResolutionNotConfigured)]);
        }

        return candidates.Count == 1
            ? candidates[0]
            : Error.Validation([new FieldError("resolution_number", IssueMessages.Required("resolution_number"))]);
    }

    /// <summary>
    /// Catálogos que la solicitud y la empresa necesitan; un id que no existe es un error de validación del campo que lo
    /// trajo, con los valores fijos que exigen algunos impuestos.
    /// </summary>
    public static async Task<Result<IReadOnlyDictionary<CatalogKey, CatalogItem>>> LoadCatalogsAsync(
        IssueSpec spec, IDocumentCatalogs catalogs, IssuerContext issuer, IssueDocumentBody body, CancellationToken ct)
    {
        var needs = CatalogNeeds.Collect(spec, body, issuer);
        var found = await catalogs.FindAsync(needs.Select(need => need.Key).Distinct().ToList(), ct);
        var errors = needs
            .Where(need => !found.ContainsKey(need.Key))
            .Select(need => need.Field)
            .Distinct(StringComparer.Ordinal)
            .Select(field => new FieldError(field, IssueMessages.Invalid(field)))
            .Concat(CatalogNeeds.FixedValueGaps(spec, body))
            .Concat(CatalogNeeds.ExchangeRateGaps(spec, body, found, issuer))
            .ToList();
        return errors.Count > 0 ? Error.Validation(errors) : Result<IReadOnlyDictionary<CatalogKey, CatalogItem>>.Ok(found);
    }

    /// <summary>Fecha y hora de emisión en Colombia: las de la solicitud (<c>date</c>, <c>time</c>) o las actuales.</summary>
    public static (DateOnly Date, TimeOnly Time) IssueMoment(IssueDocumentBody body, DateTimeOffset now) =>
        (body.Date is { } date && date.Text.Length > 0 ? LegacyDocumentMapper.Date(date) : ColombiaTime.Today(now),
            body.Time is { } time && time.Text.Length > 0
                ? TimeOnly.ParseExact(time.Text, "HH:mm:ss", CultureInfo.InvariantCulture)
                : ColombiaTime.Now(now));
}

internal sealed class IssueInvoiceHandler(
    ITenantContext tenant, IIssuerContextReads issuers, IDocumentCatalogs catalogs, INumberingSequences numbering, IDocumentStore store,
    IIdempotencyKeys idempotencyKeys, IDocumentFiles files, IOutbox outbox, ICertificateStore certificates, ISecretProtector secrets,
    IDianQrUrls qrUrls, IPlanGate plans, IClock clock)
    : IssueDocumentHandler<IssueInvoiceCommand>(
        new IssuePorts(tenant, issuers, catalogs, numbering, store, idempotencyKeys, files, outbox, certificates, secrets, qrUrls, plans, clock),
        IssueSpec.Invoice);

internal sealed class IssueAiuInvoiceHandler(
    ITenantContext tenant, IIssuerContextReads issuers, IDocumentCatalogs catalogs, INumberingSequences numbering, IDocumentStore store,
    IIdempotencyKeys idempotencyKeys, IDocumentFiles files, IOutbox outbox, ICertificateStore certificates, ISecretProtector secrets,
    IDianQrUrls qrUrls, IPlanGate plans, IClock clock)
    : IssueDocumentHandler<IssueAiuInvoiceCommand>(
        new IssuePorts(tenant, issuers, catalogs, numbering, store, idempotencyKeys, files, outbox, certificates, secrets, qrUrls, plans, clock),
        IssueSpec.AiuInvoice);

internal sealed class IssueMandateInvoiceHandler(
    ITenantContext tenant, IIssuerContextReads issuers, IDocumentCatalogs catalogs, INumberingSequences numbering, IDocumentStore store,
    IIdempotencyKeys idempotencyKeys, IDocumentFiles files, IOutbox outbox, ICertificateStore certificates, ISecretProtector secrets,
    IDianQrUrls qrUrls, IPlanGate plans, IClock clock)
    : IssueDocumentHandler<IssueMandateInvoiceCommand>(
        new IssuePorts(tenant, issuers, catalogs, numbering, store, idempotencyKeys, files, outbox, certificates, secrets, qrUrls, plans, clock),
        IssueSpec.MandateInvoice);

internal sealed class IssueTransportInvoiceHandler(
    ITenantContext tenant, IIssuerContextReads issuers, IDocumentCatalogs catalogs, INumberingSequences numbering, IDocumentStore store,
    IIdempotencyKeys idempotencyKeys, IDocumentFiles files, IOutbox outbox, ICertificateStore certificates, ISecretProtector secrets,
    IDianQrUrls qrUrls, IPlanGate plans, IClock clock)
    : IssueDocumentHandler<IssueTransportInvoiceCommand>(
        new IssuePorts(tenant, issuers, catalogs, numbering, store, idempotencyKeys, files, outbox, certificates, secrets, qrUrls, plans, clock),
        IssueSpec.TransportInvoice);

internal sealed class IssueExportInvoiceHandler(
    ITenantContext tenant, IIssuerContextReads issuers, IDocumentCatalogs catalogs, INumberingSequences numbering, IDocumentStore store,
    IIdempotencyKeys idempotencyKeys, IDocumentFiles files, IOutbox outbox, ICertificateStore certificates, ISecretProtector secrets,
    IDianQrUrls qrUrls, IPlanGate plans, IClock clock)
    : IssueDocumentHandler<IssueExportInvoiceCommand>(
        new IssuePorts(tenant, issuers, catalogs, numbering, store, idempotencyKeys, files, outbox, certificates, secrets, qrUrls, plans, clock),
        IssueSpec.ExportInvoice);

internal sealed class IssueCreditNoteHandler(
    ITenantContext tenant, IIssuerContextReads issuers, IDocumentCatalogs catalogs, INumberingSequences numbering, IDocumentStore store,
    IIdempotencyKeys idempotencyKeys, IDocumentFiles files, IOutbox outbox, ICertificateStore certificates, ISecretProtector secrets,
    IDianQrUrls qrUrls, IPlanGate plans, IClock clock)
    : IssueDocumentHandler<IssueCreditNoteCommand>(
        new IssuePorts(tenant, issuers, catalogs, numbering, store, idempotencyKeys, files, outbox, certificates, secrets, qrUrls, plans, clock),
        IssueSpec.CreditNote);

internal sealed class IssueDebitNoteHandler(
    ITenantContext tenant, IIssuerContextReads issuers, IDocumentCatalogs catalogs, INumberingSequences numbering, IDocumentStore store,
    IIdempotencyKeys idempotencyKeys, IDocumentFiles files, IOutbox outbox, ICertificateStore certificates, ISecretProtector secrets,
    IDianQrUrls qrUrls, IPlanGate plans, IClock clock)
    : IssueDocumentHandler<IssueDebitNoteCommand>(
        new IssuePorts(tenant, issuers, catalogs, numbering, store, idempotencyKeys, files, outbox, certificates, secrets, qrUrls, plans, clock),
        IssueSpec.DebitNote);
