using System.Buffers.Text;
using System.Security.Cryptography;
using ImpiFact.Infrastructure.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace ImpiFact.Infrastructure.Identity;

/// <summary>
/// Llaves de los JWT (JSON Web Token) del panel. Se firma con ES256 (ECDSA, firma con curvas elípticas, sobre la
/// curva P-256 con SHA-256): la llave privada solo la tiene la API y la pública sirve para validar. Cada llave lleva
/// un kid (key id, identificador de llave en el encabezado del token) igual a su huella JWK (RFC 7638: resumen
/// SHA-256 de la parte pública), así una rotación no necesita configurar identificadores.
/// </summary>
/// <remarks>
/// Rotación: se pone la llave nueva en <c>Jwt:SigningKey</c> y la pública de la anterior en
/// <c>Jwt:PreviousPublicKeys</c>; los tokens ya emitidos siguen validando hasta vencer (15 minutos) y luego se quita
/// la anterior. En Development, si no hay llave configurada, se genera una temporal y se avisa en el registro.
/// </remarks>
public sealed class JwtSigningKeys : IDisposable
{
    public const string Algorithm = SecurityAlgorithms.EcdsaSha256;

    private readonly List<ECDsa> _owned = [];

    public JwtSigningKeys(IOptionsService<JwtOptions> options, IHostEnvironment environment, ILogger<JwtSigningKeys> logger)
    {
        var jwt = options.Value;
        ECDsa signing;
        if (string.IsNullOrWhiteSpace(jwt.SigningKey))
        {
            // La validación al arrancar ya impide llegar aquí fuera de Development.
            if (!environment.IsDevelopment())
            {
                throw new InvalidOperationException("A JWT signing key is required outside Development.");
            }

            signing = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            SigningKey = Own(signing);
            logger.LogWarning(
                "No JWT signing key is configured; using an ephemeral key generated for Development (kid {KeyId}). "
                + "Tokens stop validating when the API restarts. Set Jwt__SigningKey to keep them.",
                SigningKey.KeyId);
        }
        else
        {
            signing = EcdsaPem.TryImportPrivate(jwt.SigningKey)
                ?? throw new InvalidOperationException("The JWT signing key is not a valid P-256 private key.");
            SigningKey = Own(signing);
        }

        var validation = new List<ECDsaSecurityKey> { SigningKey };
        foreach (var pem in jwt.PreviousPublicKeys)
        {
            // La validación al arrancar ya lo rechaza; se repite por si alguien crea esta clase sin pasar por ella.
            if (EcdsaPem.ContainsPrivateKey(pem))
            {
                throw new InvalidOperationException("A previous JWT public key contains private key material.");
            }

            var previous = EcdsaPem.TryImportPublic(pem)
                ?? throw new InvalidOperationException("A previous JWT public key is not a valid P-256 public key.");
            validation.Add(Own(previous));
        }

        ValidationKeys = validation;
    }

    /// <summary>Llave privada con la que se firman los tokens nuevos.</summary>
    public ECDsaSecurityKey SigningKey { get; }

    /// <summary>Llaves que validan tokens: la actual primero y luego las anteriores que siguen en rotación.</summary>
    public IReadOnlyList<ECDsaSecurityKey> ValidationKeys { get; }

    /// <summary>
    /// Partes públicas de <see cref="ValidationKeys"/> en formato JWK (JSON Web Key, RFC 7517), para publicarlas en
    /// <c>/.well-known/jwks.json</c>. Nunca incluyen la parte privada.
    /// </summary>
    public IReadOnlyList<IReadOnlyDictionary<string, string>> PublicJsonWebKeys() =>
        [.. ValidationKeys.Select(key =>
        {
            var point = key.ECDsa.ExportParameters(includePrivateParameters: false).Q;
            return (IReadOnlyDictionary<string, string>)new Dictionary<string, string>
            {
                ["kty"] = "EC",
                ["crv"] = "P-256",
                ["x"] = Base64Url.EncodeToString(point.X),
                ["y"] = Base64Url.EncodeToString(point.Y),
                ["kid"] = key.KeyId,
                ["use"] = "sig",
                ["alg"] = Algorithm,
            };
        })];

    public void Dispose()
    {
        foreach (var key in _owned)
        {
            key.Dispose();
        }
    }

    private ECDsaSecurityKey Own(ECDsa key)
    {
        _owned.Add(key);
        var securityKey = new ECDsaSecurityKey(key);
        securityKey.KeyId = Base64UrlEncoder.Encode(JsonWebKeyConverter.ConvertFromECDsaSecurityKey(securityKey).ComputeJwkThumbprint());
        return securityKey;
    }
}

/// <summary>
/// Crea <see cref="JwtSigningKeys"/> al arrancar (después de validar las opciones), para que en Development el
/// aviso de la llave temporal salga en el registro de inicio y no con la primera solicitud.
/// </summary>
internal sealed class JwtSigningKeysWarmup(JwtSigningKeys keys) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        GC.KeepAlive(keys);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
