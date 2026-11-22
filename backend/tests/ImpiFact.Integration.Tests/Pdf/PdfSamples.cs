using ImpiFact.Application.Abstractions.Pdf;

namespace ImpiFact.Integration.Tests.Pdf;

/// <summary>Modelos imprimibles de ejemplo, con los valores que arma el constructor del modelo para un caso golden.</summary>
internal static class PdfSamples
{
    public const string Cufe = "0b7a6bf1ac6fcd7463acd47da12e65c7756dab27f9456b302238d082aeed99e47a82028975423120683190bcd89d3ded";

    public static DocumentPdfModel Invoice(string description = "Desarrollo de software", string? notes = null) => new(
        PdfDocumentKind.Invoice,
        "SETP",
        "990000005",
        "2026-11-14",
        "10:20:00",
        "2026-11-14",
        "10:21:03",
        "CUFE",
        Cufe,
        "https://catalogo-vpfe-hab.dian.gov.co/document/searchqr?documentkey=" + Cufe,
        new PdfIssuer("EMPRESA EMISORA SAS", "900123456-7", "Responsable de IVA", "No aplica – Otros", "CALLE 1 # 2-03",
            "Bogotá, D.c. - Bogotá - Colombia", "6015550000", "facturacion@emisora.example"),
        new PdfResolution("18760000001", "2019-01-19", "SETP", "990000000", "995000000", "2019-01-19", "2030-01-19"),
        new PdfCustomer("800000001-1", "CLIENTE DE PRUEBA GOLDEN SAS", "Responsable de IVA", "No aplica – Otros", "CARRERA 2 # 2-02",
            "Bogotá, D.c. - Colombia", "3000000001", "cliente.golden@example.com"),
        [new PdfPayment("Contado", "Efectivo", "0", "2026-11-14")],
        null,
        null,
        [
            new PdfLine(1, "DEV01", description, "Primera entrega", "1.00", "Unidad", "8,000,000.00", "1,520,000.00", "0.00", "0.00", "8,000,000.00"),
            new PdfLine(2, "SOP01", "Soporte mensual", null, "1.00", "Unidad", "1,200,000.00", "228,000.00", "0.00", "0.00", "1,200,000.00"),
        ],
        [new PdfTax("IVA", "9,200,000.00", "19.00%", "1,748,000.00")],
        [
            new PdfTax("ReteRenta", "9,200,000.00", "4.00%", "368,000.00"),
            new PdfTax("ReteIVA", "1,748,000.00", "15.00%", "262,200.00"),
            new PdfTax("ReteICA", "9,200,000.00", "0.966%", "88,872.00"),
        ],
        [],
        new PdfTotals("2", "9,200,000.00", "1,748,000.00", "719,072.00", "0.00", "10,228,928.00"),
        "DIEZ MILLONES DOSCIENTOS VEINTIOCHO MIL NOVECIENTOS VEINTIOCHO PESOS CON CERO CENTAVOS",
        notes,
        "Gracias por su compra",
        "Pie de página de la empresa",
        "Software propio - ImpiFact");

    public static DocumentPdfModel CreditNote() => Invoice() with
    {
        Kind = PdfDocumentKind.CreditNote,
        Prefix = "NC",
        Number = "1",
        UuidLabel = "CUDE",
        Resolution = null,
        Reference = new PdfReference("SETP990000001", "2026-11-14", "d504983c0da24a41b26b37dbd04901ce7dfa6f8f"),
        Charges = [new PdfCharge("Flete", "35,000.00")],
        Totals = new PdfTotals("2", "9,200,000.00", "1,748,000.00", "0.00", "0.00", "10,948,000.00"),
    };

    public static DocumentPdfModel DebitNote() => CreditNote() with { Kind = PdfDocumentKind.DebitNote, Prefix = "ND" };

    public const string Cuds = "e3b6f1cf90150b66de1e1c55970c9797970c6239719a278bfb58a3a27ec1ba4dc35d586590a3958d0aa36e661a3d4d11";

    /// <summary>The support-document golden <c>resident-seller</c>: the company buys from a seller not obliged to invoice.</summary>
    public static DocumentPdfModel SupportDocument() => Invoice() with
    {
        Kind = PdfDocumentKind.SupportDocument,
        Prefix = "SEDS",
        Number = "984000001",
        UuidLabel = "CUDS",
        Uuid = Cuds,
        QrText = "https://catalogo-vpfe-hab.dian.gov.co/document/searchqr?documentkey=" + Cuds,
        Resolution = new PdfResolution("18760000001", "2022-01-01", "SEDS", "984000000", "985000000", "2022-01-01", "2030-01-19"),
        Customer = new PdfCustomer("800000003-4", "PROVEEDOR NO OBLIGADO GOLDEN", "Responsable de IVA", "No aplica – Otros", "CALLE 3 # 3-03",
            "Armenia - Colombia", "3000000003", "proveedor.golden@example.com"),
        Lines = [new PdfLine(1, "PRUEBA", "ARTICULO DE PRUEBAS", null, "2.00", "Unidad", "100.00", "0.00", "0.00", "0.00", "200.00")],
        Taxes = [new PdfTax("IVA", "200.00", "0.00%", "0.00")],
        Withholdings = [],
        Totals = new PdfTotals("1", "200.00", "0.00", "0.00", "0.00", "200.00"),
        AmountInWords = "DOSCIENTOS PESOS CON CERO CENTAVOS",
    };

    /// <summary>The adjustment-note golden <c>adjustment-with-reference</c>, which adjusts <see cref="SupportDocument"/>.</summary>
    public static DocumentPdfModel SupportDocumentAdjustmentNote() => SupportDocument() with
    {
        Kind = PdfDocumentKind.SupportDocumentAdjustmentNote,
        Prefix = "NDS",
        Number = "1",
        Resolution = null,
        Payments = [],
        Reference = new PdfReference("SEDS984000001", "2026-11-14", Cuds),
    };
}
