namespace ImpiFact.Application.Abstractions.Pdf;

// Modelo imprimible de un documento electrónico: lo que se ve en el PDF, ya resuelto y con los importes ya escritos
// como texto (dos decimales, coma de miles y punto decimal, como el number_format del PHP). Las etiquetas fijas
// («Cliente:», «Total a Pagar»…) las pone la plantilla; aquí solo van los datos, sin HTML: la plantilla escapa todo.

/// <summary>Tipo de documento que se imprime; cada uno tiene su plantilla.</summary>
public enum PdfDocumentKind
{
    /// <summary>Factura electrónica de venta.</summary>
    Invoice,

    /// <summary>Nota crédito.</summary>
    CreditNote,

    /// <summary>Nota débito.</summary>
    DebitNote,
}

/// <summary>Datos de la representación gráfica de un documento.</summary>
/// <param name="Prefix">Prefijo de la numeración, por ejemplo <c>SETP</c>.</param>
/// <param name="Number">Consecutivo sin prefijo.</param>
/// <param name="IssueDate">Fecha de emisión en hora Colombia (<c>yyyy-MM-dd</c>).</param>
/// <param name="IssueTime">Hora de emisión en hora Colombia (<c>HH:mm:ss</c>).</param>
/// <param name="ValidationDate">Fecha en que la DIAN aceptó el documento; la de emisión si no se guardó.</param>
/// <param name="ValidationTime">Hora en que la DIAN aceptó el documento; la de emisión si no se guardó.</param>
/// <param name="UuidLabel"><c>CUFE</c> en la factura, <c>CUDE</c> en las notas.</param>
/// <param name="Uuid">CUFE o CUDE: código único de la factura o del documento electrónico.</param>
/// <param name="QrText">Texto del código QR, el mismo del <c>sts:QRCode</c> del XML firmado.</param>
/// <param name="Resolution">Texto de la resolución de numeración; solo en la factura.</param>
/// <param name="Reference">Factura a la que se refiere una nota; nulo si no tiene.</param>
/// <param name="Period">Periodo facturado de una nota sin referencia, por ejemplo <c>2026-01-01 - 2026-01-31</c>.</param>
/// <param name="AmountInWords">Total a pagar en letras, como el «SON:» del PHP.</param>
/// <param name="SoftwareMode">Modo de operación y software que generó el documento.</param>
public sealed record DocumentPdfModel(
    PdfDocumentKind Kind,
    string Prefix,
    string Number,
    string IssueDate,
    string IssueTime,
    string ValidationDate,
    string ValidationTime,
    string UuidLabel,
    string Uuid,
    string QrText,
    PdfIssuer Issuer,
    PdfResolution? Resolution,
    PdfCustomer Customer,
    IReadOnlyList<PdfPayment> Payments,
    PdfReference? Reference,
    string? Period,
    IReadOnlyList<PdfLine> Lines,
    IReadOnlyList<PdfTax> Taxes,
    IReadOnlyList<PdfTax> Withholdings,
    IReadOnlyList<PdfCharge> Charges,
    PdfTotals Totals,
    string AmountInWords,
    string? Notes,
    string? HeadNote,
    string? FootNote,
    string SoftwareMode);

/// <summary>Empresa emisora.</summary>
/// <param name="Nit">NIT (Número de Identificación Tributaria) con su dígito de verificación, por ejemplo <c>900123456-7</c>.</param>
/// <param name="Regime">Régimen fiscal (<c>type_regimes</c>).</param>
/// <param name="Liability">Responsabilidades fiscales (<c>type_liabilities</c>), separadas por coma.</param>
/// <param name="Location">Municipio, departamento y país.</param>
public sealed record PdfIssuer(
    string Name, string Nit, string Regime, string Liability, string Address, string Location, string Phone, string Email);

/// <summary>Resolución de numeración de la DIAN que autoriza el consecutivo de la factura.</summary>
public sealed record PdfResolution(string Number, string Date, string Prefix, string From, string To, string ValidFrom, string ValidTo);

/// <summary>Adquiriente tal como quedó copiado en el documento.</summary>
/// <param name="Identification">Número de identificación con su dígito de verificación si lo tiene.</param>
/// <param name="City">Municipio y país.</param>
public sealed record PdfCustomer(
    string Identification, string Name, string Regime, string Liability, string Address, string City, string Phone, string Email);

/// <summary>Forma y medio de pago.</summary>
/// <param name="Form">Forma de pago (<c>payment_forms</c>: contado o crédito).</param>
/// <param name="Method">Medio de pago (<c>payment_methods</c>).</param>
/// <param name="DurationDays">Plazo en días.</param>
/// <param name="DueDate">Fecha de vencimiento.</param>
public sealed record PdfPayment(string Form, string Method, string DurationDays, string DueDate);

/// <summary>Factura a la que se refiere una nota.</summary>
/// <param name="Uuid">CUFE de la factura.</param>
public sealed record PdfReference(string Number, string IssueDate, string Uuid);

/// <summary>Línea del documento, con las columnas de la plantilla del PHP.</summary>
/// <param name="UnitPrice">Valor unitario antes del descuento.</param>
/// <param name="UnitTax">Impuesto por unidad (columna IVA/IC); <c>E</c> si la línea no trae impuestos (excluida).</param>
/// <param name="UnitDiscount">Descuento por unidad.</param>
/// <param name="DiscountPercent">Porcentaje del descuento sobre su base.</param>
/// <param name="Total">Valor de la línea (base gravable, sin impuestos).</param>
public sealed record PdfLine(
    int Position,
    string Code,
    string Description,
    string? Notes,
    string Quantity,
    string UnitMeasure,
    string UnitPrice,
    string UnitTax,
    string UnitDiscount,
    string DiscountPercent,
    string Total);

/// <summary>Impuesto o retención de la cabecera.</summary>
/// <param name="Rate">Porcentaje (<c>19.00%</c>), valor por unidad (<c>60.00 x 1.00</c>) o <c>-</c>.</param>
public sealed record PdfTax(string Name, string TaxableAmount, string Rate, string Amount);

/// <summary>Cargo de la cabecera, con su motivo o «Cargo Global Nro: n».</summary>
public sealed record PdfCharge(string Reason, string Amount);

/// <summary>Totales del documento.</summary>
/// <param name="LineCount">Cantidad de líneas.</param>
/// <param name="LineExtension">Base: suma de las líneas sin impuestos.</param>
/// <param name="Taxes">Suma de los impuestos.</param>
/// <param name="Withholdings">Suma de las retenciones.</param>
/// <param name="Discounts">Descuentos de la cabecera.</param>
/// <param name="Payable">
/// Total a pagar: en la factura, el valor a pagar menos las retenciones (como el PHP); en las notas, el total de la nota.
/// </param>
public sealed record PdfTotals(string LineCount, string LineExtension, string Taxes, string Withholdings, string Discounts, string Payable);
