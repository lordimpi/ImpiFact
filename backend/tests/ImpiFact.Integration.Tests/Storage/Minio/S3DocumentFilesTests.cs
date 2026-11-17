using System.Text;
using ImpiFact.Application.Abstractions.Documents;
using ImpiFact.Domain.Documents;
using ImpiFact.Integration.Tests.Model;
using Microsoft.EntityFrameworkCore;

namespace ImpiFact.Integration.Tests.Storage.Minio;

/// <summary>
/// Con el proveedor S3, los archivos de un documento van al bucket y su fila de <c>document_files</c> registra
/// <c>storage_provider = S3</c>, para saber dónde buscarlos aunque después cambie el proveedor configurado.
/// </summary>
[TestFixture]
[Category("Integration")]
public sealed class S3DocumentFilesTests
{
    [Test]
    public async Task A_document_file_stored_in_s3_records_s3_as_its_provider()
    {
        var companyId = await MinioStorage.CreateCompanyAsync();
        var document = ModelGraph.NewDocument(companyId, 1, cufe: null);
        await using (var session = MinioStorage.Open(companyId))
        {
            session.Db.Add(document);
            await session.Db.SaveChangesAsync();
        }

        var xml = Encoding.UTF8.GetBytes("<Invoice>firmado</Invoice>");
        await using (var session = MinioStorage.Open(companyId))
        {
            await using var transaction = await session.UnitOfWork.BeginTransactionAsync(CancellationToken.None);
            await session.Get<IDocumentFiles>().AddAsync(
                new NewDocumentFile(companyId, document.Id, DocumentFileKind.SignedXml, xml, "application/xml"), CancellationToken.None);
            await session.UnitOfWork.SaveChangesAsync(CancellationToken.None);
            await transaction.CommitAsync(CancellationToken.None);
        }

        await using var read = MinioStorage.Open(companyId);
        var row = await read.Db.Set<DocumentFile>().AsNoTracking().SingleAsync(file => file.DocumentId == document.Id);
        using var stored = await MinioStorage.Client.GetObjectAsync(MinioStorage.Bucket, row.StorageKey);
        using var buffer = new MemoryStream();
        await stored.ResponseStream.CopyToAsync(buffer);

        Assert.Multiple(() =>
        {
            Assert.That(row.StorageProvider, Is.EqualTo(StorageProvider.S3));
            Assert.That(row.StorageKey, Is.EqualTo($"{companyId:N}/documents/{document.Id:N}/{row.Id:N}.xml"));
            Assert.That(buffer.ToArray(), Is.EqualTo(xml));
        });
    }
}
