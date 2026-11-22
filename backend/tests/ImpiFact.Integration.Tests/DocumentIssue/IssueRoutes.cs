namespace ImpiFact.Integration.Tests.DocumentIssue;

/// <summary>
/// The <c>/api/v1/documents</c> issue route of each document, named by its PHP route (<c>invoice</c>, <c>credit-note</c>,
/// <c>invoice-aiu</c>…), the name the golden cases and the batch types use. The PHP routes themselves
/// (<c>/api/ubl2.1/...</c>) were deleted in phase 7a, T9b2. A name followed by <c>/{testSetId}</c> gives the route of the
/// habilitation test set.
/// </summary>
internal static class IssueRoutes
{
    public static string Of(string phpRoute)
    {
        var slash = phpRoute.IndexOf('/', StringComparison.Ordinal);
        var name = slash < 0 ? phpRoute : phpRoute[..slash];
        var path = name switch
        {
            "invoice" => "/api/v1/documents/invoices",
            "invoice-aiu" => "/api/v1/documents/invoices/aiu",
            "invoice-mandate" => "/api/v1/documents/invoices/mandate",
            "invoice-transport" => "/api/v1/documents/invoices/transport",
            "invoice-export" => "/api/v1/documents/invoices/export",
            "invoice-contingency" => "/api/v1/documents/invoices/contingency",
            "invoice-contingency-4" => "/api/v1/documents/invoices/dian-contingency",
            "credit-note" => "/api/v1/documents/credit-notes",
            "debit-note" => "/api/v1/documents/debit-notes",
            "support-document" => "/api/v1/documents/support-documents",
            "sd-credit-note" => "/api/v1/documents/support-documents/adjustment-notes",
            _ => throw new ArgumentException($"Unknown PHP issue route: {phpRoute}", nameof(phpRoute)),
        };
        return slash < 0 ? path : $"{path}/test-set/{phpRoute[(slash + 1)..]}";
    }
}
