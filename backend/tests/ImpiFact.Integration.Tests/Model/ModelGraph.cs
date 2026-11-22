using System.Text.Json.Nodes;
using ImpiFact.Domain.Billing;
using ImpiFact.Domain.Common;
using ImpiFact.Domain.Companies;
using ImpiFact.Domain.Documents;
using ImpiFact.Domain.Identity;
using ImpiFact.Domain.Masters;
using ImpiFact.Domain.Platform;
using ImpiFact.Infrastructure.Persistence.Seeding;

namespace ImpiFact.Integration.Tests.Model;

/// <summary>
/// Arma una empresa con una fila de cada tabla de empresa, todas con referencias válidas, y la guarda como
/// proceso del sistema. Los ids de catálogo son filas reales de la semilla.
/// </summary>
internal sealed class ModelGraph
{
    private const int Colombia = 46;
    private const int Bogota = 149;
    private const int Spanish = 79;
    private const int PesoColombiano = 35;
    private const int Standard = 10;
    private const int Nit = 6;
    private const int Iva = 1;
    private const int Invoice = 1;
    private const int Habilitation = 2;

    private static int sequence;

    private ModelGraph(Guid companyId) => CompanyId = companyId;

    public Guid CompanyId { get; }

    public Guid DocumentId { get; private set; }

    public Guid LineId { get; private set; }

    public List<object> Rows { get; } = [];

    /// <summary>Crea y guarda la empresa con todas sus filas.</summary>
    public static async Task<ModelGraph> CreateAsync()
    {
        var companyId = Guid.CreateVersion7();
        var nit = (900000000 + Interlocked.Increment(ref sequence)).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var user = new User
        {
            Id = Guid.CreateVersion7(),
            Email = $"owner{nit}@example.com",
            PasswordHash = "hash",
            Name = "Owner",
            SecurityStamp = "stamp",
        };

        // El plan va primero: su id lo genera la base y company_plans lo necesita.
        var plan = new Plan { Name = "Plan " + nit, Period = PlanPeriod.Monthly };
        await using (var session = ModelDatabase.Open(companyId: null))
        {
            session.Db.AddRange(user, NewCompany(companyId, nit), plan);
            await session.Db.SaveChangesAsync();
        }

        var graph = Build(companyId, nit, user.Id, plan.Id);
        await using (var session = ModelDatabase.Open(companyId: null))
        {
            session.Db.AddRange(graph.Rows);
            await session.Db.SaveChangesAsync();
        }

        return graph;
    }

    /// <summary>Una empresa sin filas hijas.</summary>
    public static Company NewCompany(Guid id, string nit) => new()
    {
        Id = id,
        IdentificationNumber = nit,
        Dv = "1",
        Name = "Company " + nit,
        Email = $"company{nit}@example.com",
        Phone = "3000000000",
        Address = "Calle 1",
        LanguageId = Spanish,
        CountryId = Colombia,
        MunicipalityId = Bogota,
        TaxId = Iva,
        TypeCurrencyId = PesoColombiano,
        TypeOperationId = Standard,
        TypeDocumentIdentificationId = Nit,
        TypeOrganizationId = 1,
        TypeRegimeId = 1,
    };

    /// <summary>Un documento mínimo de la empresa, sin cliente ni resolución.</summary>
    public static Document NewDocument(Guid companyId, long number, string? cufe, Guid? resolutionId = null, Guid? customerId = null, JsonObject? extensions = null) => new()
    {
        Id = Guid.CreateVersion7(),
        CompanyId = companyId,
        ResolutionId = resolutionId,
        CustomerId = customerId,
        DocumentTypeId = Invoice,
        TypeEnvironmentId = Habilitation,
        Prefix = "SETP",
        Number = number,
        FullNumber = "SETP" + number,
        Status = DocumentStatus.Received,
        Cufe = cufe,
        IssueDate = new DateOnly(2026, 1, 15),
        IssueTime = new TimeOnly(5, 0),
        IssuedAt = new DateTimeOffset(2026, 1, 15, 10, 0, 0, TimeSpan.Zero),
        CustomerSnapshot = new CustomerSnapshot { IdentificationNumber = "222222222222", Name = "Consumidor final" },
        CurrencyId = PesoColombiano,
        LineExtensionAmount = 100m,
        TaxExclusiveAmount = 100m,
        TaxInclusiveAmount = 119m,
        PayableAmount = 119m,
        Extensions = extensions,
        CreatedByType = ActorKind.ApiKey,
    };

