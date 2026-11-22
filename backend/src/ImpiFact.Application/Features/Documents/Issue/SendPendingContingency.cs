using System.Globalization;
using System.Text.Json.Serialization;
using FluentValidation;
using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Documents;
using ImpiFact.Application.Abstractions.Handlers;
using ImpiFact.Application.Abstractions.Messaging;
using ImpiFact.Application.Abstractions.Results;
using ImpiFact.Domain.Common;
using ImpiFact.Domain.Documents;

namespace ImpiFact.Application.Features.Documents.Issue;

/// <summary>
/// When the invoices type 04 (issued while the DIAN did not answer) are sent. Annex 1.9, section 12.2: the invoicer
/// monitors the DIAN service again 30 minutes after the error and keeps monitoring until it is back, and then has 48 hours
/// to transmit those invoices with SendBillSync. In ImpiFact the Worker does it alone: the issue schedules a
/// <c>SendDocumentToDian</c> message after <see cref="RetryInterval"/>; while the DIAN does not receive it the message
/// is retried with the outbox backoff and, when its attempts run out, the document stays pending and another message is
/// scheduled after <see cref="RetryInterval"/>, until <see cref="AutomaticWindow"/> after it was stored. The
/// <c>send-pending</c> route sends them at once (the <c>send_pendings</c> of the PHP).
/// </summary>
internal static class ContingencyDelivery
{
    /// <summary>Wait before the first automatic attempt and between rounds of attempts (annex 1.9, 12.2: 30 minutes).</summary>
    public static readonly TimeSpan RetryInterval = TimeSpan.FromMinutes(30);

    /// <summary>
    /// How long the Worker keeps retrying alone after the document was stored: the 48 hours of the annex. After that the
    /// document stays pending, the company gets a notice and only <c>send-pending</c> sends it.
    /// </summary>
    public static readonly TimeSpan AutomaticWindow = TimeSpan.FromHours(48);
}

/// <summary>
/// <c>POST /api/v1/documents/invoices/dian-contingency/send-pending</c>
/// (the PHP's <c>invoice-contingency-4/send_pendings[/{prefix}[/{number}]]</c>): send now the company's pending invoices
/// type 04 (php-rules 12.3).
/// </summary>
/// <param name="Prefix">Only the documents with this prefix; null for all.</param>
/// <param name="Number">Only the document with this number; it needs <paramref name="Prefix"/>, as in the PHP.</param>
public sealed record SendPendingContingencyInvoicesCommand(string? Prefix, string? Number) : ICommand;

/// <summary>
/// The answer of <c>send-pending</c>: the PHP messages (php-rules 12.3, item 6) and the documents queued for the Worker,
/// which sends them with SendBillSync and leaves them Accepted or Rejected (see <c>GET /api/v1/documents/{id}</c>). The PHP
/// waited for the DIAN and returned its answers in <c>responses</c>; ImpiFact answers right away.
/// </summary>
public sealed record SendPendingContingencyInvoicesResponse
{
    public const string SentMessage = "Envios de documentos de contingencia tipo 4 realizados con exito.";

    public const string NothingPendingMessage = "No existen registros de documentos de contingencia tipo 4 para realizar envios....";

    [JsonPropertyName("success")]
    public bool Success { get; init; } = true;

    [JsonPropertyName("message")]
    public required string Message { get; init; }

    /// <summary>The documents queued for sending, by prefix and number; empty when none was pending.</summary>
    [JsonPropertyName("documents")]
    public required IReadOnlyList<PendingContingencyInvoice> Documents { get; init; }
}

/// <summary>An invoice type 04 queued for sending: it stays <c>ContingencyPending</c> until the Worker sends it.</summary>
public sealed record PendingContingencyInvoice(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("full_number")] string FullNumber,
    [property: JsonPropertyName("status")] string Status);

internal sealed class SendPendingContingencyInvoicesCommandValidator : AbstractValidator<SendPendingContingencyInvoicesCommand>
{
    /// <summary>Text of the PHP (<c>send_pendings</c>, InvoiceContingencyController.php:1032-1043).</summary>
    public const string PrefixRequired =
        "Para hacer envios los envios pendientes debe al menos suministrar el prefijo de las facturas de contingencia tipo 4 que desea enviar....";

