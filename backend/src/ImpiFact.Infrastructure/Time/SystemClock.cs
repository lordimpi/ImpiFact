using ImpiFact.Application.Abstractions;

namespace ImpiFact.Infrastructure.Time;

/// <summary>Reloj real del sistema, en UTC (tiempo universal coordinado, sin desfase horario).</summary>
internal sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
