using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using ImpiFact.Application.Abstractions.Observability;
using Microsoft.Extensions.DependencyInjection;

namespace ImpiFact.Integration.Tests.Observability;

/// <summary>Una medición de un instrumento: su valor y sus etiquetas.</summary>
public sealed record Measurement(string Instrument, double Value, IReadOnlyDictionary<string, string?> Tags)
{
    public string? Tag(string name) => Tags.TryGetValue(name, out var value) ? value : null;
}

/// <summary>
/// Escucha las métricas de <see cref="ImpiFactMetrics.MeterName"/> de un contenedor de servicios con un
/// <see cref="MeterListener"/>. Solo las del <see cref="IMeterFactory"/> de ese contenedor: las de otras pruebas que
/// corren a la vez no se mezclan.
/// </summary>
public sealed class MetricRecorder : IDisposable
{
    private readonly MeterListener listener = new();
    private readonly ConcurrentQueue<Measurement> measurements = new();

    public MetricRecorder(IServiceProvider services)
    {
        var factory = services.GetRequiredService<IMeterFactory>();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == ImpiFactMetrics.MeterName && ReferenceEquals(instrument.Meter.Scope, factory))
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) => Add(instrument, value, tags));
        listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) => Add(instrument, value, tags));
        listener.Start();
    }

    public IReadOnlyList<Measurement> Measurements => [.. measurements];

    public IEnumerable<Measurement> Of(string instrument) => Measurements.Where(measurement => measurement.Instrument == instrument);

    /// <summary>Suma de las mediciones de un contador con la etiqueta dada (o todas).</summary>
    public double Sum(string instrument, string? tag = null, string? value = null) =>
        Of(instrument).Where(measurement => tag is null || measurement.Tag(tag) == value).Sum(measurement => measurement.Value);

    public void Dispose() => listener.Dispose();

    private void Add(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        var copy = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var (key, tagValue) in tags)
        {
            copy[key] = tagValue?.ToString();
        }

        measurements.Enqueue(new Measurement(instrument.Name, value, copy));
    }
}
