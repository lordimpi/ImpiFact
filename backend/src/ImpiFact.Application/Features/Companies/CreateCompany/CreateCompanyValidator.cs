using FluentValidation;
using ImpiFact.Application.Abstractions.Companies;

namespace ImpiFact.Application.Features.Companies.CreateCompany;

/// <summary>Reglas del alta: las de los datos de la empresa más los ambientes iniciales (1 o 2).</summary>
public sealed class CreateCompanyValidator : CompanyDataValidator<CreateCompanyCommand>
{
    public CreateCompanyValidator(ICatalogLookup catalogs)
        : base(catalogs)
    {
        Environment(command => command.TypeEnvironmentId, "type_environment_id");
        Environment(command => command.PayrollTypeEnvironmentId, "payroll_type_environment_id");
        Environment(command => command.EqdocsTypeEnvironmentId, "eqdocs_type_environment_id");
        Environment(command => command.SupportDocumentTypeEnvironmentId, "support_document_type_environment_id");
        Environment(command => command.EventTypeEnvironmentId, "event_type_environment_id");
    }

    private void Environment(Func<CreateCompanyCommand, int?> field, string name) =>
        RuleFor(command => field(command))
            .Must(value => value is null || Environments.IsValid(value.Value))
            .WithMessage(PhpMessages.Exists(name))
            .OverridePropertyName(name);
}
