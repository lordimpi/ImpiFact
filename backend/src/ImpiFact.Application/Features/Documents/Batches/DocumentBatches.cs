using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using FluentValidation;
using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Documents;
using ImpiFact.Application.Abstractions.Handlers;
using ImpiFact.Application.Abstractions.Messaging;
using ImpiFact.Application.Abstractions.Results;
using ImpiFact.Application.Features.Documents.Issue;
using ImpiFact.Domain.Common;
using ImpiFact.Domain.Documents;

namespace ImpiFact.Application.Features.Documents.Batches;

// Batches of documents sent with SendBillAsync (phase 7a, T8; php-rules 15). A batch is created, takes up to 50 documents
// issued by the normal issue pipeline (IIssueDocumentCommand.BatchId) and is sent once; the Worker sends the ZIP and asks GetStatusZip for the verdict of each document (BatchDelivery.cs).

/// <summary><c>POST /api/v1/documents/batches</c>: a new open batch, with the given name or one made by ImpiFact.</summary>
public sealed record CreateDocumentBatchCommand : ICommand
{
    [JsonPropertyName("name")]
    public string? Name { get; init; }
}

/// <summary>
/// <c>POST /api/v1/documents/batches/{id}/send</c>: closes the batch and queues it for the Worker, which sends it with
/// SendBillAsync.
/// </summary>
public sealed record SendDocumentBatchCommand(Guid Id) : ICommand;

/// <summary><c>GET /api/v1/documents/batches/{id}</c>: a batch with its documents.</summary>
public sealed record GetDocumentBatchQuery(Guid Id);

/// <summary><c>GET /api/v1/documents/batches</c>: the company's batches, newest first.</summary>
public sealed record ListDocumentBatchesQuery(int? Limit, int? Offset);

/// <summary>Expected errors of the batches.</summary>
public static class BatchErrors
{
    public static readonly Error NotFound = new("batch.not_found", "El lote no existe.", ErrorType.NotFound);

    public static readonly Error Empty = Error.Validation([new FieldError("batch", "El lote no tiene documentos. Agregue al menos uno antes de enviarlo.")]);

    public static readonly Error Full = new(
        "batch.full",
        $"El lote ya tiene {DocumentBatch.MaxDocuments} documentos, el máximo que la DIAN recibe en un envío por lotes. Envíelo y cree otro lote.",
        ErrorType.Validation);

    public static readonly Error EnvironmentMismatch = new(
        "batch.environment_mismatch",
        "El ambiente de la empresa cambió después de crear el lote: todos los documentos de un lote van al mismo ambiente de la DIAN. " +
        "Envíe el lote o cree otro.",
        ErrorType.Conflict);

    /// <summary>The batch was already sent (or is being sent): it takes no more documents and is never sent again.</summary>
    public static Error NotOpen(string name) => new(
        "batch.not_open", $"El lote {name} ya se envió a la DIAN: no admite más documentos ni otro envío. Cree otro lote.", ErrorType.Conflict);

    public static Error NameInUse(string name) => new(
        "batch.name_in_use", $"La empresa ya tiene un lote con el nombre {name}. Use otro nombre.", ErrorType.Conflict);
}

/// <summary>Rules a batch checks before it takes a document (the issue pipeline asks them).</summary>
internal static class BatchChecks
{
    /// <summary>Why the batch cannot take a document of that environment; null when it can.</summary>
    public static Error? RefuseAddition(DocumentBatch batch, int typeEnvironmentId) =>
        batch.Status != DocumentBatchStatus.Open ? BatchErrors.NotOpen(batch.Name)
        : batch.DocumentCount >= DocumentBatch.MaxDocuments ? BatchErrors.Full
        : batch.TypeEnvironmentId is { } environment && environment != typeEnvironmentId ? BatchErrors.EnvironmentMismatch
        : null;
}

/// <summary>Name of a batch: letters, digits, <c>-</c> and <c>_</c>, up to 50 (php-rules 15.1, item 2: the PHP used any text as a file name).</summary>
internal static partial class BatchNames
{
    public const string Rule = "El nombre del lote solo admite letras, números, guion (-) y guion bajo (_), hasta 50 caracteres.";

