using System.Text.Json.Serialization;
using ImpiFact.Application.Abstractions.Results;

namespace ImpiFact.Application.Features.Notifications;

// Respuestas de /api/v1/notifications. Nunca se expone la entidad (Arquitectura detallada, sección 4.2); los nombres
// JSON van en snake_case como el resto de la API.

/// <summary>Un aviso de la campana del panel.</summary>
public sealed record NotificationResponse
{
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    /// <summary>Clase de aviso, estable para el código del panel (por ejemplo <c>certificate_expiry</c>).</summary>
    [JsonPropertyName("type")]
    public string Type { get; init; } = "";

    [JsonPropertyName("title")]
    public string Title { get; init; } = "";

    [JsonPropertyName("body")]
    public string Body { get; init; } = "";

    [JsonPropertyName("is_read")]
    public bool IsRead { get; init; }

    /// <summary>Cuándo se creó el aviso (UTC).</summary>
    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>Una página de avisos, del más reciente al más antiguo, con el total sin leer.</summary>
public sealed record NotificationListResponse
{
    [JsonPropertyName("items")]
    public IReadOnlyList<NotificationResponse> Items { get; init; } = [];

    /// <summary>Texto opaco para pedir la página siguiente (<c>?cursor=</c>); nulo en la última.</summary>
    [JsonPropertyName("next_cursor")]
    public string? NextCursor { get; init; }

    /// <summary>Avisos sin leer de la empresa visibles para quien consulta, sin importar el filtro ni la página.</summary>
    [JsonPropertyName("unread_count")]
    public long UnreadCount { get; init; }
}

/// <summary>Resultado de marcar avisos como leídos.</summary>
public sealed record NotificationsMarkedResponse
{
    /// <summary>
    /// Cuántos avisos pasaron de sin leer a leídos; 0 si ya estaban todos leídos. Marcar un solo aviso responde 204 sin
    /// cuerpo.
    /// </summary>
    [JsonPropertyName("marked_count")]
    public int MarkedCount { get; init; }
}

internal static class NotificationErrors
{
    /// <summary>El aviso no existe, es de otra empresa o está dirigido a otro usuario: los tres responden igual.</summary>
    public static readonly Error NotFound = new("notification.not_found", "La notificación no existe.", ErrorType.NotFound);
}

/// <summary>Mensajes de validación con las mismas frases del resto de las consultas de la API.</summary>
internal static class NotificationMessages
{
    public static string Invalid(string field) => $"{field} es inválido.";

    public static string MinNumber(string field, int min) => $"El campo {field} debe ser como mínimo {min}.";

    public static string MaxNumber(string field, int max) => $"El campo {field} debe ser como máximo {max}.";
}
