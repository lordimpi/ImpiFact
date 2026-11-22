using ImpiFact.Domain.Catalog;
using ImpiFact.Domain.Companies;
using ImpiFact.Domain.Documents;
using ImpiFact.Domain.Masters;
using ImpiFact.Infrastructure.Persistence.Conversions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ImpiFact.Infrastructure.Persistence.Configurations.Documents;

// Documentos (Modelo de datos detallado, secciones 3.5, 6 y 7). Toda referencia entre tablas de la empresa es
// compuesta (company_id, X_id), así una fila hija no puede apuntar al documento de otra empresa. Los filtros de
// índice son SQL de PostgreSQL (único motor de la fase 2).

internal sealed class DocumentConfiguration : IEntityTypeConfiguration<Document>
{
    public void Configure(EntityTypeBuilder<Document> builder)
    {
        builder.ToTable("documents");
        builder.Property(document => document.Prefix).HasMaxLength(10).HasStoreDefault("");
        builder.Property(document => document.FullNumber).HasMaxLength(30);
        builder.Property(document => document.Cufe).HasMaxLength(96);

        // 16 y no 10 como dice el modelo: «CUFE-SHA384» tiene 11 caracteres (la misma corrección de type_documents).
        builder.Property(document => document.UuidAlgorithm).HasMaxLength(16);
        builder.Property(document => document.CustomerSnapshot).HasJsonConversion();
        builder.Property(document => document.ExchangeRate).IsQuantity();
        builder.Property(document => document.LineExtensionAmount).IsAmount();
        builder.Property(document => document.TaxExclusiveAmount).IsAmount();
        builder.Property(document => document.TaxInclusiveAmount).IsAmount();
        builder.Property(document => document.AllowanceTotalAmount).IsAmount().HasStoreDefault(0m);
        builder.Property(document => document.ChargeTotalAmount).IsAmount().HasStoreDefault(0m);
        builder.Property(document => document.PrepaidAmount).IsAmount().HasStoreDefault(0m);
        builder.Property(document => document.PayableAmount).IsAmount();
        builder.Property(document => document.DiscrepancyDescription).HasMaxLength(500);
        builder.Property(document => document.Notes).HasMaxLength(2000);
        builder.Property(document => document.HeadNote).HasMaxLength(2000);
        builder.Property(document => document.FootNote).HasMaxLength(2000);
        builder.Property(document => document.TrackId).HasMaxLength(64);
        builder.Property(document => document.DianStatusCode).HasMaxLength(10);
        builder.Property(document => document.DianStatusDescription).HasMaxLength(500);
        builder.Property(document => document.EmailStatus).HasStoreDefault(EmailStatus.NotSent);
        builder.Property(document => document.Extensions).HasJsonConversion();

        builder.HasCompanyScopedKey();
        builder.BelongsToCompany();
        builder.References<Document, TypeDocument>(document => document.DocumentTypeId);
        builder.References<Document, TypeEnvironment>(document => document.TypeEnvironmentId);
        builder.References<Document, TypeCurrency>(document => document.CurrencyId);
        builder.References<Document, TypeOperation>(document => document.TypeOperationId);
        builder.ReferencesInCompany<Document, Resolution>(nameof(Document.ResolutionId));
        builder.ReferencesInCompany<Document, Customer>(nameof(Document.CustomerId));
        builder.ReferencesInCompany<Document, Document>(nameof(Document.ReferenceDocumentId));

        builder.HasIndex(
                document => new { document.CompanyId, document.DocumentTypeId, document.TypeEnvironmentId, document.Prefix, document.Number }).HasDatabaseName("ux_documents_number")
            .IsUnique();
        // Los índices parciales (filtrados) los escribe cada motor (PortableIndexFilters): SQL Server trata los NULL como
        // iguales en un índice único y sin el filtro solo admitiría un documento sin CUFE.
        builder.HasIndex(document => new { document.CompanyId, document.Cufe }).HasDatabaseName("ux_documents_cufe").IsUnique()
            .HasPortableFilter(PortableIndexFilterKind.WhereNotNull, "cufe");
        builder.HasIndex(document => new { document.CompanyId, document.IssuedAt }).HasDatabaseName("ix_documents_company_issued").IsDescending(false, true);
        builder.HasIndex(document => new { document.CompanyId, document.Status }).HasDatabaseName("ix_documents_company_status");
        builder.HasIndex(document => new { document.CompanyId, document.CustomerId, document.IssuedAt }).HasDatabaseName("ix_documents_customer")
            .IsDescending(false, false, true);
        builder.HasIndex(document => document.TrackId).HasDatabaseName("ix_documents_track")
            .HasPortableFilter(PortableIndexFilterKind.WhereNotNull, "track_id");
    }
}

