using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using PokeArena.Api.Modules.Identity.Application;
using PokeArena.Api.Modules.Identity.Infrastructure;

namespace PokeArena.Tests;

// PRUEBAS DEL ADAPTADOR GoogleIdentityProvider.
//
// Problema: no se pueden pedir tokens reales a Google dentro de una prueba automática.
// Solución: la prueba se hace pasar por Google.
// 1. Genera su propio par de claves RSA (pública y privada), como las que usa Google.
// 2. Le da al adaptador SOLO la clave pública, mediante StaticConfigurationManager, en
//    lugar del ConfigurationManager real que descarga las claves de internet.
// 3. Firma tokens con la clave privada, con los mismos datos que pondría Google.
// Así se prueban las cuatro comprobaciones que exige la US (firma, emisor, audiencia y
// expiración) exactamente con el mismo código que corre en producción.
public class GoogleIdentityProviderTests
{
    private const string OurClientId = "pokearena-client-id.apps.googleusercontent.com";
    private const string GoogleIssuer = "https://accounts.google.com";

    // "Clave de Google" falsa para la prueba y una segunda clave que Google NO publicó.
    private static readonly RsaSecurityKey GoogleKey = CreateRsaKey("clave-google");
    private static readonly RsaSecurityKey AttackerKey = CreateRsaKey("clave-atacante");

    [Fact]
    public async Task ValidToken_ReturnsTheIdentity()
    {
        var provider = CreateProvider();
        var token = CreateToken();

        var result = await provider.ValidateAsync(token, CancellationToken.None);

        Assert.Null(result.Error);
        Assert.Equal("Google", result.Identity!.Provider);
        Assert.Equal("108234567890", result.Identity.Subject);
        Assert.Equal("ash@pallet.com", result.Identity.Email);
    }

    // Google usa los dos formatos de emisor y su documentación pide aceptar ambos.
    [Fact]
    public async Task IssuerWithoutHttps_IsAlsoAccepted()
    {
        var result = await CreateProvider().ValidateAsync(CreateToken(issuer: "accounts.google.com"), CancellationToken.None);

        Assert.Null(result.Error);
    }

    // CRITERIO DE ERROR: token expirado.
    [Fact]
    public async Task ExpiredToken_IsRejected()
    {
        var token = CreateToken(expires: DateTime.UtcNow.AddMinutes(-10));

        var result = await CreateProvider().ValidateAsync(token, CancellationToken.None);

        AssertInvalidToken(result);
    }

    // CRITERIO DE ERROR: firma inválida (firmado con una clave que Google no publicó).
    [Fact]
    public async Task TokenSignedWithAnotherKey_IsRejected()
    {
        var token = CreateToken(signingKey: AttackerKey);

        var result = await CreateProvider().ValidateAsync(token, CancellationToken.None);

        AssertInvalidToken(result);
    }

    // CRITERIO DE ERROR: emitido para otra aplicación (otra audiencia).
    [Fact]
    public async Task TokenForAnotherApplication_IsRejected()
    {
        var token = CreateToken(audience: "otra-app.apps.googleusercontent.com");

        var result = await CreateProvider().ValidateAsync(token, CancellationToken.None);

        AssertInvalidToken(result);
    }

    // CRITERIO DE ERROR: no fue emitido por el proveedor (otro emisor).
    [Fact]
    public async Task TokenFromAnotherIssuer_IsRejected()
    {
        var token = CreateToken(issuer: "https://login.ejemplo-falso.com");

        var result = await CreateProvider().ValidateAsync(token, CancellationToken.None);

        AssertInvalidToken(result);
    }

    // Un texto que ni siquiera es un JWT también se rechaza sin romper nada.
    [Fact]
    public async Task GarbageText_IsRejected()
    {
        var result = await CreateProvider().ValidateAsync("esto-no-es-un-token", CancellationToken.None);

        AssertInvalidToken(result);
    }

    // Sin Client ID configurado no se puede validar la audiencia: 503 y no 500.
    [Fact]
    public async Task MissingClientId_ReturnsUnavailable()
    {
        var provider = CreateProvider(clientId: "");

        var result = await provider.ValidateAsync(CreateToken(), CancellationToken.None);

        Assert.Equal(ExternalIdentityResult.UnavailableCode, result.Error!.Code);
    }

    // CRITERIO DE ERROR: el proveedor no está disponible (no se pueden descargar sus claves).
    [Fact]
    public async Task GoogleUnreachable_ReturnsUnavailable()
    {
        var provider = new GoogleIdentityProvider(
            Options.Create(new GoogleAuthOptions { ClientId = OurClientId }),
            new UnreachableConfigurationManager());

        var result = await provider.ValidateAsync(CreateToken(), CancellationToken.None);

        Assert.Equal(ExternalIdentityResult.UnavailableCode, result.Error!.Code);
    }

    private static void AssertInvalidToken(ExternalIdentityResult result)
    {
        Assert.Null(result.Identity);
        Assert.Equal(ExternalIdentityResult.InvalidTokenCode, result.Error!.Code);
    }

    // Arma el adaptador con la clave pública "de Google" fija, sin internet.
    private static GoogleIdentityProvider CreateProvider(string clientId = OurClientId)
    {
        var configuration = new OpenIdConnectConfiguration { Issuer = GoogleIssuer };
        configuration.SigningKeys.Add(GoogleKey);

        return new GoogleIdentityProvider(
            Options.Create(new GoogleAuthOptions { ClientId = clientId }),
            new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration));
    }

    // Fabrica un ID token con la forma de los de Google. Cada parámetro permite romper
    // una sola cosa a la vez para probar cada comprobación por separado.
    private static string CreateToken(
        string issuer = GoogleIssuer,
        string audience = OurClientId,
        DateTime? expires = null,
        RsaSecurityKey? signingKey = null)
    {
        var expiresAt = expires ?? DateTime.UtcNow.AddMinutes(30);
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            // Si el token está expirado, también se emitió en el pasado.
            IssuedAt = expiresAt.AddHours(-1),
            NotBefore = expiresAt.AddHours(-1),
            Expires = expiresAt,
            SigningCredentials = new SigningCredentials(signingKey ?? GoogleKey, SecurityAlgorithms.RsaSha256),
            Claims = new Dictionary<string, object>
            {
                ["sub"] = "108234567890",
                ["email"] = "ash@pallet.com",
                ["email_verified"] = true
            }
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    private static RsaSecurityKey CreateRsaKey(string keyId) =>
        new(RSA.Create(2048)) { KeyId = keyId };

    // DOBLE: simula que Google no responde al pedir sus claves.
    private sealed class UnreachableConfigurationManager : IConfigurationManager<OpenIdConnectConfiguration>
    {
        public Task<OpenIdConnectConfiguration> GetConfigurationAsync(CancellationToken cancel) =>
            throw new InvalidOperationException("IDX20803: No se pudo obtener la configuración de Google.");

        public void RequestRefresh()
        {
        }
    }
}
