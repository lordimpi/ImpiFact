using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace ImpiFact.Integration.Tests.DianSend;

/// <summary>
/// Proveedor de logs de prueba: guarda cada mensaje ya formateado, con su excepción completa, para revisar que los logs
/// nunca lleven un secreto.
/// </summary>
public sealed class CapturedLogs : ILoggerProvider
{
    private readonly ConcurrentQueue<string> entries = new();

    /// <summary>Cada entrada: nivel, categoría, mensaje y la excepción con sus excepciones internas.</summary>
    public IReadOnlyList<string> Entries => [.. entries];

    public ILogger CreateLogger(string categoryName) => new Logger(categoryName, entries);

    public void Dispose()
    {
    }

    private sealed class Logger(string category, ConcurrentQueue<string> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            entries.Enqueue($"{logLevel} {category}: {formatter(state, exception)} {exception}");
    }
}
