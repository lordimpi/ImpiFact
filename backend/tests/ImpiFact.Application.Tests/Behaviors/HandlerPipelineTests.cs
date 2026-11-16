using FluentValidation;
using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Handlers;
using ImpiFact.Application.Abstractions.Persistence;
using ImpiFact.Application.Abstractions.Results;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ImpiFact.Application.Tests.Behaviors;

/// <summary>
/// Prueba la cadena de decoradores tal como la arma <see cref="ApplicationServiceCollectionExtensions.AddApplication"/>:
/// Logging → Validation → Tenant → Transaction → Handler. Cada prueba mira qué llegó a ejecutarse y en qué orden.
/// </summary>
public class HandlerPipelineTests
{
    private Journal _journal = null!;
    private HandlerScript _script = null!;
    private FakeTenant _tenant = null!;
    private LogSink _logs = null!;
    private ServiceProvider _provider = null!;

    [SetUp]
    public void SetUp()
    {
        _journal = new Journal();
        _script = new HandlerScript();
        _tenant = new FakeTenant { CompanyId = Guid.NewGuid() };
        _logs = new LogSink();

        var services = new ServiceCollection();
        services.AddSingleton(_journal);
        services.AddSingleton(_script);
        services.AddSingleton(_logs);
        services.AddSingleton<ITenantContext>(_tenant);
        services.AddScoped<IUnitOfWork, FakeUnitOfWork>();
        services.AddSingleton(typeof(ILogger<>), typeof(CapturingLogger<>));
        services.AddScoped<IValidator<CreateThing>, CreateThingValidator>();
        services.AddScoped<IHandler<CreateThing, string>, RecordingHandler<CreateThing>>();
        services.AddScoped<IHandler<GetThing, string>, RecordingHandler<GetThing>>();
        services.AddScoped<IHandler<SignIn, string>, RecordingHandler<SignIn>>();
        services.AddApplication();
        // Sin ValidateOnBuild: AddApplication registra también los handlers reales, cuyos puertos (repositorios,
        // almacenes) no existen en esta prueba; aquí solo se prueba la cadena de decoradores con handlers de prueba.
        _provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    [TearDown]
    public void TearDown() => _provider.Dispose();

    [Test]
    public async Task A_successful_command_is_saved_and_committed_inside_a_transaction()
    {
        var result = await Handle(new CreateThing("factura"));

        Assert.That(result.Value, Is.EqualTo("done"));
        Assert.That(_journal.Entries, Is.EqualTo(new[] { "begin", "handle", "save", "commit", "dispose" }));
    }

    [Test]
    public async Task A_failed_command_is_rolled_back_without_saving()
    {
        _script.Outcome = HandlerOutcome.Fail;

        var result = await Handle(new CreateThing("factura"));

        Assert.That(result.Error, Is.EqualTo(HandlerScript.Failure));
        Assert.That(_journal.Entries, Is.EqualTo(new[] { "begin", "handle", "rollback", "dispose" }));
    }

    [Test]
    public void A_command_that_throws_releases_the_transaction_without_committing()
    {
        _script.Outcome = HandlerOutcome.Throw;

        Assert.ThrowsAsync<InvalidOperationException>(() => Handle(new CreateThing("factura")));

        Assert.That(_journal.Entries, Is.EqualTo(new[] { "begin", "handle", "dispose" }));
        Assert.That(_logs.Entries.Single().Level, Is.EqualTo(LogLevel.Error));
    }

    [Test]
    public async Task A_query_runs_without_a_transaction()
    {
        var result = await Handle(new GetThing("factura"));

        Assert.That(result.Value, Is.EqualTo("done"));
        Assert.That(_journal.Entries, Is.EqualTo(new[] { "handle" }));
    }

    [Test]
    public async Task A_request_without_a_company_is_rejected_before_the_transaction()
    {
        _tenant.CompanyId = null;

        var result = await Handle(new CreateThing("factura"));

        Assert.That(result.Error.Type, Is.EqualTo(ErrorType.Unauthorized));
        Assert.That(result.Error.Code, Is.EqualTo("tenant.company_required"));
        Assert.That(_journal.Entries, Is.Empty);
    }

    [Test]
    public async Task A_company_optional_request_runs_without_a_company()
    {
        _tenant.CompanyId = null;

        var result = await Handle(new SignIn("ana"));

        Assert.That(result.Value, Is.EqualTo("done"));
        Assert.That(_journal.Entries, Is.EqualTo(new[] { "begin", "handle", "save", "commit", "dispose" }));
    }

    [Test]
    public async Task Validation_runs_before_the_company_check_and_lists_the_fields()
    {
        _tenant.CompanyId = null;

        var result = await Handle(new CreateThing(""));

        Assert.That(result.Error.Type, Is.EqualTo(ErrorType.Validation));
        Assert.That(result.Error.Fields, Is.EqualTo(new[] { new FieldError("Name", "El nombre es obligatorio.") }));
        Assert.That(_journal.Entries, Is.Empty);
    }

    [Test]
    public async Task Logging_wraps_everything_and_records_the_error_code_without_the_request_content()
    {
        await Handle(new CreateThing("secret-value"));
        _script.Outcome = HandlerOutcome.Fail;
        await Handle(new CreateThing("secret-value"));
        await Handle(new CreateThing(""));

        Assert.That(_logs.Entries.Select(entry => entry.Level), Is.EqualTo(new[] { LogLevel.Information, LogLevel.Warning, LogLevel.Warning }));
        Assert.That(_logs.Entries[0].Message, Does.StartWith("CreateThing succeeded"));
        Assert.That(_logs.Entries[1].Message, Does.StartWith("CreateThing failed with thing.conflict"));
        Assert.That(_logs.Entries[2].Message, Does.StartWith("CreateThing failed with validation.failed"));
        Assert.That(_logs.Entries.Select(entry => entry.Message), Has.None.Contains("secret-value"));
    }

    private async Task<Result<string>> Handle<TIn>(TIn request)
    {
        await using var scope = _provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IHandler<TIn, string>>().HandleAsync(request, CancellationToken.None);
    }
}