    public static bool IsValid(string? name) => name is not null && Pattern().IsMatch(name);

    /// <summary>A name for a batch created without one: unique because it carries the batch id.</summary>
    public static string For(Guid id) => "lote-" + id.ToString("N");

    [GeneratedRegex("^[A-Za-z0-9_-]{1,50}$", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();
}

/// <summary>A batch as the list shows it, without its documents.</summary>
public record DocumentBatchSummary
{
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary><c>Open</c>, <c>Queued</c>, <c>Sent</c>, <c>Completed</c> or <c>Failed</c>.</summary>
    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("document_count")]
    public int DocumentCount { get; init; }

    [JsonPropertyName("type_environment_id")]
    public int? TypeEnvironmentId { get; init; }

    /// <summary>Name of the ZIP sent to the DIAN (<c>fileName</c> of SendBillAsync).</summary>
    [JsonPropertyName("zip_file_name")]
    public string? ZipFileName { get; init; }

    /// <summary>The ZipKey (TrackId) that SendBillAsync returned.</summary>
    [JsonPropertyName("zip_key")]
    public string? ZipKey { get; init; }

    [JsonPropertyName("dian_status_code")]
    public string? DianStatusCode { get; init; }

    [JsonPropertyName("dian_status_description")]
    public string? DianStatusDescription { get; init; }

    /// <summary>Messages of the DIAN about the whole ZIP (initial validations of SendBillAsync).</summary>
    [JsonPropertyName("error_messages")]
    public IReadOnlyList<string> ErrorMessages { get; init; } = [];

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; init; }

    [JsonPropertyName("queued_at")]
    public DateTimeOffset? QueuedAt { get; init; }

    [JsonPropertyName("sent_at")]
    public DateTimeOffset? SentAt { get; init; }

    [JsonPropertyName("completed_at")]
    public DateTimeOffset? CompletedAt { get; init; }
}

/// <summary>A batch with each of its documents and its DIAN verdict: the detail and the answers about one batch.</summary>
public sealed record DocumentBatchResponse : DocumentBatchSummary
{
    /// <summary>The documents in the order they were added.</summary>
    [JsonPropertyName("documents")]
    public IReadOnlyList<BatchDocumentResponse> Documents { get; init; } = [];
}

/// <summary>A document of a batch and its own verdict.</summary>
public sealed record BatchDocumentResponse
{
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    [JsonPropertyName("document_type_id")]
    public int DocumentTypeId { get; init; }

    [JsonPropertyName("full_number")]
    public required string FullNumber { get; init; }

    /// <summary>The document status: <c>Queued</c> in an open batch, then <c>Sent</c>, <c>Accepted</c>, <c>Rejected</c>…</summary>
    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("uuid")]
    public string? Uuid { get; init; }

    [JsonPropertyName("uuid_algorithm")]
    public string? UuidAlgorithm { get; init; }

    /// <summary>Name of its XML inside the ZIP, once the batch is sent.</summary>
    [JsonPropertyName("zip_entry")]
    public string? ZipEntry { get; init; }

    [JsonPropertyName("dian_status_code")]
    public string? DianStatusCode { get; init; }

    [JsonPropertyName("dian_status_description")]
    public string? DianStatusDescription { get; init; }

    /// <summary>Rules the DIAN rejected or notified for this document.</summary>
    [JsonPropertyName("error_messages")]
    public IReadOnlyList<string> ErrorMessages { get; init; } = [];
}

public sealed record DocumentBatchListResponse
{
    [JsonPropertyName("items")]
    public required IReadOnlyList<DocumentBatchSummary> Items { get; init; }

    [JsonPropertyName("total_count")]
    public long TotalCount { get; init; }
}

internal sealed class CreateDocumentBatchCommandValidator : AbstractValidator<CreateDocumentBatchCommand>
{
    public CreateDocumentBatchCommandValidator() =>
        RuleFor(command => command.Name).Must(BatchNames.IsValid).When(command => command.Name is not null)
            .WithMessage(BatchNames.Rule).OverridePropertyName("name");
}

