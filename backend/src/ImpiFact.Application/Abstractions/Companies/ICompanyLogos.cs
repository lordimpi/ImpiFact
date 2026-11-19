namespace ImpiFact.Application.Abstractions.Companies;

/// <summary>Logo de una empresa: la imagen tal como se subió y su tipo (<c>image/png</c> o <c>image/jpeg</c>).</summary>
public sealed record CompanyLogoImage(byte[] Content, string ContentType);

/// <summary>
/// Logo de la empresa que va en la representación gráfica (el PDF) de sus documentos. Lo guarda el módulo Company
/// (<c>PUT /api/v1/companies/{nit}/logo</c>) y lo lee el PDF del módulo Documents. Infrastructure lo implementa con la
/// tabla <c>company_logos</c> (una fila por empresa con la clave del archivo y su tipo) y el almacén de archivos.
/// </summary>
/// <remarks>
/// <see cref="SaveAsync"/> y <see cref="DeleteAsync"/> escriben el archivo enseguida y dejan el cambio de la fila para el
/// guardado de la unidad de trabajo: quien los llama es un comando, que guarda en su transacción.
/// </remarks>
public interface ICompanyLogos
{
    /// <summary>El logo de la empresa; nulo si no tiene.</summary>
    Task<CompanyLogoImage?> FindAsync(Guid companyId, CancellationToken ct);

    /// <summary>Guarda el archivo del logo y deja lista la fila que reemplaza al anterior, si había.</summary>
    Task SaveAsync(Guid companyId, CompanyLogoImage logo, CancellationToken ct);

    /// <summary>Deja lista la baja de la fila del logo; no hace nada si la empresa no tiene.</summary>
    Task DeleteAsync(Guid companyId, CancellationToken ct);
}

/// <summary>Formatos de imagen aceptados para el logo, reconocidos por sus primeros bytes y no por lo que declare quien sube.</summary>
public static class CompanyLogoFormat
{
    public const string Png = "image/png";

    public const string Jpeg = "image/jpeg";

    /// <summary>Tamaño máximo del logo: 512 KiB (524.288 bytes).</summary>
    public const int MaxBytes = 512 * 1024;

    private static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static ReadOnlySpan<byte> JpegSignature => [0xFF, 0xD8, 0xFF];

    /// <summary>
    /// <see cref="Png"/> o <see cref="Jpeg"/> según la firma del archivo; nulo para cualquier otro contenido (SVG, GIF,
    /// WebP…). El SVG no se acepta porque es un documento con código, no una imagen de píxeles.
    /// </summary>
    public static string? Detect(ReadOnlySpan<byte> content) =>
        content.StartsWith(PngSignature) ? Png
        : content.StartsWith(JpegSignature) ? Jpeg
        : null;
}
