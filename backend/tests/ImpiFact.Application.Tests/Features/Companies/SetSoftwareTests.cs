using ImpiFact.Application.Abstractions.Protection;
using ImpiFact.Application.Abstractions.Results;
using ImpiFact.Application.Features.Companies;
using ImpiFact.Application.Features.Companies.SetSoftware;
using ImpiFact.Domain.Common;
using ImpiFact.Domain.Companies;

namespace ImpiFact.Application.Tests.Features.Companies;

public sealed class SetSoftwareTests
{
    private FakeCompanyRepository _companies = null!;
    private Company _own = null!;
    private readonly FakeSecretProtector _secrets = new();

    [SetUp]
    public void SetUp()
    {
        _companies = new FakeCompanyRepository();
        _own = CompanySamples.Company();
        _companies.Stored.Add(_own);
        _companies.Stored.Add(new CompanyEnvironment { CompanyId = _own.Id, DocumentKind = DocumentKind.Invoice, TypeEnvironmentId = 2 });
        _companies.Stored.Add(new CompanyEnvironment { CompanyId = _own.Id, DocumentKind = DocumentKind.Payroll, TypeEnvironmentId = 1 });
    }

    [Test]
    public async Task New_software_is_stored_with_the_pin_encrypted_for_its_purpose()
    {
        var result = await Handle(new SetSoftwareCommand { Id = "soft-1", Pin = "12345", TestSetId = "set-1" });

        var software = _companies.AddedOf<CompanySoftware>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value.Message, Is.EqualTo("Software creado/actualizado con éxito"));
            Assert.That(software.CompanyId, Is.EqualTo(_own.Id));
            Assert.That(software.DocumentKind, Is.EqualTo(DocumentKind.Invoice));
            Assert.That(software.TypeEnvironmentId, Is.EqualTo(2), "The software goes to the current environment of the family.");
            Assert.That(software.SoftwareIdentifier, Is.EqualTo("soft-1"));
            Assert.That(software.PinCipher, Is.EqualTo(_secrets.Protect(SecretPurposes.SoftwarePin, "12345")));
            Assert.That(software.PinCipher, Does.Not.Contain("12345"));
            Assert.That(software.TestSetId, Is.EqualTo("set-1"));
            Assert.That(software.DianUrl, Is.EqualTo(FakeDianUrls.Habilitation));
        });
    }

    [Test]
    public async Task Default_url_follows_the_environment_of_the_family()
    {
        await Handle(new SetSoftwareCommand { IdPayroll = "payroll-1", PinPayroll = "54321" });

        var software = _companies.AddedOf<CompanySoftware>().Single();
        Assert.That((software.DocumentKind, software.TypeEnvironmentId, software.DianUrl),
            Is.EqualTo((DocumentKind.Payroll, 1, FakeDianUrls.Production)));
    }

    [Test]
    public async Task Url_in_the_request_is_kept()
    {
        await Handle(new SetSoftwareCommand { Id = "soft-1", Pin = "12345", Url = "https://custom.example/svc" });

        Assert.That(_companies.AddedOf<CompanySoftware>().Single().DianUrl, Is.EqualTo("https://custom.example/svc"));
    }

    [Test]
    public async Task Existing_software_is_updated_in_place()
    {
        var existing = new CompanySoftware
        {
            CompanyId = _own.Id, DocumentKind = DocumentKind.Invoice, TypeEnvironmentId = 2,
            SoftwareIdentifier = "old", PinCipher = "old-cipher", DianUrl = "https://custom.example/svc", TestSetId = "old-set",
        };
        _companies.Stored.Add(existing);

        await Handle(new SetSoftwareCommand { Id = "new", Pin = "11111" });

        Assert.Multiple(() =>
        {
            Assert.That(_companies.AddedOf<CompanySoftware>(), Is.Empty);
            Assert.That(existing.SoftwareIdentifier, Is.EqualTo("new"));
            Assert.That(existing.PinCipher, Is.EqualTo(_secrets.Protect(SecretPurposes.SoftwarePin, "11111")));
            Assert.That(existing.DianUrl, Is.EqualTo("https://custom.example/svc"), "Unlike the PHP, the URL is not reset to habilitation.");
            Assert.That(existing.TestSetId, Is.EqualTo("old-set"));
        });
    }

    [Test]
    public async Task New_software_without_identifier_is_rejected()
    {
        var result = await Handle(new SetSoftwareCommand { TestSetId = "set-1" });

        Assert.That(result.Error.Type, Is.EqualTo(ErrorType.Validation));
        Assert.That(result.Error.Fields.Single(), Is.EqualTo(new FieldError("id", "El campo id es obligatorio.")));
        Assert.That(_companies.Added, Is.Empty);
    }

    [Test]
    public async Task Response_never_carries_the_pin()
    {
        var result = await Handle(new SetSoftwareCommand { Id = "soft-1", Pin = "12345" });

        Assert.That(System.Text.Json.JsonSerializer.Serialize(result.Value), Does.Not.Contain("12345").And.Not.Contain("cipher"));
        Assert.That(new SetSoftwareCommand { Pin = "12345" }.ToString(), Does.Not.Contain("12345"));
    }

    [Test]
    public async Task Nit_of_another_company_is_forbidden()
    {
        var result = await Handle(new SetSoftwareCommand { Nit = "800111222", Id = "soft-1", Pin = "12345" });

        Assert.That(result.Error, Is.EqualTo(CompanyErrors.ForeignNit));
    }

    [TestCase("1234")]
    [TestCase("123456")]
    [TestCase("12a45")]
    public void Pin_must_be_numeric_with_5_digits(string pin)
    {
        var result = new SetSoftwareValidator().Validate(new SetSoftwareCommand { Id = "soft-1", Pin = pin });

        Assert.That(result.Errors.Single().PropertyName, Is.EqualTo("pin"));
        Assert.That(result.Errors.Single().ErrorMessage, Is.EqualTo("pin debe tener 5 dígitos."));
    }

    [Test]
    public void Pin_is_required_with_the_identifier()
    {
        var result = new SetSoftwareValidator().Validate(new SetSoftwareCommand { IdPayroll = "payroll-1" });

        Assert.That(result.Errors.Single().ErrorMessage, Is.EqualTo("El campo pinpayroll es obligatorio cuando idpayroll está presente."));
    }

    [Test]
    public void Url_must_be_absolute()
    {
        var result = new SetSoftwareValidator().Validate(new SetSoftwareCommand { Id = "soft-1", Pin = "12345", Url = "no-es-url" });

        Assert.That(result.Errors.Single().ErrorMessage, Is.EqualTo("El formato url es inválido."));
    }

    private Task<Result<SoftwareSavedResponse>> Handle(SetSoftwareCommand command) =>
        new SetSoftwareHandler(_companies, CompanySamples.CompanyKey(_own.Id), _secrets, new FakeDianUrls())
            .HandleAsync(command, CancellationToken.None);
}
