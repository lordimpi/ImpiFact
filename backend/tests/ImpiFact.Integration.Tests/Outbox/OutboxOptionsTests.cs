using ImpiFact.Infrastructure.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ImpiFact.Integration.Tests.Outbox;

/// <summary>
/// Opciones del procesador (sección «Outbox»): sin la sección valen los números del documento (Arquitectura
/// detallada, sección 11) y un valor fuera de rango detiene el arranque.
/// </summary>
[TestFixture]
[Category("Integration")]
public sealed class OutboxOptionsTests
{
    [Test]
    public async Task Without_an_outbox_section_the_documented_defaults_apply()
    {
        await using var host = OutboxHost.Create();

        var options = host.Services.GetRequiredService<IOptions<OutboxOptions>>().Value;

        Assert.Multiple(() =>
        {
            Assert.That(options.PollSeconds, Is.EqualTo(2));
            Assert.That(options.LockMinutes, Is.EqualTo(5));
            Assert.That(options.MaxAttempts, Is.EqualTo(5));
            Assert.That(options.BatchSize, Is.EqualTo(20));
        });
    }

    [TestCase(nameof(OutboxOptions.PollSeconds), "0")]
    [TestCase(nameof(OutboxOptions.LockMinutes), "0")]
    [TestCase(nameof(OutboxOptions.MaxAttempts), "0")]
    [TestCase(nameof(OutboxOptions.BatchSize), "0")]
    [TestCase(nameof(OutboxOptions.BatchSize), "1000")]
    public async Task An_out_of_range_value_is_rejected(string property, string value)
    {
        await using var host = OutboxHost.Create(new Dictionary<string, string?> { [$"Outbox:{property}"] = value });

        var exception = Assert.Throws<OptionsValidationException>(
            () => _ = host.Services.GetRequiredService<IOptions<OutboxOptions>>().Value);

        Assert.That(exception?.Message, Does.Contain(property));
    }
}
