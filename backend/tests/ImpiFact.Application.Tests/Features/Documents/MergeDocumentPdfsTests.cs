using ImpiFact.Application.Features.Documents.Queries.MergePdfs;

namespace ImpiFact.Application.Tests.Features.Documents;

public sealed class MergeDocumentPdfsTests
{
    [TestCase(1)]
    [TestCase(50)]
    public void Between_1_and_50_documents_are_accepted(int count) =>
        Assert.That(Validate(count), Is.Empty);

    [TestCase(0)]
    [TestCase(51)]
    public void An_empty_or_longer_list_is_refused(int count) =>
        Assert.That(Validate(count), Is.EqualTo(new[] { ("document_ids", "Envíe entre 1 y 50 documentos para unir sus PDF.") }));

    [Test]
    public void A_missing_list_is_refused() =>
        Assert.That(new MergeDocumentPdfsQueryValidator().Validate(new MergeDocumentPdfsQuery()).Errors.Single().PropertyName, Is.EqualTo("document_ids"));

    [Test]
    public void The_missing_pdfs_are_named_once_in_the_order_asked_for()
    {
        var error = MergePdfErrors.PdfMissing(["SETP2", "SETP1"]);

        Assert.Multiple(() =>
        {
            Assert.That(error.Code, Is.EqualTo("documents.pdf_missing"));
            Assert.That(error.Message, Is.EqualTo("Estos documentos no tienen PDF: SETP2, SETP1."));
            Assert.That(error.Fields.Single().Field, Is.EqualTo("document_ids"));
        });
    }

    private static IEnumerable<(string, string)> Validate(int count) =>
        new MergeDocumentPdfsQueryValidator()
            .Validate(new MergeDocumentPdfsQuery { DocumentIds = [.. Enumerable.Range(0, count).Select(_ => Guid.NewGuid())] })
            .Errors.Select(error => (error.PropertyName, error.ErrorMessage));
}
