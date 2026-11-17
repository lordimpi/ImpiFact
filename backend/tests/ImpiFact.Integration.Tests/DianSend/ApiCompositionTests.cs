using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Messaging;
using ImpiFact.Infrastructure.Tenancy;
using ImpiFact.Integration.Tests.Protection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace ImpiFact.Integration.Tests.DianSend;

/// <summary>
/// La API no procesa la bandeja de salida (outbox): los handlers de los mensajes, como el envío a la DIAN, solo se
/// registran en el Worker (<c>AddWorkerServices</c>). Si la API los registrara, sus dependencias del Worker quedarían
/// expuestas en la API sin que nadie las use.
/// </summary>
[TestFixture]
[Category("Integration")]
public sealed class ApiCompositionTests
{
    [Test]
    public async Task The_api_registers_no_outbox_message_handler()
    {
        using var scratch = new ProtectionScratch();
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder
            .UseSetting("Database:ConnectionString", UnreachableDatabase.ConnectionString)
            .UseSetting("Secrets:LocalAesGcm:MasterKeyFile", scratch.MasterKeyFile)
            .UseSetting("Secrets:LocalAesGcm:CertificateDirectory", scratch.CertificateDirectory)
            .UseSetting("Storage:Local:Root", Path.Combine(scratch.Root, "storage")));
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(Guid.CreateVersion7(), userId: null, ActorType.System);

        var handlers = scope.ServiceProvider.GetServices<IOutboxMessageHandler>();

        Assert.That(handlers, Is.Empty);
    }
}
