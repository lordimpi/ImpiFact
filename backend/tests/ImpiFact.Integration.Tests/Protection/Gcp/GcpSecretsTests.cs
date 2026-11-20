using Google.Cloud.SecretManager.V1;
using ImpiFact.Application.Abstractions.Protection;
using Microsoft.Extensions.DependencyInjection;

namespace ImpiFact.Integration.Tests.Protection.Gcp;

/// <summary>
/// Proveedor <c>GcpSecrets</c> contra floci-gcp: las pruebas comunes de <see cref="CloudSecretsTests"/> y lo propio de
/// Secret Manager (cada certificado es un secreto del proyecto con replicación automática). floci-gcp no guarda las
/// etiquetas de un secreto, así que la etiqueta <c>company</c> no se puede revisar aquí.
/// </summary>
[TestFixture]
[Category("Integration")]
public sealed class GcpSecretsTests : CloudSecretsTests
{
    protected override string Scheme => "gcp";

    protected override string Provider => "GcpSecrets";

    protected override ServiceProvider Build(string? connectionString = null, bool otherKey = false) =>
        FlociGcp.Build(FlociGcp.Settings(otherKey ? FlociGcp.OtherKeyName : FlociGcp.KeyName), connectionString);

    protected override ServiceProvider BuildWithMissingKey() =>
        FlociGcp.Build(FlociGcp.Settings($"projects/{FlociGcp.ProjectId}/locations/global/keyRings/impifact/cryptoKeys/missing-key"));

    protected override Task<string> CreateKeysDatabaseAsync() => FlociGcp.CreateKeysDatabaseAsync();

    [Test]
    public async Task Each_certificate_is_a_project_secret_with_automatic_replication()
    {
        using var services = Build();
        var companyId = Guid.CreateVersion7();
        var secretRef = await services.GetRequiredService<ICertificateStore>().SaveAsync(companyId, TestPfx.Rsa(), TestPfx.Password, CancellationToken.None);
        var blobId = Guid.Parse(secretRef.Split('/')[1]);

        var secret = await FlociGcp.SecretsClient.GetSecretAsync(new SecretName(FlociGcp.ProjectId, $"impifact-cert-{companyId:N}-{blobId:N}"));

        Assert.That(secret.Replication.ReplicationCase, Is.EqualTo(Replication.ReplicationOneofCase.Automatic));
    }
}
