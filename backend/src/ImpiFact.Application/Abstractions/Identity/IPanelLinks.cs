namespace ImpiFact.Application.Abstractions.Identity;

/// <summary>
/// Enlaces al panel web que van en los correos, armados con la dirección pública del panel (<c>Panel:BaseUrl</c>). Lo
/// implementa Infrastructure; solo lo registra el Worker, que es quien envía los correos.
/// </summary>
public interface IPanelLinks
{
    /// <summary>
    /// Página del panel para elegir la contraseña nueva: <c>{Panel:BaseUrl}/restablecer?token=...</c>, con el token
    /// escapado para la URL. El enlace lleva un secreto: nunca se escribe en los registros.
    /// </summary>
    Uri PasswordReset(string token);
}
