using System.Collections.Concurrent;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace ImpiFact.Integration.Tests.Observability;

/// <summary>Recibe en un puerto local lo que llegue por POST y guarda el cuerpo por ruta.</summary>
internal sealed class OtlpReceiver : IAsyncDisposable
{
    private readonly WebApplication app;
    private readonly ConcurrentDictionary<string, ConcurrentQueue<byte[]>> bodies = new();

    private OtlpReceiver(WebApplication app) => this.app = app;

    public string Endpoint => app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();

    public static async Task<OtlpReceiver> StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var app = builder.Build();
        var receiver = new OtlpReceiver(app);
        app.MapPost("/{**path}", async (HttpContext context) =>
        {
            using var buffer = new MemoryStream();
            await context.Request.Body.CopyToAsync(buffer);
            receiver.bodies.GetOrAdd(context.Request.Path.Value!, _ => new()).Enqueue(buffer.ToArray());
            return Results.Ok();
        });
        await app.StartAsync();
        return receiver;
    }

    /// <summary>Los cuerpos recibidos en la ruta, como texto (los textos del protobuf viajan en UTF-8).</summary>
    public string BodyOf(string path) =>
        bodies.TryGetValue(path, out var received) ? string.Concat(received.Select(body => Encoding.UTF8.GetString(body))) : "";

    public async ValueTask DisposeAsync() => await app.DisposeAsync();
}
