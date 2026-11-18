using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Companies;
using ImpiFact.Application.Abstractions.Storage;
using ImpiFact.Domain.Companies;
using ImpiFact.Infrastructure.Persistence;
using ImpiFact.Infrastructure.Platform;
using Microsoft.EntityFrameworkCore;

namespace ImpiFact.Infrastructure.Companies;

/// <summary>
/// Logo de la empresa: la fila de <c>company_logos</c> (una por empresa, con la clave del archivo y su tipo) y el archivo en
/// el almacén (<see cref="IBlobStorage"/>), con la clave <c>{empresa}/company/logo-{id}.{png|jpg}</c>.
/// </summary>
/// <remarks>
/// Cada subida usa una clave nueva y la fila pasa a apuntarle en la transacción del comando: si el guardado falla, la fila
/// sigue apuntando al archivo anterior, que no se tocó. El archivo reemplazado queda huérfano en el almacén, como los de
/// <c>document_files</c> (la base no sabe si la transacción se confirmó antes de que termine el comando). Quitar el logo
/// borra la fila y su archivo; si el archivo ya no está, se lee como si la empresa no tuviera logo.
/// </remarks>
internal sealed class EfCompanyLogos(ImpiFactDbContext db, IBlobStorage storage, ITenantContext tenant) : ICompanyLogos
{
    public async Task<CompanyLogoImage?> FindAsync(Guid companyId, CancellationToken ct)
    {
        var row = await db.Set<CompanyLogo>().AsNoTracking()
            .Where(logo => logo.CompanyId == companyId)
            .Select(logo => new { logo.StorageKey, logo.ContentType })
            .FirstOrDefaultAsync(ct);
        if (row is null)
        {
            return null;
        }

        try
        {
            await using var stream = await storage.OpenReadAsync(row.StorageKey, ct);
            using var copy = new MemoryStream();
            await stream.CopyToAsync(copy, ct);
            return new CompanyLogoImage(copy.ToArray(), row.ContentType);
        }
        catch (BlobNotFoundException)
        {
            return null;
        }
    }

    public async Task SaveAsync(Guid companyId, CompanyLogoImage logo, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(logo);
        // Se revisa antes de escribir el archivo, para no dejar en el almacén archivos de otra empresa.
        TenantWriteCheck.EnsureCanWrite(tenant, companyId, nameof(CompanyLogo));

        var extension = logo.ContentType == CompanyLogoFormat.Jpeg ? "jpg" : "png";
        var key = $"{companyId:N}/company/logo-{Guid.CreateVersion7():N}.{extension}";
        using (var content = new MemoryStream(logo.Content, writable: false))
        {
            await storage.PutAsync(key, content, logo.ContentType, ct);
        }

        if (await db.Set<CompanyLogo>().FirstOrDefaultAsync(row => row.CompanyId == companyId, ct) is { } current)
        {
            current.StorageKey = key;
            current.ContentType = logo.ContentType;
        }
        else
        {
            db.Add(new CompanyLogo { CompanyId = companyId, StorageKey = key, ContentType = logo.ContentType });
        }
    }

    public async Task DeleteAsync(Guid companyId, CancellationToken ct)
    {
        if (await db.Set<CompanyLogo>().FirstOrDefaultAsync(row => row.CompanyId == companyId, ct) is not { } current)
        {
            return;
        }

        TenantWriteCheck.EnsureCanWrite(tenant, companyId, nameof(CompanyLogo));
        db.Remove(current);
        await storage.DeleteAsync(current.StorageKey, ct);
    }
}
