using System.Diagnostics;
using ImpiFact.Application.Abstractions.Handlers;
using ImpiFact.Application.Abstractions.Results;
using Microsoft.Extensions.Logging;

namespace ImpiFact.Application.Behaviors;

/// <summary>
/// Primer decorador (el más externo): registra cada caso de uso con su duración y, si falla, el código
/// del error. Nunca registra el contenido de la solicitud, que puede traer contraseñas, PIN o certificados.
/// </summary>
internal sealed class LoggingDecorator<TIn, TOut>(IHandler<TIn, TOut> inner, ILogger<LoggingDecorator<TIn, TOut>> logger)
    : IHandler<TIn, TOut>
{
    private static readonly string UseCase = typeof(TIn).Name;

    public async Task<Result<TOut>> HandleAsync(TIn request, CancellationToken ct)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            var result = await inner.HandleAsync(request, ct);
            var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            if (result.IsSuccess)
            {
                logger.LogInformation("{UseCase} succeeded in {ElapsedMs:0} ms", UseCase, elapsed);
            }
            else
            {
                logger.LogWarning("{UseCase} failed with {ErrorCode} in {ElapsedMs:0} ms", UseCase, result.Error.Code, elapsed);
            }

            return result;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "{UseCase} threw after {ElapsedMs:0} ms", UseCase, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            throw;
        }
    }
}
