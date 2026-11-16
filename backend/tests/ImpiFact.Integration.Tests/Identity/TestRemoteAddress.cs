using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace ImpiFact.Integration.Tests.Identity;

/// <summary>
/// El servidor de pruebas en memoria no tiene una dirección IP (Internet Protocol) de origen. Este filtro de
/// arranque, que corre antes de todo el pipeline de la API, toma la dirección del encabezado de prueba
/// <see cref="HeaderName"/> y la pone como dirección de la conexión, como si la solicitud llegara desde esa IP
/// (por ejemplo, desde un proxy).
/// </summary>
internal sealed class TestRemoteAddress : IStartupFilter
{
    public const string HeaderName = "X-Test-Remote-Ip";

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use((context, nextMiddleware) =>
        {
            if (IPAddress.TryParse(context.Request.Headers[HeaderName].ToString(), out var address))
            {
                context.Connection.RemoteIpAddress = address;
            }

            return nextMiddleware(context);
        });
        next(app);
    };
}
