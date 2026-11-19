using System.Text.Json;
using ImpiFact.Application.Abstractions.Messaging;
using ImpiFact.Infrastructure.Observability;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging;

namespace ImpiFact.Integration.Tests.DianSend;

/// <summary>
/// Lo que registra un handler de la bandeja de salida del Worker lleva el mensaje que lo originó: su id, su tipo y la
/// empresa (Arquitectura detallada, sección 12), en las mismas líneas JSON de Serilog que la API.
/// </summary>
[TestFixture]
[Category("Integration")]
public sealed class WorkerLoggingTests
{
    [Test]
    public async Task Handler_logs_carry_the_outbox_message_id_type_and_company()
    {
        var output = new StringWriter();
        await using var host = await DianSendHost.StartAsync(configureServices: services =>
        {
            services.AddSingleton(new LogOutput(TextWriter.Synchronized(output)));
            services.AddLogging(logging => logging.AddImpiFactSerilog(
                new ConfigurationBuilder().Build(), new HostingEnvironment { EnvironmentName = Environments.Production }, "impifact-worker"));
        });
        var issuer = await host.CreateIssuerAsync();
        var document = await host.CreateDocumentAsync(issuer);
        host.Dian.Reply(DianReplies.SendBillSync(true, "00"));

        await host.RunOutboxAsync();

        var message = (await host.MessagesAsync(issuer.CompanyId, OutboxMessageTypes.SendDocumentToDian)).Single();
        var line = output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(text => JsonDocument.Parse(text).RootElement)
            .Single(entry => entry.GetProperty("@m").GetString()!.StartsWith("DIAN \"SendBillSync\" for document", StringComparison.Ordinal)
                && entry.GetProperty("DocumentId").GetString() == document.Id.ToString());
        Assert.Multiple(() =>
        {
            Assert.That(line.GetProperty("OutboxMessageId").GetString(), Is.EqualTo(message.Id.ToString()));
            Assert.That(line.GetProperty("OutboxMessageType").GetString(), Is.EqualTo(OutboxMessageTypes.SendDocumentToDian));
            Assert.That(line.GetProperty("CompanyId").GetString(), Is.EqualTo(issuer.CompanyId.ToString()));
            Assert.That(line.GetProperty("SourceContext").GetString(), Does.EndWith("DianDelivery"));
        });
    }
}
