using System.Text.Json.Serialization;
using FluentValidation;
using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Documents;
using ImpiFact.Application.Abstractions.Handlers;
using ImpiFact.Application.Abstractions.Results;
using ImpiFact.Domain.Masters;

namespace ImpiFact.Application.Features.Masters.Customers;

/// <summary>
/// Datos de un cliente, con los nombres del cliente (<c>customer</c>) del contrato de emisión. El alta y la edición
/// mandan todos los campos: lo que no llega queda vacío.
/// </summary>
public abstract record CustomerFields
{
    /// <summary>Tipo de identificación (catálogo <c>type_document_identifications</c>; 6 es NIT, 3 cédula de ciudadanía).</summary>
    [JsonPropertyName("type_document_identification_id")]
    public int? TypeDocumentIdentificationId { get; init; }

    /// <summary>Número de identificación sin dígito de verificación: letras y números, hasta 15.</summary>
    [JsonPropertyName("identification_number")]
    public string? IdentificationNumber { get; init; }

    /// <summary>Dígito de verificación; obligatorio con NIT y, si llega, debe ser el que calcula la DIAN.</summary>
    [JsonPropertyName("dv")]
    public string? Dv { get; init; }

    /// <summary>Nombre o razón social.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("phone")]
    public string? Phone { get; init; }

    [JsonPropertyName("address")]
    public string? Address { get; init; }

    /// <summary>Correo donde el cliente recibe sus facturas.</summary>
    [JsonPropertyName("email")]
    public string? Email { get; init; }

    [JsonPropertyName("municipality_id")]
    public int? MunicipalityId { get; init; }

    [JsonPropertyName("type_organization_id")]
    public int? TypeOrganizationId { get; init; }

    [JsonPropertyName("type_regime_id")]
    public int? TypeRegimeId { get; init; }

    [JsonPropertyName("type_liability_id")]
    public int? TypeLiabilityId { get; init; }

    /// <summary>Matrícula mercantil.</summary>
    [JsonPropertyName("merchant_registration")]
    public string? MerchantRegistration { get; init; }
}

/// <summary><c>POST /api/v1/customers</c>: crea un cliente de la empresa activa.</summary>
public sealed record CreateCustomerCommand : CustomerFields, ICommand;

/// <summary><c>PUT /api/v1/customers/{id}</c>: reemplaza los datos del cliente y, si llega, su estado.</summary>
public sealed record UpdateCustomerCommand : CustomerFields, ICommand
{
    [JsonIgnore]
    public Guid Id { get; init; }

    /// <summary><c>true</c> reactiva un cliente desactivado y <c>false</c> lo desactiva; nulo lo deja como está.</summary>
    [JsonPropertyName("is_active")]
    public bool? IsActive { get; init; }
}

/// <summary>
/// <c>POST /api/v1/customers/{id}/deactivate</c>: desactiva el cliente. Nunca se borra la fila, porque los documentos
/// emitidos pueden referirse a ella; se reactiva con la edición (<c>is_active: true</c>).
/// </summary>
public sealed record DeactivateCustomerCommand(Guid Id) : ICommand;