internal sealed class DocumentLineConfiguration : IEntityTypeConfiguration<DocumentLine>
{
    public void Configure(EntityTypeBuilder<DocumentLine> builder)
    {
        builder.ToTable("document_lines");
        builder.Property(line => line.Code).HasMaxLength(100);
        builder.Property(line => line.Description).HasMaxLength(500);
        builder.Property(line => line.Notes).HasMaxLength(1000);
        builder.Property(line => line.Quantity).IsQuantity();
        builder.Property(line => line.BaseQuantity).IsQuantity();
        builder.Property(line => line.UnitPrice).IsQuantity();
        builder.Property(line => line.LineExtensionAmount).IsAmount();
        builder.Property(line => line.Extensions).HasJsonConversion();
        builder.HasCompanyScopedKey();
        builder.BelongsToCompany();
        builder.ReferencesInCompany<DocumentLine, Document>(nameof(DocumentLine.DocumentId));
        builder.References<DocumentLine, TypeItemIdentification>(line => line.TypeItemIdentificationId);
        builder.References<DocumentLine, UnitMeasure>(line => line.UnitMeasureId);
        builder.References<DocumentLine, ReferencePrice>(line => line.ReferencePriceId);
        builder.HasIndex(line => new { line.DocumentId, line.LineNumber }).IsUnique();
    }
}

internal sealed class DocumentTaxConfiguration : IEntityTypeConfiguration<DocumentTax>
{
    public void Configure(EntityTypeBuilder<DocumentTax> builder)
    {
        builder.ToTable("document_taxes");
        builder.Property(tax => tax.Percent).IsPercentage();
        builder.Property(tax => tax.TaxableAmount).IsAmount();
        builder.Property(tax => tax.TaxAmount).IsAmount();
        builder.BelongsToCompany();
        builder.ReferencesInCompany<DocumentTax, Document>(nameof(DocumentTax.DocumentId));
        builder.References<DocumentTax, Tax>(tax => tax.TaxId);
        builder.HasIndex(tax => tax.DocumentId);
    }
}

internal sealed class DocumentLineTaxConfiguration : IEntityTypeConfiguration<DocumentLineTax>
{
    public void Configure(EntityTypeBuilder<DocumentLineTax> builder)
    {
        builder.ToTable("document_line_taxes");
        builder.Property(tax => tax.Percent).IsPercentage();
        builder.Property(tax => tax.TaxableAmount).IsAmount();
        builder.Property(tax => tax.TaxAmount).IsAmount();
        builder.Property(tax => tax.PerUnitAmount).IsQuantity();
        builder.BelongsToCompany();
        builder.ReferencesInCompany<DocumentLineTax, Document>(nameof(DocumentLineTax.DocumentId));
        builder.ReferencesInCompany<DocumentLineTax, DocumentLine>(nameof(DocumentLineTax.LineId));
        builder.References<DocumentLineTax, Tax>(tax => tax.TaxId);
        builder.References<DocumentLineTax, UnitMeasure>(tax => tax.UnitMeasureId);
        builder.HasIndex(tax => tax.LineId);
    }
}