internal sealed class ListDocumentBatchesQueryValidator : AbstractValidator<ListDocumentBatchesQuery>
{
    public const int MaxLimit = 200;

    public ListDocumentBatchesQueryValidator()
    {
        RuleFor(query => query.Limit).InclusiveBetween(1, MaxLimit).When(query => query.Limit is not null)
            .WithMessage($"limit debe estar entre 1 y {MaxLimit}.").OverridePropertyName("limit");
        RuleFor(query => query.Offset).GreaterThanOrEqualTo(0).When(query => query.Offset is not null)
            .WithMessage("offset no puede ser negativo.").OverridePropertyName("offset");
    }
}

internal sealed class CreateDocumentBatchHandler(ITenantContext tenant, IDocumentBatches batches, IClock clock)
    : IHandler<CreateDocumentBatchCommand, DocumentBatchResponse>
{
    public async Task<Result<DocumentBatchResponse>> HandleAsync(CreateDocumentBatchCommand request, CancellationToken ct)
    {
        var companyId = tenant.CompanyId ?? throw new InvalidOperationException("The tenant decorator guarantees a company.");
        if (request.Name is { } name && await batches.FindByNameAsync(companyId, name, ct) is not null)
        {
            return BatchErrors.NameInUse(name);
        }

        var batch = BatchMapping.New(companyId, request.Name);
        batches.Add(batch);
        // The row gets its creation time when it is saved, after this answer is built: the clock gives the same instant.
        return BatchMapping.Response(batch, [], new Dictionary<Guid, IReadOnlyList<string>>()) with { CreatedAt = clock.UtcNow };
    }
}

/// <summary>
/// Closes an open batch with documents and queues one <c>SendDocumentBatchToDian</c> for the Worker. Like the
/// <c>send-pending</c> of the invoices type 04 it checks the company and its certificate, not the plan: each document
/// passed the plan when it was added and counts when the DIAN accepts it. Two sends at the same time change the same
/// batch row: only one is saved (the other answers 409), so only one message, and one SendBillAsync, exists.
/// </summary>
internal sealed class SendDocumentBatchHandler(
    ITenantContext tenant, IDocumentBatches batches, IIssuerContextReads issuers, IOutbox outbox, IClock clock)
    : IHandler<SendDocumentBatchCommand, DocumentBatchResponse>
{
    public async Task<Result<DocumentBatchResponse>> HandleAsync(SendDocumentBatchCommand request, CancellationToken ct)
    {
        var companyId = tenant.CompanyId ?? throw new InvalidOperationException("The tenant decorator guarantees a company.");
        var batch = await batches.FindAsync(companyId, request.Id, ct);
        if (batch is null)
        {
            return BatchErrors.NotFound;
        }

        var issuer = await issuers.GetAsync(companyId, 1, DocumentKind.Invoice, ct);
        if (issuer is null || !issuer.Company.IsActive)
        {
            return IssueErrors.CompanyInactive;
        }

        if (issuer.Certificate is not { } certificate)
        {
            return IssueErrors.CertificateMissing;
        }

        if (ColombiaTime.DaysLeft(ColombiaTime.Today(certificate.NotAfter), clock.UtcNow) <= 0)
        {
            return IssueErrors.CertificateExpired;
        }

        if (batch.Status != DocumentBatchStatus.Open)
        {
            return BatchErrors.NotOpen(batch.Name);
        }

        if (batch.DocumentCount == 0)
        {
            return BatchErrors.Empty;
        }

        batch.Queue(clock.UtcNow);
        await outbox.EnqueueAsync(OutboxMessageTypes.SendDocumentBatchToDian, new DocumentBatchPayload(batch.Id), companyId, notBefore: null, ct);
        var documents = await batches.DocumentsAsync(companyId, batch.Id, ct);
        return BatchMapping.Response(batch, documents, new Dictionary<Guid, IReadOnlyList<string>>());
    }
}

