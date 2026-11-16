namespace ImpiFact.Application.Abstractions;

/// <summary>
/// La hora actual. Los casos de uso la piden a este puerto en lugar de usar DateTimeOffset.UtcNow, para
/// que las pruebas puedan fijarla.
/// </summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
