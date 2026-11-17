using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using ImpiFact.Application.Abstractions;
using ImpiFact.Application.Abstractions.Companies;
using ImpiFact.Application.Abstractions.Handlers;
using ImpiFact.Application.Abstractions.Identity;
using ImpiFact.Application.Abstractions.Results;
using ImpiFact.Application.Features.Companies.CreateCompany;
using ImpiFact.Application.Features.Companies.SetSoftware;
using ImpiFact.Application.Features.Companies.UploadCertificate;
using ImpiFact.Application.Features.Companies.UpsertResolution;
using ImpiFact.Application.Features.Identity.ApiKeys;
using ImpiFact.Infrastructure.Tenancy;
using Microsoft.Extensions.DependencyInjection;

namespace ImpiFact.Migrator;

/// <summary>
/// Comando <c>seed-demo</c>: deja lista una empresa de prueba para emitir en habilitación (el ambiente de pruebas de la
/// DIAN, Dirección de Impuestos y Aduanas Nacionales) desde una instalación local. Pasa por los mismos casos de uso que
/// la API (alta de la empresa, software, resoluciones, certificado y API key), con sus validaciones y su cifrado; no
/// escribe filas a mano. Es idempotente: la empresa, su software y sus resoluciones se actualizan en lugar de
/// duplicarse, el certificado vigente se conserva y en cada corrida se crea una API key nueva.
/// </summary>
/// <remarks>
/// Todos los datos son ficticios: el software, el PIN y el certificado autofirmado no existen ante la DIAN, que
/// rechaza lo que se le envíe con ellos. Sirven para emitir, firmar y consultar en local.
/// </remarks>
internal sealed class SeedDemo(IServiceProvider services, TextWriter output, TextWriter error, Func<string> newCertificatePassword)
{
    public const string Nit = "900123456";

    /// <summary>Dígito de verificación del NIT (número de identificación tributaria) 900123456.</summary>
    public const string Dv = "8";

    public const string BusinessName = "EMPRESA DEMO IMPIFACT SAS";

    /// <summary>Identificador de software ficticio, reconocible como de prueba («de30» = demo).</summary>
    public const string SoftwareId = "de30de30-0000-4000-8000-000000000001";

    /// <summary>PIN ficticio del software (5 dígitos, como pide la DIAN).</summary>
    public const string SoftwarePin = "58217";

    /// <summary>Servicio de la DIAN en habilitación, el mismo de <c>Dian:HabilitacionUrl</c> de la API.</summary>
    public const string HabilitationUrl = "https://vpfe-hab.dian.gov.co/WcfDianCustomerServices.svc";

    /// <summary>Clave técnica pública del set de pruebas de la DIAN para la resolución 18760000001.</summary>
    public const string TechnicalKey = "fc8eac422eba16e22ffd8c6f94b3f40a6e38162c";

    public const string KeyName = "Llave demo (seed-demo)";

    /// <summary>Los alcances que bastan para configurar la empresa, emitir y consultar.</summary>
    public static readonly string[] KeyScopes = [ApiScopes.DocumentsWrite, ApiScopes.DocumentsRead, ApiScopes.ConfigWrite];

    /// <summary>Un certificado se reemplaza si le quedan menos de estos días.</summary>
    private const int MinimumCertificateDays = 30;

    /// <summary>
    /// Resoluciones públicas de habilitación de la DIAN: factura SETP 990000000–995000000 con su clave técnica, y las
    /// de notas crédito (NC) y débito (ND), que no llevan número ni fechas.
    /// </summary>
    private static readonly UpsertResolutionCommand[] Resolutions =
    [
        new()
        {
            TypeDocumentId = 1, Prefix = "SETP", Resolution = "18760000001", ResolutionDate = "2019-01-19", TechnicalKey = TechnicalKey,
            From = 990000000, To = 995000000, DateFrom = "2019-01-19", DateTo = "2030-01-19",
        },
        new() { TypeDocumentId = 4, Prefix = "NC", From = 1, To = 99999999 },
        new() { TypeDocumentId = 5, Prefix = "ND", From = 1, To = 99999999 },
    ];