internal sealed class DocumentAllowanceChargeConfiguration : IEntityTypeConfiguration<DocumentAllowanceCharge>
{
    public void Configure(EntityTypeBuilder<DocumentAllowanceCharge> builder)
    {
        builder.ToTable("document_allowance_charges");
        builder.Property(charge => charge.Reason).HasMaxLength(500);
        builder.Property(charge => charge.Amount).IsAmount();
        builder.Property(charge => charge.BaseAmount).IsAmount();
        builder.Property(charge => charge.MultiplierFactor).IsPercentage();
        builder.BelongsToCompany();
        builder.ReferencesInCompany<DocumentAllowanceCharge, Document>(nameof(DocumentAllowanceCharge.DocumentId));
        builder.ReferencesInCompany<DocumentAllowanceCharge, DocumentLine>(nameof(DocumentAllowanceCharge.LineId));
        builder.References<DocumentAllowanceCharge, Discount>(charge => charge.DiscountId);
    }
}

internal sealed class DocumentPaymentConfiguration : IEntityTypeConfiguration<DocumentPayment>
{
    public void Configure(EntityTypeBuilder<DocumentPayment> builder)
    {
        builder.ToTable("document_payments");
        builder.Property(payment => payment.DurationMeasure).HasMaxLength(20);
        builder.BelongsToCompany();
        builder.ReferencesInCompany<DocumentPayment, Document>(nameof(DocumentPayment.DocumentId));
        builder.References<DocumentPayment, PaymentForm>(payment => payment.PaymentFormId);
        builder.References<DocumentPayment, PaymentMethod>(payment => payment.PaymentMethodId);
    }
}

internal sealed class DocumentReferenceConfiguration : IEntityTypeConfiguration<DocumentReference>
{
    public void Configure(EntityTypeBuilder<DocumentReference> builder)
    {
        builder.ToTable("document_references");
        builder.Property(reference => reference.Number).HasMaxLength(50);
        builder.Property(reference => reference.Uuid).HasMaxLength(96);
        builder.BelongsToCompany();
        builder.ReferencesInCompany<DocumentReference, Document>(nameof(DocumentReference.DocumentId));
    }
}

internal sealed class DocumentFileConfiguration : IEntityTypeConfiguration<DocumentFile>
{
    public void Configure(EntityTypeBuilder<DocumentFile> builder)
    {
        builder.ToTable("document_files");
        builder.Property(file => file.Revision).HasStoreDefault(1);
        builder.Property(file => file.StorageKey).HasMaxLength(500);
        builder.Property(file => file.ContentType).HasMaxLength(100);
        builder.Property(file => file.Sha256).IsFixed(64);
        builder.HasCompanyScopedKey();
        builder.BelongsToCompany();
        builder.ReferencesInCompany<DocumentFile, Document>(nameof(DocumentFile.DocumentId));
        builder.HasIndex(file => new { file.DocumentId, file.Kind, file.Revision }).IsUnique();
    }
}

internal sealed class DianSubmissionConfiguration : IEntityTypeConfiguration<DianSubmission>
{
    public void Configure(EntityTypeBuilder<DianSubmission> builder)
    {
        builder.ToTable("dian_submissions");
        builder.Property(submission => submission.TrackId).HasMaxLength(64);
        builder.Property(submission => submission.ZipKey).HasMaxLength(64);
        builder.Property(submission => submission.DianStatusCode).HasMaxLength(10);
        builder.Property(submission => submission.DianStatusDescription).HasMaxLength(500);
        builder.Property(submission => submission.ErrorMessages).HasJsonConversion();
        builder.BelongsToCompany();
        builder.ReferencesInCompany<DianSubmission, Document>(nameof(DianSubmission.DocumentId));
        builder.ReferencesInCompany<DianSubmission, DocumentFile>(nameof(DianSubmission.RequestFileId));
        builder.ReferencesInCompany<DianSubmission, DocumentFile>(nameof(DianSubmission.ResponseFileId));
        builder.References<DianSubmission, TypeEnvironment>(submission => submission.TypeEnvironmentId);
        builder.HasIndex(submission => new { submission.DocumentId, submission.Attempt }).IsUnique();
        builder.HasIndex(submission => submission.TrackId);
    }
}

