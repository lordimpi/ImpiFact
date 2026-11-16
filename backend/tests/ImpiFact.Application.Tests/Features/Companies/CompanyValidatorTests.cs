using FluentValidation.Results;
using ImpiFact.Application.Features.Companies;
using ImpiFact.Application.Features.Companies.CreateCompany;
using ImpiFact.Domain.Catalog;

namespace ImpiFact.Application.Tests.Features.Companies;

public sealed class CompanyValidatorTests
{
    [Test]
    public async Task Valid_sample_passes()
    {
        var result = await Validate(CompanySamples.Create());

        Assert.That(result.Errors, Is.Empty);
    }

    [Test]
    public async Task Missing_required_fields_use_the_php_messages_and_json_names()
    {
        var result = await Validate(new CreateCompanyCommand { Nit = CompanySamples.Nit, Dv = CompanySamples.Dv });

        var errors = Messages(result);
        Assert.Multiple(() =>
        {
            Assert.That(errors["business_name"], Is.EqualTo("El campo business name es obligatorio."));
            Assert.That(errors["type_document_identification_id"], Is.EqualTo("El campo type document identification id es obligatorio."));
            Assert.That(errors["type_organization_id"], Is.EqualTo("El campo type organization id es obligatorio."));
            Assert.That(errors["type_regime_id"], Is.EqualTo("El campo type regime id es obligatorio."));
            Assert.That(errors["type_liability_id"], Is.EqualTo("El campo type liability id es obligatorio."));
            Assert.That(errors["municipality_id"], Is.EqualTo("El campo municipality id es obligatorio."));
            Assert.That(errors["merchant_registration"], Is.EqualTo("El campo merchant registration es obligatorio."));
            Assert.That(errors["address"], Is.EqualTo("El campo dirección es obligatorio."));
            Assert.That(errors["phone"], Is.EqualTo("El campo teléfono es obligatorio."));
            Assert.That(errors["email"], Is.EqualTo("El campo correo electrónico es obligatorio."));
        });
    }

    [Test]
    public async Task Liability_list_alone_is_enough()
    {
        var result = await Validate(CompanySamples.Create() with { TypeLiabilityId = null, TypeLiabilityIds = [7, 14] });

        Assert.That(result.Errors, Is.Empty);
    }

    [Test]
    public async Task Wrong_check_digit_is_rejected_with_the_php_message()
    {
        var result = await Validate(CompanySamples.Create(dv: "3"));

        Assert.That(Messages(result)["dv"], Is.EqualTo("Error, el digito de verificacion no es valido para este NIT."));
    }

    [TestCase("12345a")]
    [TestCase("1234567890123456")]
    public async Task Nit_must_have_between_1_and_15_digits(string nit)
    {
        var result = await Validate(CompanySamples.Create(nit: nit, dv: "0"));

        Assert.That(Messages(result)["nit"], Is.EqualTo("nit debe tener entre 1 y 15 dígitos."));
    }

    [TestCase("321654")]
    [TestCase("32165470000")]
    [TestCase("321-6547")]
    public async Task Phone_must_have_between_7_and_10_digits(string phone)
    {
        var result = await Validate(CompanySamples.Create() with { Phone = phone });

        Assert.That(Messages(result)["phone"], Is.EqualTo("teléfono debe tener entre 7 y 10 dígitos."));
    }

    [Test]
    public async Task Invalid_email_is_rejected()
    {
        var result = await Validate(CompanySamples.Create(email: "no-es-correo"));

        Assert.That(Messages(result)["email"], Is.EqualTo("correo electrónico no es un correo válido"));
    }

    [Test]
    public async Task Unknown_catalog_ids_are_rejected_like_the_php_exists_rule()
    {
        var catalogs = new FakeCatalogLookup()
            .Missing<Municipality>(99999)
            .Missing<Language>(5)
            .Missing<TypeLiability>(500);

        var result = await Validate(CompanySamples.Create() with { MunicipalityId = 99999, LanguageId = 5, TypeLiabilityIds = [500] }, catalogs);

        var errors = Messages(result);
        Assert.Multiple(() =>
        {
            Assert.That(errors["municipality_id"], Is.EqualTo("municipality id es inválido."));
            Assert.That(errors["language_id"], Is.EqualTo("language id es inválido."));
            Assert.That(errors.Keys, Does.Contain("type_liability_ids[0]"));
        });
    }

    [TestCase(0)]
    [TestCase(3)]
    public async Task Initial_environment_must_be_production_or_habilitation(int environment)
    {
        var result = await Validate(CompanySamples.Create() with { PayrollTypeEnvironmentId = environment });

        Assert.That(Messages(result)["payroll_type_environment_id"], Is.EqualTo("payroll type environment id es inválido."));
    }

    [TestCase("900123456", 8)]
    [TestCase("800197268", 4)]
    [TestCase("860034313", 7)]
    [TestCase("1", 8)]
    public void Check_digit_follows_the_dian_algorithm(string nit, int expected)
    {
        Assert.That(Nit.CheckDigit(nit), Is.EqualTo(expected));
    }

    private static Task<ValidationResult> Validate(CreateCompanyCommand command, FakeCatalogLookup? catalogs = null) =>
        new CreateCompanyValidator(catalogs ?? new FakeCatalogLookup()).ValidateAsync(command);

    private static Dictionary<string, string> Messages(ValidationResult result) =>
        result.Errors.GroupBy(error => error.PropertyName).ToDictionary(group => group.Key, group => group.First().ErrorMessage);
}