    public async Task<int> RunAsync(CancellationToken ct)
    {
        // 1. La empresa: el alta es una operación de plataforma, sin empresa en el contexto.
        var saved = await HandleAsync<CreateCompanyCommand, CompanySavedResponse>(companyId: null, CompanyCommand(), "la empresa", ct);
        if (saved is null)
        {
            return 1;
        }

        var companyId = saved.Company.Id;
        await output.WriteLineAsync(saved.Created
            ? $"Empresa demo creada: {BusinessName}, NIT {Nit}-{Dv}, en habilitación."
            : $"Empresa demo ya existía y se actualizó: {BusinessName}, NIT {Nit}-{Dv}.");

        // 2. Software ficticio de la factura, en el ambiente actual (habilitación).
        var software = new SetSoftwareCommand { Nit = Nit, Id = SoftwareId, Pin = SoftwarePin, Url = HabilitationUrl };
        if (await HandleAsync<SetSoftwareCommand, SoftwareSavedResponse>(companyId, software, "el software", ct) is null)
        {
            return 1;
        }

        await output.WriteLineAsync("Software de prueba: identificador y PIN ficticios (la DIAN no los conoce).");

        // 3. Resoluciones de factura, nota crédito y nota débito.
        foreach (var resolution in Resolutions)
        {
            if (await HandleAsync<UpsertResolutionCommand, ResolutionSavedResponse>(companyId, resolution with { Nit = Nit }, "las resoluciones", ct) is null)
            {
                return 1;
            }
        }

        await output.WriteLineAsync("Resoluciones: factura SETP 990000000-995000000 (resolución 18760000001, vigente hasta 2030-01-19), nota crédito NC y nota débito ND.");

        // 4. Certificado autofirmado, solo si no hay uno activo con vigencia suficiente.
        if (!await UploadCertificateIfNeededAsync(companyId, ct))
        {
            return 1;
        }

        // 5. Una API key nueva: la del alta si la empresa se acaba de crear, si no una más.
        var key = saved.Token;
        if (key is null)
        {
            var created = await HandleAsync<CreateApiKeyCommand, CreatedApiKey>(companyId, new CreateApiKeyCommand(KeyName, KeyScopes, ExpiresAt: null), "la API key", ct);
            if (created is null)
            {
                return 1;
            }

            key = created.Key;
        }

        await output.WriteLineAsync();
        await output.WriteLineAsync($"API key de la empresa (alcances {string.Join(", ", KeyScopes)}). Copie la llave ahora: no se vuelve a mostrar.");
        await output.WriteLineAsync(key);
        await output.WriteLineAsync();
        await output.WriteLineAsync("Siguiente paso: abra /scalar en la API, pulse «Authentication», pegue la llave y emita el ejemplo de factura precargado en POST /api/v1/documents/invoices.");
        return 0;
    }

    private static CreateCompanyCommand CompanyCommand() => new()
    {
        Nit = Nit,
        Dv = Dv,
        // Ids de los catálogos DIAN, los mismos de la empresa de los casos golden: NIT (6, código 31), persona jurídica
        // (1), responsable de IVA (1, código 48), agente de retención del IVA (14, código O-23) y Bogotá (149, 11001).
        TypeDocumentIdentificationId = 6,
        TypeOrganizationId = 1,
        TypeRegimeId = 1,
        TypeLiabilityId = 14,
        BusinessName = BusinessName,
        MerchantRegistration = "0000000-00",
        MunicipalityId = 149,
        Address = "CALLE DEMO # 1-01",
        Phone = "3000000000",
        Email = "demo@impifact.example",
        TypeEnvironmentId = 2,
    };

    private async Task<bool> UploadCertificateIfNeededAsync(Guid companyId, CancellationToken ct)
    {
        await using (var scope = OpenScope(companyId))
        {
            var clock = scope.ServiceProvider.GetRequiredService<IClock>();
            var active = await scope.ServiceProvider.GetRequiredService<ICompanyRepository>().GetActiveCertificateAsync(companyId, ct);
            if (active is not null && active.NotAfter > clock.UtcNow.AddDays(MinimumCertificateDays))
            {
                await output.WriteLineAsync($"Certificado: se conserva el activo, vigente hasta {Day(active.NotAfter)}.");
                return true;
            }
        }

        var password = newCertificatePassword();
        var notAfter = DateTimeOffset.UtcNow.AddYears(2);
        var upload = new UploadCertificateCommand
        {
            Nit = Nit,
            Certificate = Convert.ToBase64String(SelfSignedPfx(password, notAfter)),
            Password = password,
        };
        if (await HandleAsync<UploadCertificateCommand, CertificateSavedResponse>(companyId, upload, "el certificado", ct) is null)
        {
            return false;
        }

        await output.WriteLineAsync($"Certificado: autofirmado de prueba, vigente hasta {Day(notAfter)}. Sirve para firmar en local; la DIAN rechaza los documentos firmados con él.");
        return true;
    }

    /// <summary>
    /// PKCS#12 (archivo .p12 con el certificado y su llave privada) autofirmado con llave RSA de 2048 bits, como pide la
    /// firma de la DIAN. Su nombre dice que es de prueba.
    /// </summary>
    private static byte[] SelfSignedPfx(string password, DateTimeOffset notAfter)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            $"CN={BusinessName} CERTIFICADO DE PRUEBA, O=ImpiFact demo, SERIALNUMBER={Nit}, C=CO", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.NonRepudiation, critical: true));
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), notAfter);
        return certificate.Export(X509ContentType.Pkcs12, password);
    }

    /// <summary>
    /// Corre un caso de uso en un ámbito propio, como actor de sistema y, si se indica, sobre la empresa con los alcances
    /// de la llave demo. Si falla, escribe el error con los campos que no pasaron y devuelve nulo.
    /// </summary>
    private async Task<TOut?> HandleAsync<TIn, TOut>(Guid? companyId, TIn request, string step, CancellationToken ct)
        where TOut : class
    {
        await using var scope = OpenScope(companyId);
        var result = await scope.ServiceProvider.GetRequiredService<IHandler<TIn, TOut>>().HandleAsync(request, ct);
        if (result.IsSuccess)
        {
            return result.Value;
        }

        await error.WriteLineAsync($"No se pudo preparar {step}: {Describe(result.Error)}");
        return null;
    }

    private AsyncServiceScope OpenScope(Guid? companyId)
    {
        var scope = services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(
            companyId, userId: null, ActorType.System, companyId is null ? null : new HashSet<string>(KeyScopes));
        return scope;
    }

    private static string Describe(Error failure) =>
        failure.Fields.Count == 0
            ? failure.Message
            : failure.Message + " " + string.Join("; ", failure.Fields.Select(field => $"{field.Field}: {field.Message}"));

    private static string Day(DateTimeOffset moment) => moment.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
