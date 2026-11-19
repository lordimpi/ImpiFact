using System.Data.Common;
using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Documents;
using ImpiFact.Domain.Documents;
using ImpiFact.Infrastructure.Persistence;
using ImpiFact.Infrastructure.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ImpiFact.Infrastructure.Documents;

/// <summary>
/// Consecutivos sobre el contexto de EF Core (Entity Framework, el mapeador de objetos a tablas) de la solicitud.
/// Crear o adelantar un consecutivo es un cambio normal que guarda el decorador de transacción; tomar y reservar
/// números usan el UPDATE atómico del motor (<see cref="INumberingSql"/>) sobre la misma conexión y la misma
/// transacción del contexto, así el número vuelve a quedar libre si la transacción se revierte. Adelantar un consecutivo
/// a un número pedido (<see cref="INumberingAdvance"/>) usa el mismo UPDATE de la reserva.
/// </summary>
internal sealed class EfNumberingSequences(ImpiFactDbContext db, INumberingSql sql, ITenantContext tenant, IClock clock)
    : INumberingSequences, INumberingAdvance
{
    public Task OpenForResolutionAsync(Guid companyId, Guid resolutionId, long firstNumber, CancellationToken ct) =>
        OpenAsync(companyId, NumberingScope.ForResolution(resolutionId), firstNumber, ct);

    public async Task OpenAsync(Guid companyId, string scopeKey, long firstNumber, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeKey);
        var sequence = db.Set<NumberingSequence>().Local
                .FirstOrDefault(row => row.CompanyId == companyId && row.ScopeKey == scopeKey)
            ?? await db.Set<NumberingSequence>()
                .IgnoreQueryFilters([QueryFilters.Tenant])
                .FirstOrDefaultAsync(row => row.CompanyId == companyId && row.ScopeKey == scopeKey, ct);
        if (sequence is null)
        {
            db.Set<NumberingSequence>().Add(new NumberingSequence { CompanyId = companyId, ScopeKey = scopeKey, NextNumber = firstNumber });
        }
        else
        {
            sequence.RaiseTo(firstNumber);
        }
    }

    public Task<long?> TakeNextAsync(Guid companyId, string scopeKey, CancellationToken ct)
    {
        var (connection, transaction) = CurrentTransaction(companyId);
        return sql.TakeNextAsync(connection, transaction, companyId, scopeKey, clock.UtcNow, ct);
    }

    public async Task<NumberReservation> ReserveAsync(Guid companyId, string scopeKey, DocumentNumberKey number, CancellationToken ct)
    {
        var (connection, transaction) = CurrentTransaction(companyId);
        var next = await sql.RaiseAboveAsync(connection, transaction, companyId, scopeKey, number.Number, clock.UtcNow, ct);
        if (next is null)
        {
            return NumberReservation.SequenceNotFound;
        }

        // Va después del UPDATE a propósito: la fila del consecutivo ya está bloqueada, así otra reserva del mismo
        // número espera a que esta transacción termine y luego ve el documento que esta guardó.
        var used = await db.Set<Document>()
            .IgnoreQueryFilters([QueryFilters.Tenant])
            .AnyAsync(document => document.CompanyId == companyId
                && document.DocumentTypeId == number.DocumentTypeId
                && document.TypeEnvironmentId == number.TypeEnvironmentId
                && document.Prefix == number.Prefix
                && document.Number == number.Number, ct);
        return used ? NumberReservation.AlreadyUsed : NumberReservation.Reserved;
    }

    public Task<long?> AdvanceToAsync(Guid companyId, string scopeKey, long nextNumber, CancellationToken ct)
    {
        // max(next_number, número + 1) con número = nextNumber - 1 deja max(next_number, nextNumber): es la misma sentencia
        // de la reserva, sin una segunda forma de mover el consecutivo.
        var (connection, transaction) = CurrentTransaction(companyId);
        return sql.RaiseAboveAsync(connection, transaction, companyId, scopeKey, nextNumber - 1, clock.UtcNow, ct);
    }

    private (DbConnection Connection, DbTransaction Transaction) CurrentTransaction(Guid companyId)
    {
        TenantWriteCheck.EnsureCanWrite(tenant, companyId, "Numbering sequence");
        var transaction = db.Database.CurrentTransaction
            ?? throw new InvalidOperationException("Taking or reserving a document number requires an open transaction.");
        return (db.Database.GetDbConnection(), transaction.GetDbTransaction());
    }
}
