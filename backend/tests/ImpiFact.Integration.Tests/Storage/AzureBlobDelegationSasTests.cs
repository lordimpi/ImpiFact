using System.Web;
using Azure;
using Azure.Core;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using ImpiFact.Infrastructure.Options;
using ImpiFact.Infrastructure.Storage;

namespace ImpiFact.Integration.Tests.Storage;

/// <summary>
/// Con la identidad de la carga de trabajo (sin llave de cuenta), las URL firmadas de <c>AzureBlob</c> se firman con una
/// llave de delegación de usuario que se pide al servicio una vez y se reutiliza. Azurite solo firma con la llave de
/// cuenta, así que aquí el servicio es un cliente falso que entrega la llave; no necesita Docker.
/// </summary>
[TestFixture]
public sealed class AzureBlobDelegationSasTests
{
    [Test]
    public void Signed_urls_use_one_user_delegation_key_for_several_links()
    {
        var service = new DelegationOnlyServiceClient();
        var storage = new AzureBlobStorage(service, new AzureBlobStorageOptions
        {
            ServiceUri = service.Uri.ToString(),
            Container = "impifact-documents",
            SignedUrlMaxMinutes = 60,
        });

        var first = storage.CreateSignedUrl("company/documents/invoice.pdf", TimeSpan.FromMinutes(10));
        var second = storage.CreateSignedUrl("company/documents/invoice.xml", TimeSpan.FromMinutes(60));
        var query = HttpUtility.ParseQueryString(first.Query);

        Assert.Multiple(() =>
        {
            Assert.That(service.DelegationKeyRequests, Is.EqualTo(1), "The delegation key is reused while it covers the link.");
            Assert.That(first.AbsolutePath, Is.EqualTo("/impifact-documents/company/documents/invoice.pdf"));
            Assert.That(first.Scheme, Is.EqualTo(Uri.UriSchemeHttps));
            Assert.That(query["skoid"], Is.EqualTo(DelegationOnlyServiceClient.ObjectId), "Signed with the delegation key.");
            Assert.That(query["sp"], Is.EqualTo("r"));
            Assert.That(query["sr"], Is.EqualTo("b"));
            Assert.That(query["spr"], Is.EqualTo("https"));
            Assert.That(query["sig"], Is.Not.Null.And.Not.Empty);
            Assert.That(second.AbsolutePath, Does.EndWith("/invoice.xml"));
        });
    }

    /// <summary>Cliente de una cuenta con identidad (no puede firmar con llave de cuenta) que entrega la llave de delegación.</summary>
    private sealed class DelegationOnlyServiceClient()
        : BlobServiceClient(new Uri("https://impifactdocs.blob.core.windows.net"), new StaticToken())
    {
        public const string ObjectId = "00000000-0000-0000-0000-0000000000aa";

        public int DelegationKeyRequests { get; private set; }

        public override Response<UserDelegationKey> GetUserDelegationKey(
            BlobGetUserDelegationKeyOptions options, CancellationToken cancellationToken = default)
        {
            DelegationKeyRequests++;
            // Por posición: objeto, inquilino, inicio, vencimiento, servicio, versión y la llave en base64.
            var key = BlobsModelFactory.UserDelegationKey(
                ObjectId,
                "00000000-0000-0000-0000-0000000000bb",
                options.StartsOn ?? DateTimeOffset.UtcNow,
                options.ExpiresOn,
                "b",
                "2025-01-05",
                Convert.ToBase64String(new byte[32]));
            return Response.FromValue(key, null!);
        }
    }

    private sealed class StaticToken : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            new("test-token", DateTimeOffset.UtcNow.AddHours(1));

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            ValueTask.FromResult(GetToken(requestContext, cancellationToken));
    }
}