internal sealed class DocumentEventConfiguration : IEntityTypeConfiguration<DocumentEvent>
{
    public void Configure(EntityTypeBuilder<DocumentEvent> builder)
    {
        builder.ToTable("document_events", table => table.HasCheckConstraint(
            "ck_document_events_target",
            "(document_id IS NOT NULL AND received_document_id IS NULL) OR (document_id IS NULL AND received_document_id IS NOT NULL)"));
        builder.Property(documentEvent => documentEvent.Cude).HasMaxLength(96);
        builder.Property(documentEvent => documentEvent.TrackId).HasMaxLength(64);
        builder.Property(documentEvent => documentEvent.DianStatusCode).HasMaxLength(10);
        builder.Property(documentEvent => documentEvent.Number).HasMaxLength(120);
        builder.Property(documentEvent => documentEvent.DianStatusDescription).HasMaxLength(500);
        builder.Property(documentEvent => documentEvent.ErrorMessages).HasJsonConversion();
        builder.Property(documentEvent => documentEvent.RejectionCode).HasMaxLength(2);
        builder.Property(documentEvent => documentEvent.XmlFileKey).HasMaxLength(500);
        builder.Property(documentEvent => documentEvent.ResponseFileKey).HasMaxLength(500);
        builder.BelongsToCompany();
        builder.ReferencesInCompany<DocumentEvent, Document>(nameof(DocumentEvent.DocumentId));
        builder.ReferencesInCompany<DocumentEvent, ReceivedDocument>(nameof(DocumentEvent.ReceivedDocumentId));
        builder.ReferencesInCompany<DocumentEvent, DocumentFile>(nameof(DocumentEvent.PayloadFileId));
        builder.References<DocumentEvent, Event>(documentEvent => documentEvent.EventId);
        builder.HasIndex(documentEvent => new { documentEvent.CompanyId, documentEvent.Cude }).IsUnique();
        builder.HasIndex(documentEvent => documentEvent.DocumentId);
        builder.HasIndex(documentEvent => documentEvent.ReceivedDocumentId);
    }
}

internal sealed class ReceivedDocumentConfiguration : IEntityTypeConfiguration<ReceivedDocument>
{
    public void Configure(EntityTypeBuilder<ReceivedDocument> builder)
    {
        builder.ToTable("received_documents");
        builder.Property(received => received.SupplierIdentificationNumber).HasMaxLength(20);
        builder.Property(received => received.SupplierDv).IsFixed(1);
        builder.Property(received => received.SupplierName).HasMaxLength(255);
        builder.Property(received => received.SupplierOrganizationTypeCode).HasMaxLength(2);
        builder.Property(received => received.SupplierRegimeCode).HasMaxLength(2);
        builder.Property(received => received.SupplierEmail).HasMaxLength(255);
        builder.Property(received => received.Prefix).HasMaxLength(10).HasStoreDefault("");
        builder.Property(received => received.Number).HasMaxLength(20);
        builder.Property(received => received.Cufe).HasMaxLength(96);
        builder.Property(received => received.LineExtensionAmount).IsAmount();
        builder.Property(received => received.TaxExclusiveAmount).IsAmount();
        builder.Property(received => received.AllowanceTotalAmount).IsAmount();
        builder.Property(received => received.TaxAmount).IsAmount();
        builder.Property(received => received.PayableAmount).IsAmount();
        builder.Property(received => received.XmlFileKey).HasMaxLength(500);
        builder.Property(received => received.PdfFileKey).HasMaxLength(500);
        builder.HasCompanyScopedKey();
        builder.BelongsToCompany();
        builder.References<ReceivedDocument, TypeDocument>(received => received.DocumentTypeId);
        builder.References<ReceivedDocument, TypeEnvironment>(received => received.TypeEnvironmentId);
        builder.HasIndex(received => new { received.CompanyId, received.Cufe }).IsUnique();
        builder.HasIndex(received => new { received.CompanyId, received.IssueDate }).IsDescending(false, true);
        builder.HasIndex(received => new { received.CompanyId, received.SupplierIdentificationNumber });
    }
}

internal sealed class NumberingSequenceConfiguration : IEntityTypeConfiguration<NumberingSequence>
{
    public void Configure(EntityTypeBuilder<NumberingSequence> builder)
    {
        builder.ToTable("numbering_sequences");
        builder.Property(sequence => sequence.ScopeKey).HasMaxLength(120);
        builder.BelongsToCompany();
        builder.HasIndex(sequence => new { sequence.CompanyId, sequence.ScopeKey }).IsUnique();
    }
}
