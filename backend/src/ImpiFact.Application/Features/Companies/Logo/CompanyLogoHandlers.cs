using System.Text.Json.Serialization;
using FluentValidation;
using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Companies;
using ImpiFact.Application.Abstractions.Handlers;
using ImpiFact.Application.Abstractions.Results;

namespace ImpiFact.Application.Features.Companies.Logo;

// Logo de la empresa (Arquitectura frontend, sección 7: /empresas/:id/logo). Va en el encabezado del PDF (la
// representación gráfica) de sus documentos. La fila de company_logos se guarda en la transacción del comando; el archivo
// va al almacén de archivos (ICompanyLogos).

/// <summary><c>PUT /api/v1/companies/{nit}/logo</c>: guarda el logo y reemplaza el anterior.</summary>
/// <param name="Content">Bytes del archivo subido; nulo si el formulario no trajo el campo <c>logo</c>.</param>
public sealed record SaveCompanyLogoCommand(string? Nit, byte[]? Content) : ICommand;

/// <summary><c>GET /api/v1/companies/{nit}/logo</c>.</summary>
public sealed record GetCompanyLogoQuery(string? Nit);

/// <summary><c>DELETE /api/v1/companies/{nit}/logo</c>.</summary>
public sealed record DeleteCompanyLogoCommand(string? Nit) : ICommand;

/// <summary>Logo guardado.</summary>
public sealed record CompanyLogoResponse
{
    /// <summary><c>image/png</c> o <c>image/jpeg</c>, según el contenido del archivo.</summary>
    [JsonPropertyName("content_type")]
    public string ContentType { get; init; } = "";

    [JsonPropertyName("size_bytes")]
    public int SizeBytes { get; init; }
}

/// <summary>Errores del logo.</summary>
internal static class CompanyLogoErrors
{
    public static readonly Error NotFound = new("company.logo_not_found", "La empresa no tiene logo.", ErrorType.NotFound);
}

/// <summary>
/// Solo PNG o JPEG, reconocidos por sus primeros bytes (el tipo que declara el formulario no cuenta), de 1 byte a 512 KiB.
/// El SVG no se acepta: puede llevar código y el PDF lo abre un navegador (Chromium, en Gotenberg).
/// </summary>
internal sealed class SaveCompanyLogoCommandValidator : AbstractValidator<SaveCompanyLogoCommand>
{
    public SaveCompanyLogoCommandValidator()
    {
        RuleFor(command => command.Content)
            .Cascade(CascadeMode.Stop)
            .NotNull().WithMessage("El campo logo es obligatorio.")
            .Must(content => content!.Length > 0).WithMessage("El campo logo es obligatorio.")
            .Must(content => content!.Length <= CompanyLogoFormat.MaxBytes)
            .WithMessage($"El logo no debe pesar más de {CompanyLogoFormat.MaxBytes / 1024} KiB.")
            .Must(content => CompanyLogoFormat.Detect(content) is not null).WithMessage("El logo debe ser una imagen PNG o JPEG.")
            .OverridePropertyName("logo");
    }
}

internal sealed class SaveCompanyLogoHandler(ICompanyRepository companies, ITenantContext tenant, ICompanyLogos logos)
    : IHandler<SaveCompanyLogoCommand, CompanyLogoResponse>
{
    public async Task<Result<CompanyLogoResponse>> HandleAsync(SaveCompanyLogoCommand request, CancellationToken ct)
    {
        var own = await CompanyAccess.OwnCompanyAsync(companies, tenant, request.Nit, ct);
        if (!own.IsSuccess)
        {
            return own.Error;
        }

        var content = request.Content!;
        var logo = new CompanyLogoImage(content, CompanyLogoFormat.Detect(content)!);
        await logos.SaveAsync(own.Value.Id, logo, ct);
        return new CompanyLogoResponse { ContentType = logo.ContentType, SizeBytes = content.Length };
    }
}

internal sealed class GetCompanyLogoHandler(ICompanyRepository companies, ITenantContext tenant, ICompanyLogos logos)
    : IHandler<GetCompanyLogoQuery, CompanyLogoImage>
{
    public async Task<Result<CompanyLogoImage>> HandleAsync(GetCompanyLogoQuery request, CancellationToken ct)
    {
        var own = await CompanyAccess.OwnCompanyAsync(companies, tenant, request.Nit, ct);
        if (!own.IsSuccess)
        {
            return own.Error;
        }

        return await logos.FindAsync(own.Value.Id, ct) is { } logo ? logo : CompanyLogoErrors.NotFound;
    }
}

/// <summary>Borra el logo; sin logo responde igual (es idempotente).</summary>
internal sealed class DeleteCompanyLogoHandler(ICompanyRepository companies, ITenantContext tenant, ICompanyLogos logos)
    : IHandler<DeleteCompanyLogoCommand, bool>
{
    public async Task<Result<bool>> HandleAsync(DeleteCompanyLogoCommand request, CancellationToken ct)
    {
        var own = await CompanyAccess.OwnCompanyAsync(companies, tenant, request.Nit, ct);
        if (!own.IsSuccess)
        {
            return own.Error;
        }

        await logos.DeleteAsync(own.Value.Id, ct);
        return true;
    }
}
