using System.Text.Json.Serialization;
using ImpiFact.Application.Abstractions.Handlers;

namespace ImpiFact.Application.Features.Companies.UpdateCompany;

/// <summary>
/// Una empresa actualiza sus propios datos (alcance <c>config:write</c>). El NIT de la ruta debe ser el de la empresa
/// de la credencial; si no, 403 sin revelar si ese NIT existe. Los ambientes no cambian aquí: tienen su propio caso
/// de uso (<c>SetEnvironment</c>), como en el PHP.
/// </summary>
public sealed record UpdateCompanyCommand : CompanyData, ICommand
{
    [JsonConstructor]
    public UpdateCompanyCommand()
    {
    }

    /// <summary>Copia los datos de otra solicitud con la misma forma, por ejemplo la del alta heredada.</summary>
    public UpdateCompanyCommand(CompanyData data)
        : base(data)
    {
    }
}
