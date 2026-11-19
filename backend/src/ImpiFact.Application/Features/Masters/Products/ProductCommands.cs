using System.Text.Json.Serialization;
using FluentValidation;
using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Documents;
using ImpiFact.Application.Abstractions.Handlers;
using ImpiFact.Application.Abstractions.Results;
using ImpiFact.Domain.Masters;

namespace ImpiFact.Application.Features.Masters.Products;

/// <summary>Datos de un producto. El alta y la edición mandan todos los campos: lo que no llega queda vacío.</summary>
public abstract record ProductFields
{
    /// <summary>Código propio, único en la empresa.</summary>
    [JsonPropertyName("code")]
    public string? Code { get; init; }

    /// <summary>Nombre que va en la línea de la emisión.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>Unidad de medida (catálogo <c>unit_measures</c>).</summary>
    [JsonPropertyName("unit_measure_id")]
    public int? UnitMeasureId { get; init; }

    /// <summary>Estándar del código (catálogo <c>type_item_identifications</c>); 4 si no llega.</summary>
    [JsonPropertyName("type_item_identification_id")]
    public int? TypeItemIdentificationId { get; init; }

    /// <summary>Precio unitario sin impuestos, desde 0 y con hasta 6 decimales.</summary>
    [JsonPropertyName("unit_price")]
    public decimal? UnitPrice { get; init; }

    /// <summary>Impuesto (catálogo <c>taxes</c>); nulo si no lleva.</summary>
    [JsonPropertyName("tax_id")]
    public int? TaxId { get; init; }

    /// <summary>Tarifa en porcentaje, de 0 a 100; obligatoria con impuesto, salvo en los que no van en porcentaje (2 y 10).</summary>
    [JsonPropertyName("tax_percent")]
    public decimal? TaxPercent { get; init; }

    /// <summary>Valor por unidad; obligatorio con el impuesto 10 (INC de bolsas) y permitido con el 2 (IC).</summary>
    [JsonPropertyName("tax_per_unit_amount")]
    public decimal? TaxPerUnitAmount { get; init; }
}

/// <summary><c>POST /api/v1/products</c>: crea un producto de la empresa activa.</summary>
public sealed record CreateProductCommand : ProductFields, ICommand;

/// <summary><c>PUT /api/v1/products/{id}</c>: reemplaza los datos del producto y, si llega, su estado.</summary>
public sealed record UpdateProductCommand : ProductFields, ICommand
{
    [JsonIgnore]
    public Guid Id { get; init; }

    /// <summary><c>true</c> reactiva un producto desactivado y <c>false</c> lo desactiva; nulo lo deja como está.</summary>
    [JsonPropertyName("is_active")]
    public bool? IsActive { get; init; }
}

/// <summary><c>POST /api/v1/products/{id}/deactivate</c>: desactiva el producto, sin borrarlo.</summary>
public sealed record DeactivateProductCommand(Guid Id) : ICommand;

/// <summary>
/// Reglas del producto. Las de impuestos son las de <c>tax_totals</c> de la emisión (<c>required_unless:tax_id,10,2</c> y
/// <c>required_if:tax_id,10</c> del PHP); los largos y la precisión son los de la tabla <c>items</c>.
/// </summary>
internal abstract class ProductFieldsValidator<T> : AbstractValidator<T>
    where T : ProductFields
{
    /// <summary>Impuestos que no llevan porcentaje (IC y INC de bolsas).</summary>
    public static readonly int[] TaxesWithoutPercent = [2, 10];

    /// <summary>Impuesto que exige el valor por unidad (INC de bolsas).</summary>
    public const int PerUnitTax = 10;

    /// <summary>Mayor valor de una columna <c>numeric(18,6)</c>.</summary>
    private const decimal MaxQuantity = 999_999_999_999.999999m;

    protected ProductFieldsValidator()
    {
        Text(product => product.Code, "code", 100, required: true);
        Text(product => product.Name, "name", 500, required: true);
        Text(product => product.Description, "description", 1000, required: false);
        RuleFor(product => product.UnitMeasureId)
            .NotNull().WithMessage(MasterMessages.Required("unit_measure_id"))
            .OverridePropertyName("unit_measure_id");
        RuleFor(product => product.UnitPrice)
            .Cascade(CascadeMode.Stop)
            .NotNull().WithMessage(MasterMessages.Required("unit_price"))
            .Must(price => price is >= 0 and <= MaxQuantity).WithMessage(MasterMessages.Invalid("unit_price"))
            .Must(price => decimal.Round(price!.Value, 6) == price.Value).WithMessage(MasterMessages.Decimals("unit_price", 6))
            .OverridePropertyName("unit_price");
        RuleFor(product => product).Custom((product, context) =>
        {
            if (product.TaxId is not { } taxId)
            {
                if (product.TaxPercent is not null)
                {
                    context.AddFailure("tax_percent", MasterMessages.Prohibited("tax_percent", "tax_id"));
                }

                if (product.TaxPerUnitAmount is not null)
                {
                    context.AddFailure("tax_per_unit_amount", MasterMessages.Prohibited("tax_per_unit_amount", "tax_id"));
                }

                return;
            }

            if (product.TaxPercent is { } percent)
            {
                if (percent is < 0 or > 100)
                {
                    context.AddFailure("tax_percent", MasterMessages.Invalid("tax_percent"));
                }
                else if (decimal.Round(percent, 4) != percent)
                {
                    context.AddFailure("tax_percent", MasterMessages.Decimals("tax_percent", 4));
                }
            }
            else if (!TaxesWithoutPercent.Contains(taxId))
            {
                context.AddFailure("tax_percent", MasterMessages.RequiredUnless("tax_percent", "tax_id", "10, 2"));
            }

            if (product.TaxPerUnitAmount is { } perUnit)
            {
                if (perUnit is < 0 or > MaxQuantity)
                {
                    context.AddFailure("tax_per_unit_amount", MasterMessages.Invalid("tax_per_unit_amount"));
                }
                else if (decimal.Round(perUnit, 6) != perUnit)
                {
                    context.AddFailure("tax_per_unit_amount", MasterMessages.Decimals("tax_per_unit_amount", 6));
                }
            }
            else if (taxId == PerUnitTax)
            {
                context.AddFailure("tax_per_unit_amount", MasterMessages.RequiredIf("tax_per_unit_amount", "tax_id", "10"));
            }
        });
    }

    private void Text(Func<T, string?> value, string field, int max, bool required)
    {
        var rule = RuleFor(product => MasterText.Clean(value(product))).Cascade(CascadeMode.Stop);
        if (required)
        {
            rule.NotNull().WithMessage(MasterMessages.Required(field));
        }

        rule.MaximumLength(max).WithMessage(MasterMessages.MaxLength(field, max))
            .Must(text => MasterText.IsPlain(text, allowLineBreaks: true)).WithMessage(MasterMessages.ControlCharacters(field))
            .OverridePropertyName(field);
    }
}

