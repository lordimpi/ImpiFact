using System.Text.RegularExpressions;
using ImpiFact.Application.Abstractions.Dian;
using ImpiFact.Dian.Soap;
using ImpiFact.Domain.Documents;

namespace ImpiFact.Application.Features.Documents.Delivery;

/// <summary>
/// Lectura de una llamada al servicio web de la DIAN (la autoridad tributaria de Colombia) en términos del documento
/// (Arquitectura detallada, sección 7, pasos 8 y 10; supuesto C4 de la fase 4). Es pura: no guarda ni llama a nada.
/// </summary>
/// <remarks>
/// Reglas, en orden: código <c>98</c> («en proceso») sigue en proceso; <c>IsValid</c> verdadero es aceptado, con
/// advertencias si trae notificaciones; <c>IsValid</c> falso con la regla 90 («documento procesado anteriormente») es
/// un documento ya procesado que hay que reconciliar con GetStatus; <c>IsValid</c> falso con reglas de rechazo o con
/// código <c>99</c> es rechazado. Cualquier otro resultado (por ejemplo, GetStatus de un documento que la DIAN todavía
/// no registra) no es una decisión y se sigue consultando. Si algún intento de la llamada pudo llegar a la DIAN
/// (<see cref="DianCall.PossiblyReceived"/>), una respuesta que diría «no se entregó» o «rechazó el mensaje» (un 503,
/// un SOAP Fault) se lee como «sin respuesta»: la DIAN pudo haber recibido el documento y hay que consultar su estado.
/// </remarks>
internal abstract partial record DianAnswer
{
    private const string InProcessCode = "98";
    private const string RejectedCode = "99";

    private DianAnswer()
    {
    }

    /// <summary>Lee una llamada completa: primero si hubo respuesta, después qué respondió la DIAN.</summary>
    public static DianAnswer From(DianCall call)
    {
        ArgumentNullException.ThrowIfNull(call);
        return call.Outcome switch
        {
            DianCallOutcome.NotSent => new NotDelivered(call.Failure ?? "The request was not sent."),
            DianCallOutcome.NoAnswer => new NoAnswer(call.Failure ?? "The request got no answer."),
            _ => FromResponse(call.Response ?? throw new ArgumentException("An answered call has a response.", nameof(call))) switch
            {
                NotDelivered or Refused when call.PossiblyReceived => new NoAnswer(
                    "An earlier attempt may have reached the DIAN without an answer; the last answer does not settle it."),
                var answer => answer,
            },
        };
    }

    /// <summary>Lee un resultado de SendBillSync, GetStatus o GetStatusZip.</summary>
    public static DianAnswer Classify(DianResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.StatusCode == InProcessCode)
        {
            return new InProcess(result);
        }

        if (result.IsValid == true)
        {
            return new Decided(result.ErrorMessages.Count > 0 ? DocumentStatus.AcceptedWithWarnings : DocumentStatus.Accepted, result);
        }

        if (result.IsValid == false && IsRule90(result))
        {
            return new AlreadyProcessed(result);
        }

        return result.IsValid == false && (result.ErrorMessages.Count > 0 || result.StatusCode == RejectedCode)
            ? new Decided(DocumentStatus.Rejected, result)
            : new InProcess(result);
    }

    private static DianAnswer FromResponse(DianSoapResponse response) => response switch
    {
        DianSoapFaultResponse fault => IsServiceFault(fault.Code) ? new NotDelivered(fault.Reason) : new Refused(fault.Reason),
        DianServiceUnavailableResponse => new NotDelivered("The DIAN service is unavailable."),
        DianResultResponse { Results.Count: 0 } => new InProcess(null),
        DianResultResponse { OperationName: "SendTestSetAsync", Results: [var testSet, ..] } =>
            string.IsNullOrWhiteSpace(testSet.ZipKey) ? new Decided(DocumentStatus.Rejected, testSet) : new ZipReceived(testSet.ZipKey),
        DianResultResponse { Results: [var first, ..] } => Classify(first),
        _ => new NoAnswer("The DIAN answer was not recognized."),
    };

    // SOAP 1.2 usa «Receiver» y SOAP 1.1 «Server» para las fallas del servicio; «Sender» o «Client» son del que envía.
    private static bool IsServiceFault(string code) =>
        code.EndsWith("Receiver", StringComparison.OrdinalIgnoreCase) || code.EndsWith("Server", StringComparison.OrdinalIgnoreCase);

    private static bool IsRule90(DianResult result) =>
        result.ErrorMessages.Append(result.StatusDescription).Append(result.StatusMessage)
            .Any(text => text is not null && Rule90Pattern().IsMatch(text));

    [GeneratedRegex(@"Regla\s*:\s*90\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Rule90Pattern();

    /// <summary>
    /// No se entregó: no se alcanzó a enviar, el servicio no estaba disponible o falló del lado de la DIAN. Es seguro
    /// reintentar; el documento sigue en <see cref="DocumentStatus.Queued"/>.
    /// </summary>
    public sealed record NotDelivered(string Reason) : DianAnswer;

    /// <summary>Se envió y no hubo una respuesta útil: la DIAN pudo haber recibido el documento.</summary>
    public sealed record NoAnswer(string Reason) : DianAnswer;

    /// <summary>
    /// La DIAN rechazó el mensaje (SOAP Fault del que envía, por ejemplo por la firma o el certificado) sin evaluar el
    /// documento. Reintentar no lo arregla.
    /// </summary>
    public sealed record Refused(string Reason) : DianAnswer;

    /// <summary>SendTestSetAsync recibió el ZIP; el resultado se consulta después con GetStatusZip y esta ZipKey.</summary>
    public sealed record ZipReceived(string ZipKey) : DianAnswer;

    /// <summary>La DIAN decidió: <see cref="Status"/> es Accepted, AcceptedWithWarnings o Rejected.</summary>
    public sealed record Decided(DocumentStatus Status, DianResult Result) : DianAnswer;

    /// <summary>La DIAN todavía no tiene una decisión (código 98 o un resultado que no decide).</summary>
    public sealed record InProcess(DianResult? Result) : DianAnswer;

    /// <summary>Regla 90: la DIAN ya procesó un documento con ese CUFE; su estado se consulta con GetStatus.</summary>
    public sealed record AlreadyProcessed(DianResult Result) : DianAnswer;
}
