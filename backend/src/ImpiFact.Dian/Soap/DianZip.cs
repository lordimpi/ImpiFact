using System.IO.Compression;
using System.Text;

namespace ImpiFact.Dian.Soap;

/// <summary>
/// Empaqueta un XML firmado en el ZIP (archivo comprimido) que se envía a la DIAN (la autoridad tributaria
/// de Colombia) dentro del parámetro <c>contentFile</c> de la operación SOAP, codificado en Base64 (texto
/// que representa bytes).
/// </summary>
/// <remarks>
/// Todo ocurre en memoria: no se lee ni se escribe ningún archivo. La fecha de modificación de la entrada
/// se recibe como parámetro (no se toma del reloj), así que la misma entrada produce siempre los mismos bytes.
/// </remarks>
public static class DianZip
{
    private static readonly UTF8Encoding Utf8WithoutBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// Crea un ZIP con una sola entrada <paramref name="entryName"/> cuyo contenido son exactamente los bytes
    /// de <paramref name="content"/>.
    /// </summary>
    /// <param name="entryName">Nombre del XML dentro del ZIP (ver <see cref="DianFileNames.XmlEntry"/>).</param>
    /// <param name="content">Bytes del XML firmado.</param>
    /// <param name="modifiedAt">Fecha de modificación que se guarda en la entrada (entre 1980 y 2107).</param>
    public static byte[] Create(string entryName, byte[] content, DateTimeOffset modifiedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entryName);
        ArgumentNullException.ThrowIfNull(content);

        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
            entry.LastWriteTime = modifiedAt;
            using var stream = entry.Open();
            stream.Write(content);
        }

        return buffer.ToArray();
    }

    /// <summary>
    /// Igual que <see cref="Create(string, byte[], DateTimeOffset)"/>, con el XML como texto: se guarda en
    /// UTF-8 sin BOM (la marca de orden de bytes al inicio del archivo), que son los bytes que se firmaron.
    /// </summary>
    public static byte[] Create(string entryName, string xml, DateTimeOffset modifiedAt)
    {
        ArgumentNullException.ThrowIfNull(xml);
        return Create(entryName, Utf8WithoutBom.GetBytes(xml), modifiedAt);
    }

    /// <summary>
    /// A ZIP with several XML, in the given order, each with exactly its bytes: the batch of SendBillAsync (annex 1.9, section
    /// 6.8: up to 50 documents, and «fv», «nc» and «nd» may go in the same ZIP).
    /// </summary>
    /// <exception cref="ArgumentException">Without entries, or with two entries of the same name.</exception>
    public static byte[] Create(IReadOnlyList<DianZipEntry> entries, DateTimeOffset modifiedAt)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (entries.Count == 0)
        {
            throw new ArgumentException("A ZIP needs at least one entry.", nameof(entries));
        }

        if (entries.Select(entry => entry.Name).Distinct(StringComparer.Ordinal).Count() != entries.Count)
        {
            throw new ArgumentException("Two entries of a ZIP have the same name.", nameof(entries));
        }

        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var item in entries)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(item.Name, nameof(entries));
                var entry = archive.CreateEntry(item.Name, CompressionLevel.Optimal);
                entry.LastWriteTime = modifiedAt;
                using var stream = entry.Open();
                stream.Write(item.Content);
            }
        }

        return buffer.ToArray();
    }

    /// <summary>El ZIP de <see cref="Create(string, byte[], DateTimeOffset)"/> en Base64, listo para <c>contentFile</c>.</summary>
    public static string CreateBase64(string entryName, byte[] content, DateTimeOffset modifiedAt) =>
        Convert.ToBase64String(Create(entryName, content, modifiedAt));

    /// <summary>El ZIP de <see cref="Create(string, string, DateTimeOffset)"/> en Base64, listo para <c>contentFile</c>.</summary>
    public static string CreateBase64(string entryName, string xml, DateTimeOffset modifiedAt) =>
        Convert.ToBase64String(Create(entryName, xml, modifiedAt));
}

/// <summary>An XML inside a batch ZIP: its name (see <see cref="DianFileNames.XmlEntry"/>) and its signed bytes.</summary>
public sealed record DianZipEntry(string Name, byte[] Content);
