using ImpiFact.Application.Abstractions.Results;
using ImpiFact.Application.Features.Companies.SetResolutionNextNumber;
using ImpiFact.Domain.Companies;
using ImpiFact.Domain.Documents;

namespace ImpiFact.Application.Tests.Features.Companies;

public sealed class SetResolutionNextNumberTests
{
    private FakeCompanyRepository _companies = null!;
    private FakeNumberingAdvance _advance = null!;
    private FakeNumberingSequences _numbering = null!;
    private Company _own = null!;
    private Resolution _resolution = null!;

    [SetUp]
    public void SetUp()
    {
        _companies = new FakeCompanyRepository();
        _advance = new FakeNumberingAdvance();
        _numbering = new FakeNumberingSequences();
        _own = CompanySamples.Company();
        _resolution = new Resolution
        {
            Id = Guid.CreateVersion7(), CompanyId = _own.Id, DocumentTypeId = 1, TypeEnvironmentId = 2, Prefix = "SETP",
            RangeFrom = 990000000, RangeTo = 995000000, IsActive = true,
        };
        _companies.Stored.Add(_own);
        _companies.Stored.Add(_resolution);
        _advance.Next[(_own.Id, NumberingScope.ForResolution(_resolution.Id))] = 990000001;
    }

    [Test]
    public async Task Moves_the_sequence_to_the_requested_number()
    {
        var result = await Handle(990000005);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value.NextNumber, Is.EqualTo(990000005));
            Assert.That(result.Value.ResolutionId, Is.EqualTo(_resolution.Id));
            Assert.That(_advance.Next[(_own.Id, NumberingScope.ForResolution(_resolution.Id))], Is.EqualTo(990000005));
        });
    }

    [Test]
    public async Task A_number_below_the_current_one_is_a_validation_error_on_next_number()
    {
        var result = await Handle(990000000);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error.Type, Is.EqualTo(ErrorType.Validation));
            Assert.That(result.Error.Fields.Single(), Is.EqualTo(new FieldError("next_number",
                "El próximo número no puede ser menor que 990000001, el próximo número actual: bajarlo podría repetir un " +
                "número ya enviado a la DIAN.")));
        });
    }

    [TestCase(989999999)]
    [TestCase(995000001)]
    public async Task A_number_outside_the_range_is_a_validation_error_and_the_sequence_is_not_touched(long number)
    {
        var result = await Handle(number);

        Assert.Multiple(() =>
        {
            Assert.That(result.Error.Fields.Single(), Is.EqualTo(new FieldError("next_number",
                "El próximo número debe estar dentro del rango de la resolución, entre 990000000 y 995000000.")));
            Assert.That(_advance.Next[(_own.Id, NumberingScope.ForResolution(_resolution.Id))], Is.EqualTo(990000001));
        });
    }

    [Test]
    public async Task A_resolution_without_a_sequence_opens_it_at_the_requested_number()
    {
        _advance.Next.Clear();

        var result = await Handle(990000300);

        Assert.Multiple(() =>
        {
            Assert.That(result.Value.NextNumber, Is.EqualTo(990000300));
            Assert.That(_numbering.Opened.Single(), Is.EqualTo((_own.Id, _resolution.Id, 990000300L)));
        });
    }

    [Test]
    public async Task An_unknown_resolution_is_not_found()
    {
        var result = await new SetResolutionNextNumberHandler(_companies, _advance, _numbering, CompanySamples.CompanyKey(_own.Id))
            .HandleAsync(new SetResolutionNextNumberCommand { ResolutionId = Guid.CreateVersion7(), NextNumber = 990000005 }, CancellationToken.None);

        Assert.That(result.Error.Code, Is.EqualTo("resolution.not_found"));
    }

    [Test]
    public async Task The_next_number_is_required_and_not_negative()
    {
        var validator = new SetResolutionNextNumberValidator();

        var missing = await validator.ValidateAsync(new SetResolutionNextNumberCommand());
        var negative = await validator.ValidateAsync(new SetResolutionNextNumberCommand { NextNumber = -1 });

        Assert.Multiple(() =>
        {
            Assert.That(missing.Errors.Single().PropertyName, Is.EqualTo("next_number"));
            Assert.That(missing.Errors.Single().ErrorMessage, Is.EqualTo("El campo next number es obligatorio."));
            Assert.That(negative.Errors.Single().ErrorMessage, Is.EqualTo("El campo next number debe ser como mínimo 0."));
        });
    }

    private Task<Result<ResolutionNextNumberResponse>> Handle(long number) =>
        new SetResolutionNextNumberHandler(_companies, _advance, _numbering, CompanySamples.CompanyKey(_own.Id))
            .HandleAsync(new SetResolutionNextNumberCommand { ResolutionId = _resolution.Id, NextNumber = number }, CancellationToken.None);
}
