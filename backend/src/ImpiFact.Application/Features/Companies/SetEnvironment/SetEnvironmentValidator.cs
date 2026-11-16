using FluentValidation;

namespace ImpiFact.Application.Features.Companies.SetEnvironment;

/// <summary>Cada ambiente debe ser 1 o 2, con los mensajes de <c>ConfigurationEnvironmentRequest.php</c>.</summary>
public sealed class SetEnvironmentValidator : AbstractValidator<SetEnvironmentCommand>
{
    private const string Invoice = "El ambiente de factura de ventas seleccionado no es válido.";
    private const string Payroll = "El ambiente de nómina seleccionado no es válido.";
    private const string Pos = "El ambiente de POS seleccionado no es válido.";
    private const string Support = "El ambiente de documentos soporte seleccionado no es válido.";
    private const string Events = "El ambiente de eventos RADIAN seleccionado no es válido.";

    public SetEnvironmentValidator()
    {
        Environment(command => command.TypeEnvironmentId, "type_environment_id", Invoice);
        Environment(command => command.FacturaVentas, "factura_ventas", Invoice);
        Environment(command => command.PayrollTypeEnvironmentId, "payroll_type_environment_id", Payroll);
        Environment(command => command.Nomina, "nomina", Payroll);
        Environment(command => command.EqdocsTypeEnvironmentId, "eqdocs_type_environment_id", Pos);
        Environment(command => command.Pos, "pos", Pos);
        Environment(command => command.SupportDocumentTypeEnvironmentId, "support_document_type_environment_id", Support);
        Environment(command => command.DocumentosSoporte, "documentos_soporte", Support);
        Environment(command => command.EventTypeEnvironmentId, "event_type_environment_id", Events);
        Environment(command => command.EventosRadian, "eventos_radian", Events);
    }

    private void Environment(Func<SetEnvironmentCommand, int?> field, string name, string message) =>
        RuleFor(command => field(command))
            .Must(value => value is null || Environments.IsValid(value.Value))
            .WithMessage(message)
            .OverridePropertyName(name);
}