    private static ModelGraph Build(Guid companyId, string nit, Guid userId, int planId)
    {
        var graph = new ModelGraph(companyId);
        var now = new DateTimeOffset(2026, 1, 15, 10, 0, 0, TimeSpan.Zero);
        var customer = new Customer
        {
            Id = Guid.CreateVersion7(),
            CompanyId = companyId,
            IdentificationNumber = "222222222222",
            TypeDocumentIdentificationId = 3,
            Name = "Consumidor final",
        };
        var resolution = new Resolution
        {
            Id = Guid.CreateVersion7(),
            CompanyId = companyId,
            DocumentTypeId = Invoice,
            TypeEnvironmentId = Habilitation,
            Prefix = "SETP",
            ResolutionNumber = "18760000001",
            ResolutionDate = new DateOnly(2019, 1, 19),
            RangeFrom = 990000000,
            RangeTo = 995000000,
            IsActive = true,
        };
        var document = NewDocument(companyId, 990000001, "ab" + nit, resolution.Id, customer.Id, new JsonObject { ["aiu"] = true });
        var line = new DocumentLine
        {
            Id = Guid.CreateVersion7(),
            CompanyId = companyId,
            DocumentId = document.Id,
            LineNumber = 1,
            Description = "Item",
            TypeItemIdentificationId = 1,
            UnitMeasureId = 1,
            Quantity = 1.5m,
            BaseQuantity = 1m,
            UnitPrice = 66.666667m,
            LineExtensionAmount = 100m,
        };
        var file = new DocumentFile
        {
            Id = Guid.CreateVersion7(),
            CompanyId = companyId,
            DocumentId = document.Id,
            Kind = DocumentFileKind.SignedXml,
            StorageProvider = StorageProvider.Local,
            StorageKey = $"{nit}/invoice.xml",
            ContentType = "application/xml",
            SizeBytes = 1024,
            Sha256 = new string('a', 64),
        };
        var received = new ReceivedDocument
        {
            Id = Guid.CreateVersion7(),
            CompanyId = companyId,
            SupplierIdentificationNumber = "800000000",
            SupplierDv = "1",
            SupplierName = "Supplier",
            DocumentTypeId = Invoice,
            Number = "1",
            Cufe = "cd" + nit,
            IssueDate = new DateOnly(2026, 1, 10),
            TypeEnvironmentId = Habilitation,
            PayableAmount = 50m,
        };
        
        graph.DocumentId = document.Id;
        graph.LineId = line.Id;
        graph.Rows.AddRange(
        [
            customer, resolution, document, line, file, received,
            new UserCompany { CompanyId = companyId, UserId = userId, RoleId = PlatformSeed.CustomerRoleId, CustomerId = customer.Id },
            new CompanyLiability { CompanyId = companyId, TypeLiabilityId = 7 },
            new CompanyEnvironment { CompanyId = companyId, DocumentKind = DocumentKind.Invoice, TypeEnvironmentId = Habilitation },
            new CompanySoftware
            {
                CompanyId = companyId, DocumentKind = DocumentKind.Invoice, TypeEnvironmentId = Habilitation,
                SoftwareIdentifier = "software", PinCipher = "cipher", DianUrl = "https://vpfe-hab.dian.gov.co",
            },
            new Certificate
            {
                CompanyId = companyId, Name = "cert.p12", SecretRef = "certificates/" + nit, Thumbprint = new string('b', 40),
                Subject = "CN=Company", Issuer = "CN=CA", NotBefore = now, NotAfter = now.AddYears(1), IsActive = true, UploadedBy = userId,
            },
            new CompanyMailSettings { CompanyId = companyId, UsePlatformDefault = true },
            new CompanyLogo { CompanyId = companyId, StorageKey = $"{nit}/logo.png", ContentType = "image/png" },
            new Item
            {
                CompanyId = companyId, Code = "ITEM-1", Name = "Item", UnitMeasureId = 1, TypeItemIdentificationId = 1,
                SaleUnitPrice = 1.123456m, TaxId = Iva,
            },
            new DocumentTax { CompanyId = companyId, DocumentId = document.Id, TaxId = Iva, Percent = 19m, TaxableAmount = 100m, TaxAmount = 19m },
            new DocumentLineTax
            {
                CompanyId = companyId, DocumentId = document.Id, LineId = line.Id, TaxId = Iva, Percent = 19m,
                TaxableAmount = 100m, TaxAmount = 19m,
            },
            new DocumentAllowanceCharge
            {
                CompanyId = companyId, DocumentId = document.Id, LineId = line.Id, DiscountId = 1, Amount = 1m, BaseAmount = 100m,
                MultiplierFactor = 1m,
            },
            new DocumentPayment { CompanyId = companyId, DocumentId = document.Id, PaymentFormId = 1, PaymentMethodId = 1 },
            new DocumentReference { CompanyId = companyId, DocumentId = document.Id, Kind = DocumentReferenceKind.OrderReference, Number = "OC-1" },
            new DianSubmission
            {
                CompanyId = companyId, DocumentId = document.Id, Attempt = 1, Operation = DianOperation.SendTestSetAsync,
                TypeEnvironmentId = Habilitation, RequestFileId = file.Id, ErrorMessages = ["Regla: FAD06"], StartedAt = now,
            },
            new DocumentEvent
            {
                CompanyId = companyId, ReceivedDocumentId = received.Id, EventId = 1, Cude = "ef" + nit, IssuedAt = now,
                Status = DocumentEventStatus.Pending, PayloadFileId = file.Id,
            },
            new DocumentBatch { CompanyId = companyId, Name = "batch-" + nit },
            new NumberingSequence { CompanyId = companyId, ScopeKey = "R:" + resolution.Id, NextNumber = 990000002 },
            new CompanyPlan { CompanyId = companyId, PlanId = planId, StartsAt = now, IsActive = true },
            new Notification { CompanyId = companyId, UserId = userId, Type = "certificate", Title = "Title", Body = "Body" },
            new IdempotencyKey
            {
                CompanyId = companyId, Key = "key-" + nit, RequestHash = new string('c', 64), ResponseStatus = 200,
                ResponseBody = JsonNode.Parse("""{"is_valid":true}"""), ExpiresAt = now.AddDays(1),
            },
        ]);

        return graph;
    }
}
