using FluentValidation;
using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Handlers;
using ImpiFact.Application.Abstractions.Persistence;
using ImpiFact.Application.Abstractions.Results;
using Microsoft.Extensions.Logging;

namespace ImpiFact.Application.Tests.Behaviors;

// Solicitudes de prueba: un comando que exige empresa, una consulta y un comando que no exige empresa.
public sealed record CreateThing(string Name) : ICommand;

public sealed record GetThing(string Name);

public sealed record SignIn(string Name) : ICommand, ICompanyOptional;

/// <summary>Lo que pasó en orden: llamadas al handler y a la unidad de trabajo.</summary>
public sealed class Journal
{
    public List<string> Entries { get; } = [];
}

/// <summary>Qué hace el handler de prueba: salir bien, fallar o lanzar una excepción.</summary>
public enum HandlerOutcome
{
    Succeed,
    Fail,
    Throw,
}

public sealed class HandlerScript
{
    public HandlerOutcome Outcome { get; set; }

    public static readonly Error Failure = new("thing.conflict", "La cosa ya existe.", ErrorType.Conflict);
}

public sealed class RecordingHandler<TIn>(Journal journal, HandlerScript script) : IHandler<TIn, string>
{
    public Task<Result<string>> HandleAsync(TIn request, CancellationToken ct)
    {
        journal.Entries.Add("handle");
        return script.Outcome switch
        {
            HandlerOutcome.Succeed => Task.FromResult(Result<string>.Ok("done")),
            HandlerOutcome.Fail => Task.FromResult(Result<string>.Fail(HandlerScript.Failure)),
            _ => throw new InvalidOperationException("Handler exploded."),
        };
    }
}

public sealed class FakeTenant : ITenantContext
{
    public Guid? CompanyId { get; set; }

    public Guid? UserId { get; set; }

    public ActorType Actor { get; set; } = ActorType.ApiKey;
}

public sealed class FakeUnitOfWork(Journal journal) : IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken ct)
    {
        journal.Entries.Add("save");
        return Task.FromResult(1);
    }

    public Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken ct)
    {
        journal.Entries.Add("begin");
        return Task.FromResult<IUnitOfWorkTransaction>(new FakeTransaction(journal));
    }

    private sealed class FakeTransaction(Journal journal) : IUnitOfWorkTransaction
    {
        public Task CommitAsync(CancellationToken ct)
        {
            journal.Entries.Add("commit");
            return Task.CompletedTask;
        }

        public Task RollbackAsync(CancellationToken ct)
        {
            journal.Entries.Add("rollback");
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            journal.Entries.Add("dispose");
            return ValueTask.CompletedTask;
        }
    }
}

public sealed class CreateThingValidator : AbstractValidator<CreateThing>
{
    public CreateThingValidator()
    {
        RuleFor(request => request.Name).NotEmpty().WithMessage("El nombre es obligatorio.");
    }
}

/// <summary>Guarda cada mensaje de registro con su nivel, ya formateado.</summary>
public sealed class LogSink
{
    public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = [];
}

public sealed class CapturingLogger<T>(LogSink sink) : ILogger<T>
{
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        sink.Entries.Add((logLevel, formatter(state, exception), exception));
}