internal sealed class GetDocumentBatchHandler(ITenantContext tenant, IDocumentBatches batches) : IHandler<GetDocumentBatchQuery, DocumentBatchResponse>
{
    public async Task<Result<DocumentBatchResponse>> HandleAsync(GetDocumentBatchQuery request, CancellationToken ct)
    {
        var companyId = tenant.CompanyId ?? throw new InvalidOperationException("The tenant decorator guarantees a company.");
        if (await batches.FindAsync(companyId, request.Id, ct) is not { } batch)
        {
            return BatchErrors.NotFound;
        }

        var documents = await batches.DocumentsAsync(companyId, batch.Id, ct);
        var messages = await batches.LastMessagesAsync(companyId, [.. documents.Select(document => document.Id)], ct);
        return BatchMapping.Response(batch, documents, messages);
    }
}

internal sealed class ListDocumentBatchesHandler(ITenantContext tenant, IDocumentBatches batches)
    : IHandler<ListDocumentBatchesQuery, DocumentBatchListResponse>
{
    public const int DefaultLimit = 50;

    public async Task<Result<DocumentBatchListResponse>> HandleAsync(ListDocumentBatchesQuery request, CancellationToken ct)
    {
        var companyId = tenant.CompanyId ?? throw new InvalidOperationException("The tenant decorator guarantees a company.");
        var rows = await batches.ListAsync(companyId, request.Offset ?? 0, request.Limit ?? DefaultLimit, ct);
        return new DocumentBatchListResponse
        {
            Items = [.. rows.Select(BatchMapping.Summary)],
            TotalCount = await batches.CountAsync(companyId, ct),
        };
    }
}

/// <summary>Payload of <c>SendDocumentBatchToDian</c> and <c>PollDocumentBatchStatus</c>.</summary>
public sealed record DocumentBatchPayload(Guid BatchId);

internal static class BatchMapping
{
    public static DocumentBatch New(Guid companyId, string? name)
    {
        var id = Guid.CreateVersion7();
        return new DocumentBatch { Id = id, CompanyId = companyId, Name = name ?? BatchNames.For(id), Status = DocumentBatchStatus.Open };
    }

    public static DocumentBatchSummary Summary(DocumentBatch batch) => new()
    {
        Id = batch.Id,
        Name = batch.Name,
        Status = batch.Status.ToString(),
        DocumentCount = batch.DocumentCount,
        TypeEnvironmentId = batch.TypeEnvironmentId,
        ZipFileName = batch.ZipFileName,
        ZipKey = batch.ZipKey,
        DianStatusCode = batch.DianStatusCode,
        DianStatusDescription = batch.DianStatusDescription,
        ErrorMessages = batch.ErrorMessages ?? [],
        CreatedAt = batch.CreatedAt,
        QueuedAt = batch.QueuedAt,
        SentAt = batch.SentAt,
        CompletedAt = batch.CompletedAt,
    };

    public static DocumentBatchResponse Response(
        DocumentBatch batch, IReadOnlyList<Document> documents, IReadOnlyDictionary<Guid, IReadOnlyList<string>> messages) => new()
    {
        Id = batch.Id,
        Name = batch.Name,
        Status = batch.Status.ToString(),
        DocumentCount = batch.DocumentCount,
        TypeEnvironmentId = batch.TypeEnvironmentId,
        ZipFileName = batch.ZipFileName,
        ZipKey = batch.ZipKey,
        DianStatusCode = batch.DianStatusCode,
        DianStatusDescription = batch.DianStatusDescription,
        ErrorMessages = batch.ErrorMessages ?? [],
        CreatedAt = batch.CreatedAt,
        QueuedAt = batch.QueuedAt,
        SentAt = batch.SentAt,
        CompletedAt = batch.CompletedAt,
        Documents = documents.Select(document => new BatchDocumentResponse
        {
            Id = document.Id,
            DocumentTypeId = document.DocumentTypeId,
            FullNumber = document.FullNumber,
            Status = document.Status.ToString(),
            Uuid = document.Cufe,
            UuidAlgorithm = document.UuidAlgorithm,
            ZipEntry = batch.Entries?.FirstOrDefault(entry => entry.DocumentId == document.Id)?.FileName,
            DianStatusCode = document.DianStatusCode,
            DianStatusDescription = document.DianStatusDescription,
            ErrorMessages = messages.GetValueOrDefault(document.Id) ?? [],
        }).ToList(),
    };
}
