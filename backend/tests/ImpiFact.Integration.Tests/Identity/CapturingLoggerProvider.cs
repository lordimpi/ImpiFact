using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace ImpiFact.Integration.Tests.Identity;

/// <summary>
/// Proveedor de registros (logs) que guarda en memoria todo lo que se escribe: el mensaje ya armado, el estado,
/// la excepción y los ámbitos (scopes). Sirve para comprobar que ningún registro contiene una credencial.
/// </summary>
internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<string> _entries = new();

    public IReadOnlyCollection<string> Entries => _entries;

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, _entries);

    public void Dispose()
    {
    }

    private sealed class CapturingLogger(string category, ConcurrentQueue<string> entries) : ILogger
    {
        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull
        {
            entries.Enqueue($"{category} scope: {Describe(state)}");
            return NullScope.Instance;
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            entries.Enqueue($"{logLevel} {category}: {formatter(state, exception)} | {Describe(state)} | {exception}");
        }

        private static string Describe(object? state) => state switch
        {
            IEnumerable<KeyValuePair<string, object?>> pairs => string.Join(", ", pairs.Select(pair => $"{pair.Key}={pair.Value}")),
            _ => state?.ToString() ?? "",
        };
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose()
        {
        }
    }
}
