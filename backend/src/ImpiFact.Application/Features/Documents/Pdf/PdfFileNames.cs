using System.Globalization;

namespace ImpiFact.Application.Features.Documents.Pdf;

/// <summary>
/// Nombre con que se entrega el PDF (adjunto del correo, descarga), como <c>createPDF</c> del PHP:
/// <c>FES-{prefijo}{número}.pdf</c> en la factura, <c>NCS-…</c> en la nota crédito y <c>NDS-…</c> en la nota débito.
/// El archivo guardado no usa este nombre: su clave sale del id de su fila.
/// </summary>
public static class PdfFileNames
{
    public static string For(int documentTypeId, string prefix, long number)
    {
        var tag = documentTypeId switch
        {
            4 => "NCS",
            5 => "NDS",
            _ => "FES",
        };
        return $"{tag}-{prefix}{number.ToString(CultureInfo.InvariantCulture)}.pdf";
    }
}