    public SendPendingContingencyInvoicesCommandValidator()
    {
        RuleFor(command => command.Prefix)
            .NotEmpty().When(command => !string.IsNullOrWhiteSpace(command.Number)).WithMessage(PrefixRequired)
            .OverridePropertyName("prefix");
        RuleFor(command => command.Prefix)
            .MaximumLength(10).WithMessage(IssueMessages.MaxLength("prefix", 10))
            .OverridePropertyName("prefix");
        RuleFor(command => command.Number)
            .Must(number => long.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out _))
            .When(command => !string.IsNullOrWhiteSpace(command.Number))
            .WithMessage(IssueMessages.Integer("number"))
            .OverridePropertyName("number");
    }
}

/// <summary>
/// Makes a <c>SendDocumentToDian</c> message due now for each pending invoice type 04 of the company of the credential
/// (never another company's, unlike the PHP lookups of php-rules 12.2, item 2): the one already queued is brought forward,
/// and only a document without one gets a new message, so a document never has two sends racing to the DIAN (end-of-7a
/// verification, M1). Like the PHP (php-rules 12.3, item 2) it
/// checks the company and its certificate but not the plan: the quota was checked when issuing and counts on acceptance.
/// The documents stay <c>ContingencyPending</c> until the Worker sends them; one the DIAN rejects ends <c>Rejected</c>
/// with its notice, never pending again (the PHP left it in state 2 and sent it again on every call, item 5).
/// </summary>
internal sealed class SendPendingContingencyInvoicesHandler(
    ITenantContext tenant, IIssuerContextReads issuers, IDocumentDelivery documents, IOutbox outbox, IClock clock)
    : IHandler<SendPendingContingencyInvoicesCommand, SendPendingContingencyInvoicesResponse>
{
    public async Task<Result<SendPendingContingencyInvoicesResponse>> HandleAsync(SendPendingContingencyInvoicesCommand request, CancellationToken ct)
    {
        var companyId = tenant.CompanyId ?? throw new InvalidOperationException("The tenant decorator guarantees a company.");
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

        var prefix = string.IsNullOrWhiteSpace(request.Prefix) ? null : request.Prefix.Trim();
        long? number = string.IsNullOrWhiteSpace(request.Number) ? null : long.Parse(request.Number, NumberStyles.None, CultureInfo.InvariantCulture);
        var pending = await documents.ListContingencyPendingAsync(companyId, prefix, number, ct);
        var live = await documents.LiveSendsAsync(companyId, pending.Select(document => document.Id).ToList(), ct);
        var now = clock.UtcNow;
        foreach (var document in pending)
        {
            var sends = live.Where(send => send.DocumentId == document.Id).ToList();
            if (sends.Count > 0)
            {
                // Already queued (the automatic attempt of the issue, a retry, or an earlier call): the waiting message is
                // made due now and a running one is left alone. A second message would race the first one to the DIAN.
                foreach (var send in sends.Where(send => !send.Running))
                {
                    await documents.ExpediteSendAsync(send.MessageId, now, ct);
                }

                continue;
            }

            // No message left (the automatic window ended): a new one, with the document claimed in the same save, so two
            // calls at the same time cannot both add one (the second answers 409).
            var tracked = await documents.FindForUpdateAsync(companyId, document.Id, ct);
            if (tracked is null)
            {
                continue;
            }

            documents.Claim(tracked);
            await outbox.EnqueueAsync(
                OutboxMessageTypes.SendDocumentToDian, new SendDocumentToDianPayload(document.Id, null), companyId, notBefore: null, ct);
        }

        return new SendPendingContingencyInvoicesResponse
        {
            Message = pending.Count > 0 ? SendPendingContingencyInvoicesResponse.SentMessage : SendPendingContingencyInvoicesResponse.NothingPendingMessage,
            Documents = pending.Select(document => new PendingContingencyInvoice(document.Id, document.FullNumber, document.Status.ToString())).ToList(),
        };
    }
}
