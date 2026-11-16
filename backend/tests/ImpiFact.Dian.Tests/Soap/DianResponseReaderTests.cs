using System.Text;
using ImpiFact.Dian.Soap;

namespace ImpiFact.Dian.Tests.Soap;

// Pruebas del lector de respuestas SOAP de la DIAN: cada respuesta se convierte en un resultado tipado, y
// un error SOAP (Fault) o una página HTML son resultados distintos, no excepciones.
public class DianResponseReaderTests
{
    [Test]
    public void Accepted_send_bill_sync_is_read_with_its_notifications_and_application_response()
    {
        var result = SingleResult(DianResponseSamples.SendBillSyncAccepted, "SendBillSync");

        Assert.Multiple(() =>
        {
            Assert.That(result.IsValid, Is.True);
            Assert.That(result.StatusCode, Is.EqualTo("00"));
            Assert.That(result.StatusDescription, Is.EqualTo("Procesado Correctamente."));
            Assert.That(result.StatusMessage, Is.EqualTo("La Factura electrónica SETP994500239, ha sido autorizada."));
            Assert.That(result.ErrorMessages, Is.EqualTo(new[]
            {
                "Regla: FAK55, Notificación: Correo electrónico no informado",
                "Regla: FAJ43b, Notificación: Nombre informado No corresponde al registrado en el RUT con respecto al Nit suminstrado.",
            }));
            Assert.That(result.XmlDocumentKey, Is.EqualTo(DianResponseSamples.DocumentKey));
            Assert.That(result.XmlFileName, Is.EqualTo("fv09018462800002500000166"));
            Assert.That(result.XmlBase64Bytes, Is.EqualTo(Encoding.UTF8.GetBytes(DianResponseSamples.ApplicationResponseXml)));
            Assert.That(result.ZipKey, Is.Null);
        });
    }

    [Test]
    public void Rejected_send_bill_sync_is_read_with_its_error_list()
    {
        var result = SingleResult(DianResponseSamples.SendBillSyncRejected, "SendBillSync");

        Assert.Multiple(() =>
        {
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.StatusCode, Is.EqualTo("99"));
            Assert.That(result.StatusDescription, Is.EqualTo("Validación contiene errores en campos mandatorios."));
            Assert.That(result.StatusMessage, Is.Null);
            Assert.That(result.ErrorMessages, Is.EqualTo(new[]
            {
                "Regla: FAD06, Rechazo: Valor del CUFE no está calculado correctamente.",
                "Regla: FAB05b, Rechazo: Número de factura fuera del rango de numeración autorizado.",
            }));
        });
    }

    [Test]
    public void Send_test_set_async_returns_the_zip_key()
    {
        var result = SingleResult(DianResponseSamples.SendTestSetAsyncAccepted, "SendTestSetAsync");

        Assert.Multiple(() =>
        {
            Assert.That(result.ZipKey, Is.EqualTo("3f6e1d52-8a3b-4c1e-9b7a-2d4c5e6f7a8b"));
            Assert.That(result.ErrorMessages, Is.Empty);
            Assert.That(result.IsValid, Is.Null);
            Assert.That(result.StatusCode, Is.Null);
        });
    }

    [Test]
    public void Send_test_set_async_reads_the_processed_messages_of_its_error_list()
    {
        var result = SingleResult(DianResponseSamples.SendTestSetAsyncWithErrors, "SendTestSetAsync");

        Assert.Multiple(() =>
        {
            Assert.That(result.ZipKey, Is.Null);
            Assert.That(result.ErrorMessages, Is.EqualTo(new[] { "Set de pruebas inexistente o inactivo." }));
        });
    }

    [Test]
    public void Get_status_is_read_like_send_bill_sync()
    {
        var result = SingleResult(DianResponseSamples.GetStatusAccepted, "GetStatus");

        Assert.Multiple(() =>
        {
            Assert.That(result.IsValid, Is.True);
            Assert.That(result.StatusCode, Is.EqualTo("00"));
            Assert.That(result.ErrorMessages, Is.Empty);
            Assert.That(result.XmlDocumentKey, Is.EqualTo(DianResponseSamples.DocumentKey));
        });
    }

    [Test]
    public void Get_status_zip_returns_one_result_per_dian_response_item()
    {
        var response = Read<DianResultResponse>(DianResponseSamples.GetStatusZipTwoDocuments);

        Assert.Multiple(() =>
        {
            Assert.That(response.OperationName, Is.EqualTo("GetStatusZip"));
            Assert.That(response.Results, Has.Count.EqualTo(2));
            Assert.That(response.Results[0].IsValid, Is.True);
            Assert.That(response.Results[0].XmlFileName, Is.EqualTo("fv09018462800002500000166"));
            Assert.That(response.Results[1].IsValid, Is.False);
            Assert.That(response.Results[1].StatusCode, Is.EqualTo("98"));
            Assert.That(response.Results[1].StatusDescription, Is.EqualTo("En Proceso de Validación"));
            Assert.That(response.Results[1].XmlBase64Bytes, Is.Null);
            Assert.That(response.Results[1].XmlDocumentKey, Is.Null);
        });
    }

    [Test]
    public void Soap_fault_is_a_typed_outcome()
    {
        var fault = Read<DianSoapFaultResponse>(DianResponseSamples.SecurityFault);

        Assert.Multiple(() =>
        {
            Assert.That(fault.Code, Is.EqualTo("s:Sender"));
            Assert.That(fault.Subcode, Is.EqualTo("a:InvalidSecurity"));
            Assert.That(fault.Reason, Is.EqualTo("An error occurred when verifying security for the message."));
        });
    }

    [Test]
    public void Html_body_means_the_service_is_unavailable()
    {
        var response = Read<DianServiceUnavailableResponse>(DianResponseSamples.HtmlErrorPage);

        Assert.That(response.Body, Is.EqualTo(DianResponseSamples.HtmlErrorPage));
    }

    [TestCase("<html><body>Service Unavailable</body></html>")]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("Service Unavailable")]
    public void Well_formed_html_empty_or_plain_text_body_means_the_service_is_unavailable(string body)
    {
        Read<DianServiceUnavailableResponse>(body);
    }

    [TestCase("<root/>")]
    [TestCase("<s:Envelope xmlns:s=\"http://www.w3.org/2003/05/soap-envelope\"><s:Body/></s:Envelope>")]
    [TestCase(DianResponseSamples.NumberingRangeResponse)]
    public void Xml_without_a_known_result_is_unrecognized(string body)
    {
        var response = Read<DianUnrecognizedResponse>(body);

        Assert.That(response.Body, Is.EqualTo(body));
    }

    [Test]
    public void Invalid_base64_application_response_is_unrecognized()
    {
        var body = DianResponseSamples.GetStatusAccepted.Replace(DianResponseSamples.ApplicationResponseBase64, "not base64!", StringComparison.Ordinal);

        Read<DianUnrecognizedResponse>(body);
    }

    private static DianResult SingleResult(string body, string operation)
    {
        var response = Read<DianResultResponse>(body);
        Assert.That(response.OperationName, Is.EqualTo(operation));
        Assert.That(response.Results, Has.Count.EqualTo(1));
        return response.Results[0];
    }

    private static T Read<T>(string body)
        where T : DianSoapResponse
    {
        var response = DianResponseReader.Read(body);
        Assert.That(response, Is.InstanceOf<T>());
        return (T)response;
    }
}
