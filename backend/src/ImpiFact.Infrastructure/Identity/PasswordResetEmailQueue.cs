using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Identity;
using ImpiFact.Application.Abstractions.Messaging;
using ImpiFact.Infrastructure.Persistence;
using ImpiFact.Infrastructure.Platform;

namespace ImpiFact.Infrastructure.Identity;

/// <summary>
/// Adaptador de <see cref="IPasswordResetEmailQueue"/>: escribe con la misma bandeja de salida de siempre
/// (<see cref="EfOutbox"/>, sobre el contexto de la solicitud, así entra en su transacción), pero actuando como el
/// sistema, porque la solicitud anónima no puede escribir mensajes sin empresa. Solo escribe el tipo
/// <c>SendPasswordResetEmail</c> y siempre sin empresa.
/// </summary>
internal sealed class PasswordResetEmailQueue(ImpiFactDbContext db, IClock clock) : IPasswordResetEmailQueue
{
    public Task EnqueueAsync(SendPasswordResetEmailPayload payload, CancellationToken ct) =>
        new EfOutbox(db, SystemWriter.Instance, clock)
            .EnqueueAsync(OutboxMessageTypes.SendPasswordResetEmail, payload, companyId: null, notBefore: null, ct);

    /// <summary>El sistema, sin empresa ni usuario: el único actor que la bandeja de salida deja escribir sin empresa además de la plataforma.</summary>
    private sealed class SystemWriter : ITenantContext
    {
        public static SystemWriter Instance { get; } = new();

        public Guid? CompanyId => null;

        public Guid? UserId => null;

        public ActorType Actor => ActorType.System;

        public IReadOnlySet<string> Scopes { get; } = new HashSet<string>();
    }
}