/// <summary>
/// Reglas de forma del cliente de la emisión (<c>InvoiceRequest</c> del PHP, php-rules 3.1) con los mismos mensajes. Los
/// largos máximos son los de las columnas de <c>customers</c> cuando son menores que los de la emisión (nombre y
/// dirección, 255). Lo que depende de la base (catálogos y unicidad) lo revisa el caso de uso.
/// </summary>
internal abstract class CustomerFieldsValidator<T> : AbstractValidator<T>
    where T : CustomerFields
{
    /// <summary>NIT en el catálogo de tipos de identificación: exige el dígito de verificación.</summary>
    public const int NitType = 6;

    protected CustomerFieldsValidator()
    {
        RuleFor(customer => customer.TypeDocumentIdentificationId)
            .NotNull().WithMessage(MasterMessages.Required("type_document_identification_id"))
            .OverridePropertyName("type_document_identification_id");
        RuleFor(customer => MasterText.Clean(customer.IdentificationNumber))
            .Cascade(CascadeMode.Stop)
            .NotNull().WithMessage(MasterMessages.Required("identification_number"))
            .Must(number => number!.All(char.IsAsciiLetterOrDigit)).WithMessage(MasterMessages.AlphaNum("identification_number"))
            .MaximumLength(15).WithMessage(MasterMessages.BetweenLength("identification_number", 1, 15))
            .OverridePropertyName("identification_number");
        RuleFor(customer => customer).Custom((customer, context) =>
        {
            var dv = MasterText.Clean(customer.Dv);
            var number = MasterText.Clean(customer.IdentificationNumber);
            if (dv is null)
            {
                if (customer.TypeDocumentIdentificationId == NitType)
                {
                    context.AddFailure("dv", MasterMessages.RequiredIf("dv", "type_document_identification_id", "6"));
                }
            }
            else if (dv.Length != 1 || !char.IsAsciiDigit(dv[0]))
            {
                context.AddFailure("dv", MasterMessages.Digits("dv", 1));
            }
            else if (number is { Length: <= 15 } && number.All(char.IsAsciiDigit) && !MasterCheckDigit.Matches(number, dv))
            {
                context.AddFailure("dv", MasterMessages.InvalidDv);
            }
        });
        RuleFor(customer => MasterText.Clean(customer.Name))
            .Cascade(CascadeMode.Stop)
            .NotNull().WithMessage(MasterMessages.Required("name"))
            .MaximumLength(255).WithMessage(MasterMessages.MaxLength("name", 255))
            .OverridePropertyName("name");
        Text(customer => customer.Phone, "phone", 20);
        Text(customer => customer.Address, "address", 255);
        Text(customer => customer.MerchantRegistration, "merchant_registration", 50);
        RuleFor(customer => MasterText.Clean(customer.Email))
            .Cascade(CascadeMode.Stop)
            .MaximumLength(200).WithMessage(MasterMessages.MaxLength("email", 200))
            .Must(email => email is null || MasterMessages.IsEmail(email)).WithMessage(MasterMessages.Email("email"))
            .OverridePropertyName("email");
    }

    private void Text(Func<T, string?> value, string field, int max) =>
        RuleFor(customer => MasterText.Clean(value(customer)))
            .MaximumLength(max).WithMessage(MasterMessages.MaxLength(field, max))
            .OverridePropertyName(field);
}

internal sealed class CreateCustomerCommandValidator : CustomerFieldsValidator<CreateCustomerCommand>;

internal sealed class UpdateCustomerCommandValidator : CustomerFieldsValidator<UpdateCustomerCommand>;

/// <summary>Lo que comparten el alta y la edición: catálogos, unicidad y copia de los campos.</summary>
internal static class CustomerWriter
{
    public static async Task<Error?> CheckAsync(
        CustomerFields fields, Guid companyId, Guid? exceptId, ICustomerStore customers, IDocumentCatalogs catalogs, CancellationToken ct)
    {
        var missing = await MasterCatalogs.MissingAsync(
            catalogs,
            [
                ("type_document_identification_id", DianCatalog.IdentificationType, fields.TypeDocumentIdentificationId),
                ("municipality_id", DianCatalog.Municipality, fields.MunicipalityId),
                ("type_organization_id", DianCatalog.OrganizationType, fields.TypeOrganizationId),
                ("type_regime_id", DianCatalog.Regime, fields.TypeRegimeId),
                ("type_liability_id", DianCatalog.Liability, fields.TypeLiabilityId),
            ],
            ct);
        if (missing.Count > 0)
        {
            return Error.Validation(missing);
        }

        var taken = await customers.IsIdentificationTakenAsync(
            companyId, fields.TypeDocumentIdentificationId!.Value, MasterText.Clean(fields.IdentificationNumber)!, exceptId, ct);
        return taken ? MasterErrors.CustomerDuplicate : null;
    }

