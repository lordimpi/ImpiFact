using System.Diagnostics;

namespace ImpiFact.Infrastructure.Observability;

/// <summary>
/// Trazas propias de ImpiFact (<see cref="SourceName"/>): por ahora, un tramo (span) por cada mensaje de la bandeja de
/// salida que procesa el Worker, para que las llamadas a la DIAN, a Gotenberg y a la base de ese mensaje queden en una
/// misma traza.
/// </summary>
internal static class ImpiFactActivities
{
    public const string SourceName = "ImpiFact";

    public static readonly ActivitySource Source = new(SourceName);
}
