namespace ImpiFact.Application.Abstractions.Notifications;

/// <summary>
/// Avisos del panel para los usuarios de una empresa (<c>notifications</c>). El texto lo ven los usuarios: va en
/// español y nunca lleva datos sensibles (secretos, errores técnicos completos).
/// </summary>
public interface ICompanyNotifications
{
    /// <summary>Agrega un aviso para todos los usuarios de la empresa; se guarda con el próximo <c>SaveChangesAsync</c>.</summary>
    /// <param name="type">Tipo del aviso, en inglés y estable (por ejemplo <c>DocumentUnconfirmed</c>).</param>
    /// <exception cref="InvalidOperationException">Si la solicitud es de otra empresa.</exception>
    void Add(Guid companyId, string type, string title, string body);
}
