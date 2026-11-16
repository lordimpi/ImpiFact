using ImpiFact.Application.Abstractions.Companies;
using ImpiFact.Domain.Companies;
using Mapster;

namespace ImpiFact.Application.Features.Companies;

/// <summary>
/// Mapeo explícito de las entidades del módulo a sus respuestas (Arquitectura detallada, sección 4.2). Mapster es una
/// librería que copia objetos de un tipo a otro; con <see cref="TypeAdapterConfig.RequireDestinationMemberSource"/>
/// cada campo de la respuesta necesita un origen declarado o con el mismo nombre, así un campo nuevo no queda vacío
/// sin que nadie lo note.
/// </summary>
public sealed class CompanyMapping : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<Company, CompanyResponse>()
            .Ignore(response => response.TypeLiabilityIds, response => response.Environments);

        config.NewConfig<CompanyEnvironment, EnvironmentResponse>()
            .Map(response => response.DocumentKind, environment => (int)environment.DocumentKind);

        config.NewConfig<CompanySoftware, SoftwareResponse>()
            .Map(response => response.DocumentKind, software => (int)software.DocumentKind)
            .Map(response => response.Identifier, software => software.SoftwareIdentifier)
            .Map(response => response.Url, software => software.DianUrl);

        config.NewConfig<Resolution, ResolutionResponse>()
            .Map(response => response.TypeDocumentId, resolution => resolution.DocumentTypeId)
            .Map(response => response.Resolution, resolution => resolution.ResolutionNumber)
            .Map(response => response.From, resolution => resolution.RangeFrom)
            .Map(response => response.To, resolution => resolution.RangeTo)
            .Map(response => response.DateFrom, resolution => resolution.ValidFrom)
            .Map(response => response.DateTo, resolution => resolution.ValidTo);

        // Los días restantes dependen de la fecha de hoy: los pone el caso de uso.
        config.NewConfig<Certificate, CertificateResponse>().Ignore(response => response.DaysLeft);
        config.NewConfig<CertificateMetadata, CertificateResponse>().Ignore(response => response.DaysLeft);
    }
}

/// <summary>Configuración de Mapster del módulo, armada y compilada una sola vez.</summary>
internal static class CompanyMapper
{
    public static TypeAdapterConfig Config { get; } = Build();

    public static TDestination Map<TDestination>(object source) => source.Adapt<TDestination>(Config);

    private static TypeAdapterConfig Build()
    {
        var config = new TypeAdapterConfig { RequireDestinationMemberSource = true };
        new CompanyMapping().Register(config);
        config.Compile();
        return config;
    }
}
