using System.Text.Json.Nodes;
using ImpiFact.Dian.Soap;

namespace ImpiFact.Dian.Tests.Soap;

// Pruebas de la conversión de una respuesta de la DIAN al objeto «ResponseDian» de ApiDian (la implementación de
// referencia en PHP): cada elemento es un objeto con sus hijos por nombre sin prefijo, los atributos van en
// «_attributes», un elemento con solo texto es ese texto y los hijos repetidos forman una lista.
public class DianLegacyJsonTests
{
    [Test]
    public void A_get_status_answer_has_the_shape_of_the_php_response_dian()
    {
        var json = DianLegacyJson.FromXml(DianResponseSamples.GetStatusAccepted)!;

        var result = json["Envelope"]!["Body"]!["GetStatusResponse"]!["GetStatusResult"]!;
        Assert.Multiple(() =>
        {
            Assert.That(json["Envelope"]!["Header"]!["Action"]!.ToJsonString(), Is.EqualTo(
                """{"_attributes":{"mustUnderstand":"1"},"_value":"http://wcf.dian.colombia/IWcfDianCustomerServices/GetStatusResponse"}"""));
            Assert.That(json["Envelope"]!["Header"]!["Security"]!["Timestamp"]!.ToJsonString(), Is.EqualTo(
                """{"_attributes":{"Id":"_0"},"Created":"2025-07-22T17:20:27.320Z","Expires":"2025-07-22T17:25:27.320Z"}"""));
            Assert.That(result["IsValid"]!.GetValue<string>(), Is.EqualTo("true"));
            Assert.That(result["StatusCode"]!.GetValue<string>(), Is.EqualTo("00"));
            Assert.That(result["XmlDocumentKey"]!.GetValue<string>(), Is.EqualTo(DianResponseSamples.DocumentKey));
            Assert.That(result["XmlBase64Bytes"]!.GetValue<string>(), Is.EqualTo(DianResponseSamples.ApplicationResponseBase64));
            Assert.That(result["XmlBytes"]!.ToJsonString(), Is.EqualTo("""{"_attributes":{"nil":"true"}}"""));
            Assert.That(result["ErrorMessage"]!.ToJsonString(), Is.EqualTo("{}"));
        });
    }

    [Test]
    public void Repeated_children_become_a_list_and_a_single_child_stays_a_value()
    {
        var json = DianLegacyJson.FromXml(DianResponseSamples.SendBillSyncAccepted)!;
        var accepted = DianLegacyJson.FromXml(DianResponseSamples.GetStatusZipTwoDocuments)!;

        var errors = json["Envelope"]!["Body"]!["SendBillSyncResponse"]!["SendBillSyncResult"]!["ErrorMessage"]!["string"]!;
        var documents = accepted["Envelope"]!["Body"]!["GetStatusZipResponse"]!["GetStatusZipResult"]!["DianResponse"]!;
        Assert.Multiple(() =>
        {
            Assert.That(errors.ToJsonString(), Is.EqualTo(new JsonArray(
                "Regla: FAK55, Notificación: Correo electrónico no informado",
                "Regla: FAJ43b, Notificación: Nombre informado No corresponde al registrado en el RUT con respecto al Nit suminstrado.")
                .ToJsonString()));
            Assert.That(documents.AsArray(), Has.Count.EqualTo(2));
            Assert.That(documents[1]!["StatusCode"]!.GetValue<string>(), Is.EqualTo("98"));
        });
    }

    [Test]
    public void A_soap_fault_keeps_its_code_and_reason()
    {
        var json = DianLegacyJson.FromXml(DianResponseSamples.SecurityFault)!;

        var fault = json["Envelope"]!["Body"]!["Fault"]!;
        Assert.Multiple(() =>
        {
            Assert.That(fault["Code"]!["Value"]!.GetValue<string>(), Is.EqualTo("s:Sender"));
            Assert.That(fault["Code"]!["Subcode"]!["Value"]!.GetValue<string>(), Is.EqualTo("a:InvalidSecurity"));
            Assert.That(fault["Reason"]!["Text"]!.ToJsonString(), Is.EqualTo(
                """{"_attributes":{"lang":"en-US"},"_value":"An error occurred when verifying security for the message."}"""));
        });
    }

    [TestCase("")]
    [TestCase("Service Unavailable")]
    [TestCase("<s:Envelope")]
    public void A_body_that_is_not_xml_has_no_object(string body)
    {
        Assert.That(DianLegacyJson.FromXml(body), Is.Null);
    }

    [Test]
    public void A_document_type_declaration_is_refused()
    {
        const string body = """<!DOCTYPE x [<!ENTITY e "boom">]><x>&e;</x>""";

        Assert.That(DianLegacyJson.FromXml(body), Is.Null);
    }
}
