using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace ImpiFact.Infrastructure.Persistence.Conversions;

/// <summary>
/// Guarda todo <see cref="DateTimeOffset"/> como instante UTC (tiempo universal coordinado, desfase 0) y lo
/// devuelve con desfase 0 (Modelo de datos detallado, principio 7 y sección 8, punto 5). PostgreSQL rechaza
/// desfases distintos de cero y MySQL no guarda el desfase; así los tres motores se comportan igual. La hora
/// Colombia que va al XML se calcula aparte, no sale de esta columna.
/// </summary>
internal sealed class UtcDateTimeOffsetConverter() : ValueConverter<DateTimeOffset, DateTimeOffset>(
    value => value.ToUniversalTime(),
    value => value.ToUniversalTime());
