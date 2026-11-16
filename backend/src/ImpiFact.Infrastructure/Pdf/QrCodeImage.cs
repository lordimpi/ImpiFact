using QRCoder;

namespace ImpiFact.Infrastructure.Pdf;

/// <summary>
/// Imagen PNG del código QR del documento, dibujada con QRCoder (licencia MIT) con corrección de errores nivel Q, como
/// el PHP. El texto es el mismo del <c>sts:QRCode</c> del XML firmado.
/// </summary>
public static class QrCodeImage
{
    private const int PixelsPerModule = 6;

    public static byte[] Png(string text)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.Q);
        return new PngByteQRCode(data).GetGraphic(PixelsPerModule);
    }
}
