using System.Text.RegularExpressions;
using HandlebarsDotNet;
using ImpiFact.Application.Abstractions.Pdf;

namespace ImpiFact.Infrastructure.Pdf;

/// <summary>
/// Plantillas HTML de la representación gráfica (el PDF), escritas con Handlebars (un lenguaje de plantillas con
/// marcas <c>{{campo}}</c>; librería Handlebars.Net, licencia MIT) y guardadas dentro del ensamblado como recursos
/// <c>Pdf/Templates/*</c>. Hay una plantilla por tipo de documento (<c>invoice</c>, <c>credit-note</c>,
/// <c>debit-note</c>) y partes compartidas cuyo nombre empieza con guion bajo. Se compilan una vez al crear la clase.
/// </summary>
/// <remarks>
/// Los nombres, descripciones y notas son texto del usuario: las plantillas solo usan <c>{{ }}</c>, que escapa el HTML,
/// nunca <c>{{{ }}}</c>. La hoja de estilos (<see cref="Stylesheet"/>) y la imagen del QR viajan aparte, como archivos
/// junto al HTML (<c>styles.css</c> y <c>qr.png</c>).
/// </remarks>
public sealed partial class DocumentHtmlTemplates
{
    /// <summary>Nombre con que el HTML pide la imagen del código QR.</summary>
    public const string QrImageFile = "qr.png";

    /// <summary>Nombre con que el HTML pide la hoja de estilos.</summary>
    public const string StylesheetFile = "styles.css";

    private const string ResourcePrefix = "Pdf/Templates/";

    private const string InvoiceLegalText =
        "La presente Factura Electrónica de Venta, es un título valor de acuerdo con lo establecido en el Código de Comercio y en " +
        "especial en los artículos 621, 772 y 774. El Decreto 2242 del 24 de noviembre de 2015 y el Decreto Único 1074 de mayo de 2015. " +
        "El presente título valor se asimila en todos sus efectos a una letra de cambio Art. 779 del Código de Comercio. Con esta el " +
        "Comprador declara haber recibido real y materialmente las mercancías o prestación de servicios descritos en este título valor.";

    private static readonly IReadOnlyDictionary<PdfDocumentKind, PdfLabels> Labels = new Dictionary<PdfDocumentKind, PdfLabels>
    {
        [PdfDocumentKind.Invoice] = new(
            "FACTURA ELECTRONICA DE VENTA", "REPRESENTACION GRAFICA DE FACTURA ELECTRONICA", "Factura Electronica de Venta",
            ["Total Factura - Descuentos:", "Total a Pagar"], ShowDiscountPercent: true, ShowCharges: false, InvoiceLegalText),
        [PdfDocumentKind.CreditNote] = new(
            "NOTA CREDITO DE LA FACTURA ELECTRONICA DE VENTA No", "REPRESENTACION GRAFICA DE NOTA CREDITO ELECTRONICA", "Nota Crédito",
            ["Total Nota:"], ShowDiscountPercent: false, ShowCharges: true, LegalText: null),
        [PdfDocumentKind.DebitNote] = new(
            "NOTA DEBITO DE LA FACTURA", "REPRESENTACION GRAFICA DE NOTA DEBITO ELECTRONICA", "Nota Debito",
            ["Total Nota:"], ShowDiscountPercent: false, ShowCharges: true, LegalText: null),
    };

    private readonly IReadOnlyDictionary<PdfDocumentKind, HandlebarsTemplate<object, object>> compiled;

    public DocumentHtmlTemplates()
    {
        var handlebars = Handlebars.Create();
        var assembly = typeof(DocumentHtmlTemplates).Assembly;
        foreach (var name in assembly.GetManifestResourceNames().Where(name => name.StartsWith(ResourcePrefix + "_", StringComparison.Ordinal)))
        {
            handlebars.RegisterTemplate(Path.GetFileNameWithoutExtension(name[ResourcePrefix.Length..]), Read(name));
        }

        compiled = new Dictionary<PdfDocumentKind, HandlebarsTemplate<object, object>>
        {
            [PdfDocumentKind.Invoice] = handlebars.Compile(Read(ResourcePrefix + "invoice.hbs")),
            [PdfDocumentKind.CreditNote] = handlebars.Compile(Read(ResourcePrefix + "credit-note.hbs")),
            [PdfDocumentKind.DebitNote] = handlebars.Compile(Read(ResourcePrefix + "debit-note.hbs")),
        };
        Stylesheet = System.Text.Encoding.UTF8.GetBytes(Read(ResourcePrefix + StylesheetFile));
    }

    /// <summary>Contenido de <c>styles.css</c>.</summary>
    public byte[] Stylesheet { get; }

    /// <summary>HTML completo del documento (<c>index.html</c>).</summary>
    public string Render(DocumentPdfModel model) =>
        compiled[model.Kind](new PdfView(model, Labels[model.Kind], QrImageFile));

    private static string Read(string resource)
    {
        using var stream = typeof(DocumentHtmlTemplates).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"The PDF template resource '{resource}' is missing.");
        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);
        var source = reader.ReadToEnd();

        // Handlebars.Net 2.4 escribe sin escapar el HTML de una parte compartida llamada sola en su línea con sangría
        // («    {{> _lines}}»): esos valores son texto del usuario, así que una plantilla así no se acepta.
        return IndentedPartial().IsMatch(source)
            ? throw new InvalidOperationException($"The PDF template '{resource}' calls a partial with indentation, which disables HTML escaping.")
            : source;
    }

    [GeneratedRegex(@"^[ \t]+\{\{~?>", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex IndentedPartial();

    /// <summary>Lo que ve la plantilla: el documento, los textos fijos de su tipo y el archivo de la imagen del QR.</summary>
    private sealed record PdfView(DocumentPdfModel Document, PdfLabels Labels, string QrImage);

    /// <summary>Textos fijos que cambian según el tipo de documento, en español como en ApiDian.</summary>
    private sealed record PdfLabels(
        string Title,
        string Representation,
        string FooterTitle,
        IReadOnlyList<string> TotalLabels,
        bool ShowDiscountPercent,
        bool ShowCharges,
        string? LegalText);
}
