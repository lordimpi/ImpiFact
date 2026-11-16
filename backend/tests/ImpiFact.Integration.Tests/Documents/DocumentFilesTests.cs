using System.Security.Cryptography;
using System.Text;
using ImpiFact.Application.Abstractions.Documents;
using ImpiFact.Application.Abstractions.Storage;
using ImpiFact.Domain.Documents;
using ImpiFact.Integration.Tests.Model;
using Microsoft.EntityFrameworkCore;

namespace ImpiFact.Integration.Tests.Documents;

/// <summary>
/// Archivos de documento: el contenido va al almacén local y la fila de <c>document_files</c> guarda el tipo, el
/// proveedor, la clave, el tipo de contenido, el tamaño y el SHA-256, y se guarda con la transacción.
/// También comprueba que el estado del documento cambia y se guarda solo con transiciones permitidas.
/// </summary>
[TestFixture]
[Category("Integration")]
public sealed class DocumentFilesTests
{
    private static readonly byte[] SignedXml = Encoding.UTF8.GetBytes("<Invoice>firmado</Invoice>");

    [Test]
    public async Task A_document_file_is_stored_and_recorded_with_its_size_and_sha256()
    {
        var (companyId, documentId) = await CreateDocumentAsync();

        DocumentFile added;
        await using (var session = DocumentsDatabase.Open(companyId))
        {
            await using var transaction = await session.UnitOfWork.BeginTransactionAsync(CancellationToken.None);
            added = await session.Get<IDocumentFiles>().AddAsync(
                new NewDocumentFile(companyId, documentId, DocumentFileKind.SignedXml, SignedXml, "application/xml"), CancellationToken.None);
            await session.UnitOfWork.SaveChangesAsync(CancellationToken.None);
            await transaction.CommitAsync(CancellationToken.None);
        }

        await using var read = DocumentsDatabase.Open(companyId);
        var row = await read.Db.Set<DocumentFile>().AsNoTracking().SingleAsync(file => file.DocumentId == documentId);
        await using var stream = await read.Get<IBlobStorage>().OpenReadAsync(row.StorageKey, CancellationToken.None);
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);

        Assert.Multiple(() =>
        {
            Assert.That(row.Id, Is.EqualTo(added.Id));
            Assert.That(row.Kind, Is.EqualTo(DocumentFileKind.SignedXml));
            Assert.That(row.Revision, Is.EqualTo(1));
            Assert.That(row.StorageProvider, Is.EqualTo(StorageProvider.Local));
            Assert.That(row.StorageKey, Is.EqualTo($"{companyId:N}/documents/{documentId:N}/{row.Id:N}.xml"));
            Assert.That(row.ContentType, Is.EqualTo("application/xml"));
            Assert.That(row.SizeBytes, Is.EqualTo(SignedXml.Length));
            Assert.That(row.Sha256, Is.EqualTo(Convert.ToHexStringLower(SHA256.HashData(SignedXml))));
            Assert.That(buffer.ToArray(), Is.EqualTo(SignedXml));
            Assert.That(File.Exists(Path.Combine(DocumentsDatabase.StorageRoot, row.StorageKey)), Is.True);
        });
    }

    [Test]
    public async Task A_rolled_back_transaction_records_no_document_file()
    {
        var (companyId, documentId) = await CreateDocumentAsync();

        await using (var session = DocumentsDatabase.Open(companyId))
        {
            await using var transaction = await session.UnitOfWork.BeginTransactionAsync(CancellationToken.None);
            await session.Get<IDocumentFiles>().AddAsync(
                new NewDocumentFile(companyId, documentId, DocumentFileKind.SignedXml, SignedXml, "application/xml"), CancellationToken.None);
            await session.UnitOfWork.SaveChangesAsync(CancellationToken.None);
            await transaction.RollbackAsync(CancellationToken.None);
        }

        await using var read = DocumentsDatabase.Open(companyId);
        Assert.That(await read.Db.Set<DocumentFile>().AnyAsync(file => file.DocumentId == documentId), Is.False);
    }

    [Test]
    public async Task A_company_request_cannot_store_files_of_another_company()
    {
        var (otherCompany, documentId) = await CreateDocumentAsync();
        var own = await DocumentsDatabase.CreateCompanyAsync();
        await using var session = DocumentsDatabase.Open(own);

        Assert.ThrowsAsync<InvalidOperationException>(() => session.Get<IDocumentFiles>().AddAsync(
            new NewDocumentFile(otherCompany, documentId, DocumentFileKind.SignedXml, SignedXml, "application/xml"), CancellationToken.None));
        Assert.That(Directory.Exists(Path.Combine(DocumentsDatabase.StorageRoot, otherCompany.ToString("N"))), Is.False,
            "Nothing is written to storage for a refused request.");
    }

    [Test]
    public async Task A_document_moved_to_queued_is_stored_queued()
    {
        var (companyId, documentId) = await CreateDocumentAsync();

        await using (var session = DocumentsDatabase.Open(companyId))
        {
            var document = await session.Db.Set<Document>().SingleAsync(row => row.Id == documentId);
            document.MoveTo(DocumentStatus.Queued);
            await session.Db.SaveChangesAsync();
        }

        await using var read = DocumentsDatabase.Open(companyId);
        var stored = await read.Db.Set<Document>().AsNoTracking().SingleAsync(row => row.Id == documentId);
        Assert.Multiple(() =>
        {
            Assert.That(stored.Status, Is.EqualTo(DocumentStatus.Queued));
            Assert.Throws<InvalidDocumentTransitionException>(() => stored.MoveTo(DocumentStatus.Received));
        });
    }

    private static async Task<(Guid CompanyId, Guid DocumentId)> CreateDocumentAsync()
    {
        var companyId = await DocumentsDatabase.CreateCompanyAsync();
        var document = ModelGraph.NewDocument(companyId, 1, cufe: null);
        await using var session = DocumentsDatabase.Open(companyId);
        session.Db.Add(document);
        await session.Db.SaveChangesAsync();
        return (companyId, document.Id);
    }
}
