using ImpiFact.Application.Abstractions.Protection;
using ImpiFact.Application.Abstractions.Results;
using ImpiFact.Application.Features.Companies;
using ImpiFact.Application.Features.Companies.UploadCertificate;
using ImpiFact.Domain.Companies;

namespace ImpiFact.Application.Tests.Features.Companies;

public sealed class UploadCertificateTests
{
    private static readonly byte[] Pfx = [1, 2, 3, 4];

    private FakeCompanyRepository _companies = null!;
    private FakeCertificateInspector _inspector = null!;
    private FakeCertificateStore _store = null!;
    private Company _own = null!;
    private readonly Guid _userId = Guid.CreateVersion7();

    [SetUp]
    public void SetUp()
    {
        _companies = new FakeCompanyRepository();
        _inspector = new FakeCertificateInspector();
        _store = new FakeCertificateStore();
        _own = CompanySamples.Company();
        _companies.Stored.Add(_own);
    }

    [Test]
    public async Task Valid_certificate_is_stored_as_a_secret_and_only_its_metadata_goes_to_the_table()
    {
        var result = await Handle(Upload());

        var row = _companies.AddedOf<Certificate>().Single();
        var details = _inspector.Outcome.Value;
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(_store.Saved.Single().CompanyId, Is.EqualTo(_own.Id));
            Assert.That(_store.Saved.Single().Pfx, Is.EqualTo(Pfx));
            Assert.That(_store.Saved.Single().Password, Is.EqualTo("secreto"));
            Assert.That(row.CompanyId, Is.EqualTo(_own.Id));
            Assert.That(row.SecretRef, Is.EqualTo($"local:{_own.Id:N}/1"));
            Assert.That(row.Name, Is.EqualTo($"{_own.IdentificationNumber}{_own.Dv}.p12"), "Same file name as the PHP.");
            Assert.That(row.Thumbprint, Is.EqualTo(details.Thumbprint));
            Assert.That(row.Subject, Is.EqualTo(details.Subject));
            Assert.That(row.Issuer, Is.EqualTo(details.Issuer));
            Assert.That(row.NotBefore, Is.EqualTo(details.NotBefore));
            Assert.That(row.NotAfter, Is.EqualTo(details.NotAfter));
            Assert.That(row.IsActive, Is.True);
            Assert.That(row.UploadedBy, Is.EqualTo(_userId));
        });
    }

    [Test]
    public async Task Response_carries_the_days_left_and_never_the_secret()
    {
        var result = await Handle(Upload());

        var json = System.Text.Json.JsonSerializer.Serialize(result.Value);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value.Message, Is.EqualTo("Certificado creado con éxito"));
            Assert.That(result.Value.DaysLeft, Is.EqualTo(46));
            Assert.That(result.Value.Certificate.DaysLeft, Is.EqualTo(46));
            Assert.That(json, Does.Contain("\"certificate_days_left\":46"));
            Assert.That(json, Does.Not.Contain("secreto").And.Not.Contain("local:"));
            Assert.That(Upload().ToString(), Does.Not.Contain("secreto").And.Not.Contain("AQIDBA=="));
        });
    }

    [Test]
    public async Task Previous_active_certificate_is_deactivated()
    {
        var previous = new Certificate
        {
            CompanyId = _own.Id, Name = "old.p12", SecretRef = "local:old", Thumbprint = "OLD", Subject = "CN=Old", Issuer = "CN=CA",
            IsActive = true, UploadedBy = _userId,
        };
        _companies.Stored.Add(previous);

        await Handle(Upload());

        Assert.That(previous.IsActive, Is.False);
        Assert.That(_companies.AddedOf<Certificate>().Single().IsActive, Is.True);
    }

    [Test]
    public async Task Inspector_errors_are_returned_and_nothing_is_stored()
    {
        _inspector.Outcome = CertificateErrors.InvalidPasswordOrFormat;

        var result = await Handle(Upload());

        Assert.That(result.Error, Is.EqualTo(CertificateErrors.InvalidPasswordOrFormat));
        Assert.That(_store.Saved, Is.Empty);
        Assert.That(_companies.Added, Is.Empty);
    }

    [Test]
    public async Task Expired_certificate_is_rejected()
    {
        _inspector.Outcome = CertificateErrors.Expired;

        var result = await Handle(Upload());

        Assert.That(result.Error.Message, Is.EqualTo("El certificado digital ya se encuentra vencido."));
        Assert.That(_store.Saved, Is.Empty);
    }

    [Test]
    public async Task Api_key_without_a_user_cannot_upload_until_the_model_allows_it()
    {
        var result = await new UploadCertificateHandler(_companies, CompanySamples.CompanyKey(_own.Id), _inspector, _store)
            .HandleAsync(Upload(), CancellationToken.None);

        Assert.That(result.Error, Is.EqualTo(CompanyErrors.CertificateUploaderRequired));
        Assert.That(_store.Saved, Is.Empty, "Nothing is stored before failing.");
    }

    [Test]
    public async Task Nit_of_another_company_is_forbidden()
    {
        var result = await Handle(Upload() with { Nit = "800111222" });

        Assert.That(result.Error, Is.EqualTo(CompanyErrors.ForeignNit));
        Assert.That(_inspector.Inspected, Is.Empty);
    }

    [TestCase("no-es-base64!!")]
    [TestCase("")]
    public void Certificate_must_be_valid_base64(string certificate)
    {
        var result = new UploadCertificateValidator().Validate(Upload() with { Certificate = certificate });

        Assert.That(result.Errors.Single().PropertyName, Is.EqualTo("certificate"));
    }

    [Test]
    public void Password_is_required()
    {
        var result = new UploadCertificateValidator().Validate(Upload() with { Password = null });

        Assert.That(result.Errors.Single().ErrorMessage, Is.EqualTo("El campo contraseña es obligatorio."));
    }

    private static UploadCertificateCommand Upload() => new() { Certificate = Convert.ToBase64String(Pfx), Password = "secreto" };

    private Task<Result<CertificateSavedResponse>> Handle(UploadCertificateCommand command) =>
        new UploadCertificateHandler(_companies, CompanySamples.PanelUser(_own.Id, _userId), _inspector, _store)
            .HandleAsync(command, CancellationToken.None);
}
