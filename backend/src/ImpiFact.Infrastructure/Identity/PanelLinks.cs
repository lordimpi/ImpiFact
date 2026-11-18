using ImpiFact.Application.Abstractions.Identity;
using ImpiFact.Infrastructure.Options;

namespace ImpiFact.Infrastructure.Identity;

/// <summary>Enlaces al panel con <see cref="PanelOptions.BaseUrl"/>, conservando su ruta base si la tiene.</summary>
internal sealed class PanelLinks(IOptionsService<PanelOptions> options) : IPanelLinks
{
    /// <summary>Ruta de la página del panel que pide la contraseña nueva (Arquitectura frontend, sección 7).</summary>
    internal const string PasswordResetPath = "restablecer";

    public Uri PasswordReset(string token) =>
        new($"{options.Value.BaseUrl.TrimEnd('/')}/{PasswordResetPath}?token={Uri.EscapeDataString(token)}", UriKind.Absolute);
}
