using System.Text.Json;
using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Messaging;
using ImpiFact.Domain.Platform;
using ImpiFact.Infrastructure.Persistence;
using ImpiFact.Infrastructure.Persistence.Conversions;

namespace ImpiFact.Infrastructure.Platform;

/// <summary>
/// Bandeja de salida sobre EF Core (Entity Framework, el mapeador de objetos a tablas): agrega la fila de
/// <c>outbox_messages</c> al contexto de la solicitud, así el decorador de transacción la guarda y la confirma junto
/// con el resto de los cambios, o la descarta si la transacción se revierte. El procesador que toma los mensajes es
/// del Worker (fase 4).
/// </summary>
internal sealed class EfOutbox(ImpiFactDbContext db, ITenantContext tenant, IClock clock) : IOutbox
{
    public Task<Guid> EnqueueAsync<TPayload>(
        string type, TPayload payload, Guid? companyId, DateTimeOffset? notBefore, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        TenantWriteCheck.EnsureCanWrite(tenant, companyId, $"Outbox message '{type}'");

        // Mismas opciones fijas de las columnas JSON: nombres en snake_case y el mismo texto en todos los motores.
        var node = JsonSerializer.SerializeToNode(payload, JsonColumn.SerializerOptions)
            ?? throw new ArgumentNullException(nameof(payload), "An outbox message needs a payload.");
        var now = clock.UtcNow;
        var message = new OutboxMessage
        {
            Id = Guid.CreateVersion7(),
            CompanyId = companyId,
            Type = type,
            Payload = node,
            Status = OutboxStatus.Pending,
            NextAttemptAt = notBefore ?? now,
            CreatedAt = now,
        };
        db.Set<OutboxMessage>().Add(message);
        return Task.FromResult(message.Id);
    }
}