internal sealed class CreateProductCommandValidator : ProductFieldsValidator<CreateProductCommand>;

internal sealed class UpdateProductCommandValidator : ProductFieldsValidator<UpdateProductCommand>;

/// <summary>Lo que comparten el alta y la edición: catálogos, unicidad del código y copia de los campos.</summary>
internal static class ProductWriter
{
    /// <summary>Estándar de adopción del contribuyente: el código es propio de la empresa (catálogo <c>type_item_identifications</c>).</summary>
    public const int OwnCodeStandard = 4;

    public static async Task<Error?> CheckAsync(
        ProductFields fields, Guid companyId, Guid? exceptId, IProductStore products, IDocumentCatalogs catalogs, CancellationToken ct)
    {
        var missing = await MasterCatalogs.MissingAsync(
            catalogs,
            [
                ("unit_measure_id", DianCatalog.UnitMeasure, fields.UnitMeasureId),
                ("type_item_identification_id", DianCatalog.ItemIdentificationType, fields.TypeItemIdentificationId ?? OwnCodeStandard),
                ("tax_id", DianCatalog.Tax, fields.TaxId),
            ],
            ct);
        if (missing.Count > 0)
        {
            return Error.Validation(missing);
        }

        return await products.IsCodeTakenAsync(companyId, MasterText.Clean(fields.Code)!, exceptId, ct) ? MasterErrors.ProductDuplicate : null;
    }

    public static void Apply(Item item, ProductFields fields)
    {
        item.Code = MasterText.Clean(fields.Code)!;
        item.Name = MasterText.Clean(fields.Name)!;
        item.Description = MasterText.Clean(fields.Description);
        item.UnitMeasureId = fields.UnitMeasureId!.Value;
        item.TypeItemIdentificationId = fields.TypeItemIdentificationId ?? OwnCodeStandard;
        item.SaleUnitPrice = fields.UnitPrice!.Value;
        item.TaxId = fields.TaxId;
        item.TaxPercent = fields.TaxPercent;
        item.TaxPerUnitAmount = fields.TaxPerUnitAmount;
    }
}

/// <summary>Crea el producto en la empresa activa; un código repetido (aunque el otro esté desactivado) responde 409.</summary>
internal sealed class CreateProductHandler(IProductStore products, IDocumentCatalogs catalogs, ITenantContext tenant)
    : IHandler<CreateProductCommand, ProductResponse>
{
    public async Task<Result<ProductResponse>> HandleAsync(CreateProductCommand request, CancellationToken ct)
    {
        if (tenant.CompanyId is not { } companyId)
        {
            return MasterErrors.ProductNotFound;
        }

        if (await ProductWriter.CheckAsync(request, companyId, exceptId: null, products, catalogs, ct) is { } error)
        {
            return error;
        }

        var item = new Item { Id = Guid.CreateVersion7(), CompanyId = companyId, Code = "", Name = "" };
        ProductWriter.Apply(item, request);
        products.Add(item);
        return ProductResponse.From(item);
    }
}

/// <summary>Reemplaza los datos del producto de la empresa activa; uno de otra empresa responde 404.</summary>
internal sealed class UpdateProductHandler(IProductStore products, IDocumentCatalogs catalogs, ITenantContext tenant)
    : IHandler<UpdateProductCommand, ProductResponse>
{
    public async Task<Result<ProductResponse>> HandleAsync(UpdateProductCommand request, CancellationToken ct)
    {
        if (tenant.CompanyId is not { } companyId || await products.FindAsync(companyId, request.Id, ct) is not { } item)
        {
            return MasterErrors.ProductNotFound;
        }

        if (await ProductWriter.CheckAsync(request, companyId, item.Id, products, catalogs, ct) is { } error)
        {
            return error;
        }

        ProductWriter.Apply(item, request);
        item.IsActive = request.IsActive ?? item.IsActive;
        return ProductResponse.From(item);
    }
}

/// <summary>Desactiva el producto; repetirlo no cambia nada.</summary>
internal sealed class DeactivateProductHandler(IProductStore products, ITenantContext tenant) : IHandler<DeactivateProductCommand, ProductResponse>
{
    public async Task<Result<ProductResponse>> HandleAsync(DeactivateProductCommand request, CancellationToken ct)
    {
        if (tenant.CompanyId is not { } companyId || await products.FindAsync(companyId, request.Id, ct) is not { } item)
        {
            return MasterErrors.ProductNotFound;
        }

        item.IsActive = false;
        return ProductResponse.From(item);
    }
}