    public static void Apply(Customer customer, CustomerFields fields)
    {
        customer.TypeDocumentIdentificationId = fields.TypeDocumentIdentificationId!.Value;
        customer.IdentificationNumber = MasterText.Clean(fields.IdentificationNumber)!;
        customer.Dv = MasterText.Clean(fields.Dv);
        customer.Name = MasterText.Clean(fields.Name)!;
        customer.Phone = MasterText.Clean(fields.Phone);
        customer.Address = MasterText.Clean(fields.Address);
        customer.Email = MasterText.Clean(fields.Email);
        customer.MunicipalityId = fields.MunicipalityId;
        customer.TypeOrganizationId = fields.TypeOrganizationId;
        customer.TypeRegimeId = fields.TypeRegimeId;
        customer.TypeLiabilityId = fields.TypeLiabilityId;
        customer.MerchantRegistration = MasterText.Clean(fields.MerchantRegistration);
    }
}

/// <summary>
/// Crea el cliente en la empresa activa. Un cliente con el mismo tipo y número de identificación (aunque esté
/// desactivado) responde 409; si dos solicitudes lo crean a la vez, el índice único deja pasar una y la otra también
/// responde 409.
/// </summary>
internal sealed class CreateCustomerHandler(ICustomerStore customers, IDocumentCatalogs catalogs, ITenantContext tenant)
    : IHandler<CreateCustomerCommand, CustomerResponse>
{
    public async Task<Result<CustomerResponse>> HandleAsync(CreateCustomerCommand request, CancellationToken ct)
    {
        if (tenant.CompanyId is not { } companyId)
        {
            return MasterErrors.CustomerNotFound;
        }

        if (await CustomerWriter.CheckAsync(request, companyId, exceptId: null, customers, catalogs, ct) is { } error)
        {
            return error;
        }

        var customer = new Customer
        {
            Id = Guid.CreateVersion7(),
            CompanyId = companyId,
            IdentificationNumber = "",
            Name = "",
        };
        CustomerWriter.Apply(customer, request);
        customers.Add(customer);
        return CustomerResponse.From(customer);
    }
}

/// <summary>Reemplaza los datos del cliente de la empresa activa; uno de otra empresa responde 404, igual que uno que no existe.</summary>
internal sealed class UpdateCustomerHandler(ICustomerStore customers, IDocumentCatalogs catalogs, ITenantContext tenant)
    : IHandler<UpdateCustomerCommand, CustomerResponse>
{
    public async Task<Result<CustomerResponse>> HandleAsync(UpdateCustomerCommand request, CancellationToken ct)
    {
        if (tenant.CompanyId is not { } companyId || await customers.FindAsync(companyId, request.Id, ct) is not { } customer)
        {
            return MasterErrors.CustomerNotFound;
        }

        if (await CustomerWriter.CheckAsync(request, companyId, customer.Id, customers, catalogs, ct) is { } error)
        {
            return error;
        }

        CustomerWriter.Apply(customer, request);
        customer.IsActive = request.IsActive ?? customer.IsActive;
        return CustomerResponse.From(customer);
    }
}

/// <summary>Desactiva el cliente; repetirlo no cambia nada.</summary>
internal sealed class DeactivateCustomerHandler(ICustomerStore customers, ITenantContext tenant)
    : IHandler<DeactivateCustomerCommand, CustomerResponse>
{
    public async Task<Result<CustomerResponse>> HandleAsync(DeactivateCustomerCommand request, CancellationToken ct)
    {
        if (tenant.CompanyId is not { } companyId || await customers.FindAsync(companyId, request.Id, ct) is not { } customer)
        {
            return MasterErrors.CustomerNotFound;
        }

        customer.IsActive = false;
        return CustomerResponse.From(customer);
    }
}
