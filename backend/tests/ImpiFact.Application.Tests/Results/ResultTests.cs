using ImpiFact.Application.Abstractions.Results;

namespace ImpiFact.Application.Tests.Results;

public class ResultTests
{
    private static readonly Error NotFound = new("company.not_found", "La empresa no existe.", ErrorType.NotFound);

    [Test]
    public void Ok_carries_the_value()
    {
        var result = Result<int>.Ok(42);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value, Is.EqualTo(42));
    }

    [Test]
    public void Fail_carries_the_error()
    {
        var result = Result<int>.Fail(NotFound);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error, Is.EqualTo(NotFound));
    }

    [Test]
    public void Reading_the_value_of_a_failure_throws()
    {
        var result = Result<int>.Fail(NotFound);

        var exception = Assert.Throws<InvalidOperationException>(() => _ = result.Value);
        Assert.That(exception!.Message, Does.Contain("company.not_found"));
    }

    [Test]
    public void Reading_the_error_of_a_success_throws()
    {
        var result = Result<int>.Ok(42);

        Assert.Throws<InvalidOperationException>(() => _ = result.Error);
    }

    [Test]
    public void Values_and_errors_convert_implicitly()
    {
        Result<int> success = 7;
        Result<int> failure = NotFound;

        Assert.That(success.Value, Is.EqualTo(7));
        Assert.That(failure.Error, Is.EqualTo(NotFound));
    }

    [Test]
    public void An_error_without_fields_has_an_empty_list()
    {
        Assert.That(NotFound.Fields, Is.Empty);
        Assert.That(default(Error).Fields, Is.Empty);
    }

    [Test]
    public void A_validation_error_lists_its_fields()
    {
        var error = Error.Validation([new FieldError("email", "El correo es obligatorio.")]);

        Assert.That(error.Type, Is.EqualTo(ErrorType.Validation));
        Assert.That(error.Code, Is.EqualTo("validation.failed"));
        Assert.That(error.Fields, Is.EqualTo(new[] { new FieldError("email", "El correo es obligatorio.") }));
    }
}
