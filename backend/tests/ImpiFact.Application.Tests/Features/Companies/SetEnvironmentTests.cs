using ImpiFact.Application.Abstractions.Results;
using ImpiFact.Application.Features.Companies;
using ImpiFact.Application.Features.Companies.SetEnvironment;
using ImpiFact.Domain.Common;
using ImpiFact.Domain.Companies;

namespace ImpiFact.Application.Tests.Features.Companies;

public sealed class SetEnvironmentTests
{
    private FakeCompanyRepository _companies = null!;
    private Company _own = null!;

    [SetUp]
    public void SetUp()
    {
        _companies = new FakeCompanyRepository();
        _own = CompanySamples.Company();
        _companies.Stored.Add(_own);
        foreach (var kind in Enum.GetValues<DocumentKind>())
        {
            _companies.Stored.Add(new CompanyEnvironment { CompanyId = _own.Id, DocumentKind = kind, TypeEnvironmentId = 2 });
        }
    }

    [Test]
    public async Task Without_software_the_environment_is_not_changed_and_the_php_message_is_returned()
    {
        var result = await Handle(new SetEnvironmentCommand { TypeEnvironmentId = 1 });

        Assert.Multiple(() =>
        {
            Assert.That(result.Error, Is.EqualTo(CompanyErrors.SoftwareMissing));
            Assert.That(result.Error.Message, Is.EqualTo("No se ha configurado el software para esta empresa. Configure el software primero."));
            Assert.That(Environment(DocumentKind.Invoice).TypeEnvironmentId, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task Each_family_takes_its_own_environment_from_technical_or_spanish_names()
    {
        AddSoftware(DocumentKind.Invoice, 2);

        var result = await Handle(new SetEnvironmentCommand { FacturaVentas = 1, PayrollTypeEnvironmentId = 1, EventosRadian = 1 });

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value.Message, Is.EqualTo("Ambiente actualizado con éxito"));
            Assert.That(Environment(DocumentKind.Invoice).TypeEnvironmentId, Is.EqualTo(1));
            Assert.That(Environment(DocumentKind.Payroll).TypeEnvironmentId, Is.EqualTo(1));
            Assert.That(Environment(DocumentKind.Events).TypeEnvironmentId, Is.EqualTo(1));
            Assert.That(Environment(DocumentKind.SupportDocument).TypeEnvironmentId, Is.EqualTo(2), "Families not sent stay as they are.");
            Assert.That(result.Value.Environments, Has.Count.EqualTo(5));
        });
    }

    [Test]
    public async Task Technical_name_wins_when_both_names_are_sent()
    {
        AddSoftware(DocumentKind.Invoice, 2);

        await Handle(new SetEnvironmentCommand { TypeEnvironmentId = 2, FacturaVentas = 1 });

        Assert.That(Environment(DocumentKind.Invoice).TypeEnvironmentId, Is.EqualTo(2));
    }

    [Test]
    public async Task Moving_to_production_copies_the_software_with_the_production_url()
    {
        var habilitation = AddSoftware(DocumentKind.Invoice, 2);

        await Handle(new SetEnvironmentCommand { TypeEnvironmentId = 1 });

        var production = _companies.AddedOf<CompanySoftware>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(production.DocumentKind, Is.EqualTo(DocumentKind.Invoice));
            Assert.That(production.TypeEnvironmentId, Is.EqualTo(1));
            Assert.That(production.SoftwareIdentifier, Is.EqualTo(habilitation.SoftwareIdentifier));
            Assert.That(production.PinCipher, Is.EqualTo(habilitation.PinCipher));
            Assert.That(production.DianUrl, Is.EqualTo(FakeDianUrls.Production));
            Assert.That(production.TestSetId, Is.Null, "The test set only belongs to habilitation.");
            Assert.That(habilitation.DianUrl, Is.EqualTo(FakeDianUrls.Habilitation), "The habilitation row is kept.");
        });
    }

    [Test]
    public async Task Existing_software_of_the_target_environment_is_reused()
    {
        AddSoftware(DocumentKind.Invoice, 2);
        AddSoftware(DocumentKind.Invoice, 1);

        await Handle(new SetEnvironmentCommand { TypeEnvironmentId = 1 });

        Assert.That(_companies.AddedOf<CompanySoftware>(), Is.Empty);
    }

    [Test]
    public async Task Missing_environment_row_is_created()
    {
        AddSoftware(DocumentKind.Invoice, 2);
        _companies.Stored.Remove(Environment(DocumentKind.Payroll));

        await Handle(new SetEnvironmentCommand { Nomina = 1 });

        var added = _companies.AddedOf<CompanyEnvironment>().Single();
        Assert.That((added.DocumentKind, added.TypeEnvironmentId, added.CompanyId), Is.EqualTo((DocumentKind.Payroll, 1, _own.Id)));
    }

    [Test]
    public async Task Nit_of_another_company_is_forbidden()
    {
        AddSoftware(DocumentKind.Invoice, 2);

        var result = await Handle(new SetEnvironmentCommand { Nit = "800111222", TypeEnvironmentId = 1 });

        Assert.That(result.Error, Is.EqualTo(CompanyErrors.ForeignNit));
        Assert.That(Environment(DocumentKind.Invoice).TypeEnvironmentId, Is.EqualTo(2));
    }

    [TestCase(3)]
    [TestCase(0)]
    public void Environment_must_be_1_or_2_with_the_php_messages(int value)
    {
        var result = new SetEnvironmentValidator().Validate(new SetEnvironmentCommand { TypeEnvironmentId = value, Nomina = value });

        var errors = result.Errors.ToDictionary(error => error.PropertyName, error => error.ErrorMessage);
        Assert.Multiple(() =>
        {
            Assert.That(errors["type_environment_id"], Is.EqualTo("El ambiente de factura de ventas seleccionado no es válido."));
            Assert.That(errors["nomina"], Is.EqualTo("El ambiente de nómina seleccionado no es válido."));
        });
    }

    private CompanyEnvironment Environment(DocumentKind kind) =>
        _companies.Stored.OfType<CompanyEnvironment>().Single(environment => environment.DocumentKind == kind);

    private CompanySoftware AddSoftware(DocumentKind kind, int environment)
    {
        var software = new CompanySoftware
        {
            CompanyId = _own.Id,
            DocumentKind = kind,
            TypeEnvironmentId = environment,
            SoftwareIdentifier = $"software-{environment}",
            PinCipher = "cipher",
            DianUrl = environment == 1 ? FakeDianUrls.Production : FakeDianUrls.Habilitation,
            TestSetId = "test-set",
        };
        _companies.Stored.Add(software);
        return software;
    }

    private Task<Result<EnvironmentsResponse>> Handle(SetEnvironmentCommand command) =>
        new SetEnvironmentHandler(_companies, CompanySamples.CompanyKey(_own.Id), new FakeDianUrls()).HandleAsync(command, CancellationToken.None);
}
