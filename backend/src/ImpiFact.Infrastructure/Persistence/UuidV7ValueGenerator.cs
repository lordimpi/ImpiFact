using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.ValueGeneration;

namespace ImpiFact.Infrastructure.Persistence;

/// <summary>
/// Genera en la aplicación las llaves <c>uuid</c> versión 7 (identificadores únicos que empiezan por la hora,
/// así quedan casi ordenados y los índices no se fragmentan). EF lo llama al agregar la entidad, antes del INSERT,
/// solo si la llave está vacía.
/// </summary>
internal sealed class UuidV7ValueGenerator : ValueGenerator<Guid>
{
    /// <summary>El valor es definitivo, no uno temporal que la base reemplace al guardar.</summary>
    public override bool GeneratesTemporaryValues => false;

    public override Guid Next(EntityEntry entry) => Guid.CreateVersion7();
}
